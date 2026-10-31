# /// script
# requires-python = ">=3.12"
# dependencies = [
#     "lz4>=4.4.5",
#     "pillow>=13.0.0",
#     "zstandard>=0.25.0",
# ]
# ///
"""Read-only analysis of game texture caches; writes experiments under --output."""
import argparse
import ctypes as ct
import hashlib
import json
from pathlib import Path
import struct
import time
import zlib

import lz4.block
import zstandard
from PIL import Image


class Cabinet:
    def __init__(self, algorithm):
        self.api = ct.WinDLL("cabinet.dll", use_last_error=True)
        for name in ("CreateCompressor", "CreateDecompressor"):
            f = getattr(self.api, name)
            f.argtypes = [ct.c_uint32, ct.c_void_p, ct.POINTER(ct.c_void_p)]
            f.restype = ct.c_int
        for name in ("Compress", "Decompress"):
            f = getattr(self.api, name)
            f.argtypes = [ct.c_void_p, ct.c_void_p, ct.c_size_t, ct.c_void_p,
                          ct.c_size_t, ct.POINTER(ct.c_size_t)]
            f.restype = ct.c_int
        for name in ("CloseCompressor", "CloseDecompressor"):
            getattr(self.api, name).argtypes = [ct.c_void_p]
        self.compressor, self.decoder = ct.c_void_p(), ct.c_void_p()
        if not self.api.CreateCompressor(algorithm, None, ct.byref(self.compressor)):
            raise ct.WinError(ct.get_last_error())
        if not self.api.CreateDecompressor(algorithm, None, ct.byref(self.decoder)):
            raise ct.WinError(ct.get_last_error())

    def compress(self, raw):
        output = ct.create_string_buffer(len(raw) + len(raw) // 16 + 65536)
        size = ct.c_size_t()
        if not self.api.Compress(self.compressor, ct.c_char_p(raw), len(raw), output,
                                 len(output), ct.byref(size)):
            raise ct.WinError(ct.get_last_error())
        return output.raw[:size.value]

    def decompress(self, encoded, length):
        output = ct.create_string_buffer(length)
        size = ct.c_size_t()
        if not self.api.Decompress(self.decoder, ct.c_char_p(encoded), len(encoded),
                                   output, length, ct.byref(size)):
            raise ct.WinError(ct.get_last_error())
        if size.value != length:
            raise ValueError("native decoded length mismatch")
        return output.raw

    def close(self):
        self.api.CloseCompressor(self.compressor)
        self.api.CloseDecompressor(self.decoder)


def deflate(raw):
    encoder = zlib.compressobj(6, zlib.DEFLATED, -15)
    return encoder.compress(raw) + encoder.flush()


def read_cache(path):
    data = path.read_bytes()
    magic, schema, kind = struct.unpack_from("<iii", data)
    if magic != 0x46434844 or kind != 2 or schema not in (1, 2):
        raise ValueError(f"not a supported texture cache: {path}")
    width, height, fmt, mips, length = struct.unpack_from("<iiiii", data, 76)
    raw = data[96:]
    if schema == 2:
        codec, stored = struct.unpack_from("<ii", data, 96)
        if codec not in (0, 1) or stored != len(data) - 104:
            raise ValueError("compressed cache header")
        raw = data[104:]
        if codec == 1:
            raw = lz4.block.decompress(raw, uncompressed_size=length)
    if length != len(raw):
        raise ValueError("cache length mismatch")
    if hashlib.sha256(data[76:96] + raw).digest() != data[44:76]:
        raise ValueError(f"cache checksum mismatch: {path}")
    return raw, dict(width=width, height=height, format=fmt, mips=mips)


def alpha_stats(raw, row, source):
    if row["format"] not in (4, 5, 14):
        return {}
    offset = 0 if row["format"] == 5 else 3
    alpha = raw[:row["width"] * row["height"] * 4][offset::4]
    with Image.open(source) as image:
        original = image.convert("RGBA").getchannel("A").histogram()
    # A histogram comparison is independent of Unity's vertical image orientation.
    if Image.frombytes("L", (row["width"], row["height"]), alpha).histogram() != original:
        raise ValueError(f"cached alpha differs from source PNG: {source}")
    return dict(base_alpha_opaque=alpha.count(255) == len(alpha),
                base_alpha_partial=len(alpha) - alpha.count(255) - alpha.count(0))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--cache", type=Path, required=True)
    parser.add_argument("--resources", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--alpha-only", action="store_true")
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    names = {}
    for source in args.resources.rglob("t_*.png"):
        key = hashlib.sha256(str(source.resolve()).upper().encode("utf-8")).hexdigest()
        names[key] = source.relative_to(args.resources).as_posix()
    if args.alpha_only:
        report_path = args.output / "compression-sizes.json"
        report = json.loads(report_path.read_text(encoding="utf-8"))
        for row in report["rows"]:
            raw, _ = read_cache(args.cache / row["file"])
            row.update(alpha_stats(raw, row, args.resources / row["label"]))
        report["opaque"] = sum(row.get("base_alpha_opaque", False) for row in report["rows"])
        report["partial_alpha_textures"] = sum(row.get("base_alpha_partial", 0) > 0 for row in report["rows"])
        report_path.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
        print(json.dumps({k: v for k, v in report.items() if k != "rows"}, indent=2))
        return
    cabinet = {name: Cabinet(alg) for name, alg in [("mszip", 2), ("xpress", 3), ("xpress_huff", 4)]}
    zstd = zstandard.ZstdCompressor(level=3)
    algorithms = {
        "lz4": (lambda data: lz4.block.compress(data, store_size=False),
                lambda data, n: lz4.block.decompress(data, uncompressed_size=n)),
        "lz4hc9": (lambda data: lz4.block.compress(data, mode="high_compression", compression=9, store_size=False),
                   lambda data, n: lz4.block.decompress(data, uncompressed_size=n)),
        "deflate6": (deflate, lambda data, n: zlib.decompress(data, -15)),
        "zstd3": (zstd.compress, lambda data, n: zstandard.ZstdDecompressor().decompress(data)),
    }
    algorithms.update({name: (codec.compress, codec.decompress) for name, codec in cabinet.items()})
    rows = []
    paths = sorted(args.cache.glob("*.texture"))
    try:
        for index, path in enumerate(paths):
            raw, row = read_cache(path)
            row.update(file=path.name, label=names.get(path.stem, path.stem), raw_bytes=len(raw), sizes={}, encode_ms={})
            row.update(alpha_stats(raw, row, args.resources / row["label"]))
            for name, (encode, decode) in algorithms.items():
                start = time.perf_counter()
                encoded = encode(raw)
                row["encode_ms"][name] = (time.perf_counter() - start) * 1000
                row["sizes"][name] = len(encoded)
                if decode(encoded, len(raw)) != raw:
                    raise ValueError(f"lossless roundtrip failed: {path}, {name}")
            rows.append(row)
            print(f"{index + 1}/{len(paths)} {row['label']} raw={len(raw)} huff={row['sizes']['xpress_huff']} lz4hc={row['sizes']['lz4hc9']}", flush=True)
        totals = {name: sum(row["sizes"][name] + 96 for row in rows) for name in algorithms}
        report = dict(count=len(rows), raw_file_bytes=sum(row["raw_bytes"] + 96 for row in rows),
                      source_cache_file_bytes=sum(p.stat().st_size for p in paths),
                      totals=totals, opaque=sum(row.get("base_alpha_opaque", False) for row in rows),
                      encode_ms={name: sum(row["encode_ms"][name] for row in rows) for name in algorithms}, rows=rows)
        (args.output / "compression-sizes.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
        # Keep just three representative fixtures, not another full copy of the 1 GiB cache.
        selected = [next(row for row in rows if "144-Anya/" in row["label"]),
                    min(rows, key=lambda row: row["raw_bytes"]),
                    max(rows, key=lambda row: row["sizes"]["xpress_huff"] / row["raw_bytes"])]
        manifest = []
        for index, row in enumerate({row["file"]: row for row in selected}.values()):
            raw, _ = read_cache(args.cache / row["file"])
            prefix = f"sample{index}"
            (args.output / (prefix + ".raw")).write_bytes(raw)
            digest = hashlib.sha256(raw).hexdigest()
            manifest.append([prefix, "raw", row["label"], len(raw), digest, digest])
            for name, (encode, _) in algorithms.items():
                encoded = encode(raw)
                (args.output / (prefix + "." + name)).write_bytes(encoded)
                manifest.append([prefix, name, row["label"], len(raw), digest, hashlib.sha256(encoded).hexdigest()])
        (args.output / "samples.tsv").write_text("\n".join("\t".join(map(str, row)) for row in manifest) + "\n", encoding="utf-8")
        print(json.dumps({k: v for k, v in report.items() if k != "rows"}, ensure_ascii=False, indent=2), flush=True)
    finally:
        for codec in cabinet.values():
            codec.close()


if __name__ == "__main__":
    main()
