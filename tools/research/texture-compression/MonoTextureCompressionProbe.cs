// Experimental lossless-cache decoder comparison. Never loads UnityPlayer or creates Unity objects.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;

namespace OC2DIYChef.Research
{
    public static class MonoCacheProbe
    {
        sealed class Destination : IDisposable
        {
            internal IntPtr Pointer;
            internal Destination(int length) { Pointer = Marshal.AllocHGlobal(length); }
            public void Dispose() { if (Pointer != IntPtr.Zero) { Marshal.FreeHGlobal(Pointer); Pointer = IntPtr.Zero; } }
        }

        sealed class Decoder : IDisposable
        {
            readonly string codec;
            IntPtr handle;
            IntPtr module;
            [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Lz4Decode(IntPtr source, IntPtr destination, int sourceLength, int destinationLength);
            [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate UIntPtr ZstdDecode(IntPtr destination, UIntPtr capacity, IntPtr source, UIntPtr sourceLength);
            Lz4Decode lz4;
            ZstdDecode zstd;
            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
            [DllImport("kernel32.dll", CharSet = CharSet.Ansi)] static extern IntPtr GetProcAddress(IntPtr module, string name);
            [DllImport("kernel32.dll")] static extern bool FreeLibrary(IntPtr module);
            [DllImport("cabinet.dll", SetLastError = true)] static extern bool CreateDecompressor(uint algorithm, IntPtr routines, out IntPtr decoder);
            [DllImport("cabinet.dll", SetLastError = true)] static extern bool Decompress(IntPtr decoder, IntPtr input, UIntPtr inputLength,
                IntPtr output, UIntPtr outputLength, out UIntPtr written);
            [DllImport("cabinet.dll")] static extern bool CloseDecompressor(IntPtr decoder);
            internal Decoder(string name)
            {
                codec = name;
                if (name.EndsWith("_native") || name == "zstd3")
                {
                    string variable = name == "zstd3" ? "DIYCHEF_ZSTD_PROBE_DLL" : "DIYCHEF_LZ4_PROBE_DLL";
                    string path = Environment.GetEnvironmentVariable(variable);
                    if (string.IsNullOrEmpty(path)) throw new DllNotFoundException(variable + " not set");
                    module = LoadLibraryEx(path, IntPtr.Zero, 8);
                    if (module == IntPtr.Zero) throw new DllNotFoundException(path + ": error " + Marshal.GetLastWin32Error());
                    string symbol = name == "zstd3" ? "ZSTD_decompress" : "LZ4_decompress_safe";
                    IntPtr method = GetProcAddress(module, symbol);
                    if (method == IntPtr.Zero) { FreeLibrary(module); module = IntPtr.Zero; throw new EntryPointNotFoundException(symbol); }
                    if (name == "zstd3") zstd = (ZstdDecode)Marshal.GetDelegateForFunctionPointer(method, typeof(ZstdDecode));
                    else lz4 = (Lz4Decode)Marshal.GetDelegateForFunctionPointer(method, typeof(Lz4Decode));
                }
                uint algorithm = name == "mszip" ? 2U : name == "xpress" ? 3U : name == "xpress_huff" ? 4U : 0;
                if (algorithm != 0 && !CreateDecompressor(algorithm, IntPtr.Zero, out handle))
                    throw new IOException("CreateDecompressor error " + Marshal.GetLastWin32Error());
            }
            internal unsafe void Decode(IntPtr input, int length, IntPtr output, int expected)
            {
                if (codec == "raw") return;
                if (lz4 != null)
                {
                    if (lz4(input, output, length, expected) != expected) throw new FormatException("native LZ4 output length");
                    return;
                }
                if (zstd != null)
                {
                    if (zstd(output, new UIntPtr((uint)expected), input, new UIntPtr((uint)length)).ToUInt64() != (ulong)expected)
                        throw new FormatException("native Zstd output length");
                    return;
                }
                if (codec == "lz4" || codec == "lz4hc9") { DecodeLz4(input, length, output, expected); return; }
                if (codec == "deflate6")
                {
                    using (var source = new UnmanagedMemoryStream((byte*)input, length))
                    using (var inflater = new DeflateStream(source, CompressionMode.Decompress))
                    {
                        byte[] chunk = new byte[65536];
                        int offset = 0, read;
                        while ((read = inflater.Read(chunk, 0, Math.Min(chunk.Length, expected - offset))) != 0)
                        {
                            Marshal.Copy(chunk, 0, NativePixelBuffer.Offset(output, offset), read);
                            offset += read;
                        }
                        if (offset != expected) throw new FormatException("Deflate output length");
                    }
                    return;
                }
                UIntPtr written;
                if (!Decompress(handle, input, new UIntPtr((uint)length), output, new UIntPtr((uint)expected), out written) ||
                    written.ToUInt64() != (ulong)expected)
                    throw new IOException("Decompress error " + Marshal.GetLastWin32Error());
            }
            public void Dispose()
            {
                if (handle != IntPtr.Zero) { CloseDecompressor(handle); handle = IntPtr.Zero; }
                if (module != IntPtr.Zero) { FreeLibrary(module); module = IntPtr.Zero; }
            }
        }

        // Research prototype of the public LZ4 block format, not a shipping codec.
        static unsafe void DecodeLz4(IntPtr input, int length, IntPtr output, int expected)
        {
            byte* ip = (byte*)input, end = ip + length;
            byte* op = (byte*)output, start = op, limit = op + expected;
            while (ip < end)
            {
                int token = *ip++, literals = token >> 4;
                if (literals == 15)
                {
                    int extension;
                    do { if (ip >= end) throw new FormatException("LZ4 literal length"); extension = *ip++; literals = checked(literals + extension); }
                    while (extension == 255);
                }
                if (literals > end - ip || literals > limit - op) throw new FormatException("LZ4 literal bounds");
                while (literals >= 8) { *(ulong*)op = *(ulong*)ip; op += 8; ip += 8; literals -= 8; }
                while (literals-- > 0) *op++ = *ip++;
                if (ip == end) { if (op != limit) throw new FormatException("LZ4 output length"); return; }
                if (end - ip < 2) throw new FormatException("LZ4 offset");
                int distance = ip[0] | ip[1] << 8; ip += 2;
                if (distance == 0 || distance > op - start) throw new FormatException("LZ4 match offset");
                int match = 4 + (token & 15);
                if ((token & 15) == 15)
                {
                    int extension;
                    do { if (ip >= end) throw new FormatException("LZ4 match length"); extension = *ip++; match = checked(match + extension); }
                    while (extension == 255);
                }
                if (match > limit - op) throw new FormatException("LZ4 match bounds");
                if (distance < 8)
                {
                    int safeDistance = (8 + distance - 1) / distance * distance;
                    int prime = Math.Min(match, safeDistance);
                    for (int i = 0; i < prime; i++) { *op = *(op - distance); op++; }
                    match -= prime;
                    distance = safeDistance;
                }
                while (match >= 8) { *(ulong*)op = *(ulong*)(op - distance); op += 8; match -= 8; }
                while (match-- > 0) { *op = *(op - distance); op++; }
            }
            if (op != limit) throw new FormatException("LZ4 final output length");
        }

        static string Hash(IntPtr pointer, int length)
        {
            using (var digest = AssetHash.Create())
            {
                digest.Append(pointer, length);
                return BitConverter.ToString(digest.Finish()).Replace("-", "").ToLowerInvariant();
            }
        }
        static double Measure(Action action)
        {
            action();
            var samples = new List<double>();
            for (int i = 0; i < 5; i++)
            {
                GC.Collect(); GC.WaitForPendingFinalizers();
                var timer = Stopwatch.StartNew(); action(); timer.Stop();
                samples.Add(timer.Elapsed.TotalMilliseconds);
            }
            samples.Sort(); return samples[2];
        }
        public static void Run()
        {
            AssetHash.Warning = Console.WriteLine;
            AssetHash.Timings = null;
            string root = Environment.GetEnvironmentVariable("DIYCHEF_COMPRESSION_DIR");
            Console.WriteLine("PointerSize=" + IntPtr.Size + " CLR=" + Environment.Version);
            using (var report = new StreamWriter(Path.Combine(root, "mono-decompression.csv"), false))
            {
                report.WriteLine("sample,codec,label,raw_bytes,stored_bytes,decode_median_ms,read_storedhash_decode_median_ms,read_decode_pixelhash_median_ms");
                var inputs = new List<string[]>();
                foreach (string line in File.ReadAllLines(Path.Combine(root, "samples.tsv")))
                {
                    var fields = line.Split('\t'); inputs.Add(fields);
                    if (fields[1] == "lz4" || fields[1] == "lz4hc9")
                    { var native = (string[])fields.Clone(); native[1] += "_native"; inputs.Add(native); }
                }
                foreach (string[] fields in inputs)
                {
                    string codec = fields[1];
                    string path = Path.Combine(root, fields[0] + "." + codec.Replace("_native", ""));
                    int rawLength = int.Parse(fields[3], CultureInfo.InvariantCulture);
                    int storedLength = checked((int)new FileInfo(path).Length);
                    try
                    {
                        using (var decoder = new Decoder(codec))
                        using (var source = File.OpenRead(path))
                        using (var bytes = NativePixelBuffer.Read(source, storedLength, null))
                        using (var destination = new Destination(rawLength))
                        using (AssetHash.BeginBatch())
                        {
                            decoder.Decode(bytes.Pointer, storedLength, destination.Pointer, rawLength);
                            if (Hash(codec == "raw" ? bytes.Pointer : destination.Pointer, rawLength) != fields[4])
                                throw new Exception("decoded pixels differ from original SHA-256");
                            double decode = codec == "raw" ? 0 : Measure(delegate
                            { decoder.Decode(bytes.Pointer, storedLength, destination.Pointer, rawLength); });
                            double load = Measure(delegate
                            {
                                using (var stream = File.OpenRead(path))
                                using (var input = NativePixelBuffer.Read(stream, storedLength, null))
                                {
                                    if (Hash(input.Pointer, storedLength) != fields[5]) throw new Exception("stored checksum mismatch");
                                    if (codec != "raw")
                                        using (var output = new Destination(rawLength))
                                            decoder.Decode(input.Pointer, storedLength, output.Pointer, rawLength);
                                }
                            });
                            double pixelValidated = Measure(delegate
                            {
                                using (var stream = File.OpenRead(path))
                                using (var input = NativePixelBuffer.Read(stream, storedLength, null))
                                {
                                    if (codec == "raw")
                                    {
                                        if (Hash(input.Pointer, storedLength) != fields[4]) throw new Exception("raw pixel checksum mismatch");
                                    }
                                    else using (var output = new Destination(rawLength))
                                    {
                                        decoder.Decode(input.Pointer, storedLength, output.Pointer, rawLength);
                                        if (Hash(output.Pointer, rawLength) != fields[4]) throw new Exception("decoded pixel checksum mismatch");
                                    }
                                }
                            });
                            string row = string.Join(",", new[] { fields[0], codec, fields[2], fields[3], storedLength.ToString(),
                                decode.ToString("F3", CultureInfo.InvariantCulture), load.ToString("F3", CultureInfo.InvariantCulture),
                                pixelValidated.ToString("F3", CultureInfo.InvariantCulture) });
                            Console.WriteLine(row); report.WriteLine(row); report.Flush();
                        }
                    }
                    catch (DllNotFoundException error) { Console.WriteLine("UNAVAILABLE " + codec + ": " + error.Message); }
                    catch (EntryPointNotFoundException error) { Console.WriteLine("UNAVAILABLE " + codec + ": " + error.Message); }
                }
            }
            if (NativePixelBuffer.AllocatedBytes != 0) throw new Exception("Native input buffers retained");
        }
    }
}
