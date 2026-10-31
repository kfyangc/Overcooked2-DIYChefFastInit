using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace OC2DIYChef
{
    internal static class Lz4Codec
    {
        const string Resource = "OC2DIYChef.Native.LZ4.x86";
        const string Digest = "803075f5021d5a1eb0b7fa75c6bb1675ddb01f1f4fc99f6e118a62f2b594c6c2";
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Bound(int length);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Encode(IntPtr input, IntPtr output, int length, int capacity, int level);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Decode(IntPtr input, IntPtr output, int length, int capacity);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi)] static extern IntPtr GetProcAddress(IntPtr module, string name);
        [DllImport("kernel32.dll")] static extern bool FreeLibrary(IntPtr module);
        static readonly object gate = new object();
        static IntPtr module;
        static Bound bound;
        static Encode encode;
        static Decode decode;
        internal static bool Available { get { return module != IntPtr.Zero && decode != null; } }

        static string Hash(Stream stream) { return BitConverter.ToString(AssetHash.Compute(stream)).Replace("-", "").ToLowerInvariant(); }
        static T Function<T>(string name) where T : class
        {
            IntPtr address = GetProcAddress(module, name);
            if (address == IntPtr.Zero) throw new EntryPointNotFoundException(name);
            return Marshal.GetDelegateForFunctionPointer(address, typeof(T)) as T;
        }
        internal static bool Initialise(string directory, Action<string> log)
        {
            lock (gate)
            {
                if (Available) return true;
                if (IntPtr.Size != 4 || Environment.OSVersion.Platform != PlatformID.Win32NT) return false;
                try
                {
                    Directory.CreateDirectory(directory);
                    string licensePath = Path.Combine(directory, "LZ4-LICENSE.txt");
                    using (var license = Assembly.GetExecutingAssembly().GetManifestResourceStream("OC2DIYChef.Native.LZ4.LICENSE"))
                        if (license != null && !File.Exists(licensePath))
                            using (var text = new StreamReader(license)) File.WriteAllText(licensePath, text.ReadToEnd());
                    string path = Path.GetFullPath(Path.Combine(directory, "lz4-x86-" + Digest.Substring(0, 16) + ".dll"));
                    bool valid = false;
                    if (File.Exists(path)) using (var input = File.OpenRead(path)) valid = Hash(input) == Digest;
                    if (!valid)
                    {
                        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                        try
                        {
                            using (var input = Assembly.GetExecutingAssembly().GetManifestResourceStream(Resource))
                            using (var output = File.Create(temporary))
                            {
                                if (input == null) throw new FileNotFoundException("Embedded LZ4 library missing");
                                byte[] buffer = new byte[65536]; int count;
                                while ((count = input.Read(buffer, 0, buffer.Length)) != 0) output.Write(buffer, 0, count);
                            }
                            using (var input = File.OpenRead(temporary)) if (Hash(input) != Digest) throw new FormatException("Embedded LZ4 checksum");
                            if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
                        }
                        finally { if (File.Exists(temporary)) File.Delete(temporary); }
                    }
                    module = LoadLibraryEx(path, IntPtr.Zero, 8);
                    if (module == IntPtr.Zero) throw new IOException("LZ4 load error " + Marshal.GetLastWin32Error());
                    bound = Function<Bound>("LZ4_compressBound");
                    encode = Function<Encode>("LZ4_compress_HC");
                    decode = Function<Decode>("LZ4_decompress_safe");
                    // Keep the module loaded for the process: background cache jobs may
                    // finish after the plugin object is destroyed during game shutdown.
                    if (log != null) log("Texture cache compression=LZ4HC level=9; native=x86");
                    return true;
                }
                catch (Exception error)
                {
                    decode = null;
                    if (module != IntPtr.Zero) { FreeLibrary(module); module = IntPtr.Zero; }
                    if (log != null) log("LZ4 unavailable; keeping raw texture cache: " + error.Message);
                    return false;
                }
            }
        }
        internal static int CompressBound(int length)
        { if (!Available) throw new InvalidOperationException("LZ4 unavailable"); return bound(length); }
        internal static int Compress(IntPtr input, int length, IntPtr output, int capacity)
        {
            if (!Available) throw new InvalidOperationException("LZ4 unavailable");
            int written = encode(input, output, length, capacity, 9);
            if (written < 1) throw new FormatException("LZ4HC compression failed");
            return written;
        }
        internal static void Decompress(IntPtr input, int length, IntPtr output, int expected)
        {
            if (!Available) throw new InvalidOperationException("LZ4 unavailable");
            if (decode(input, output, length, expected) != expected) throw new FormatException("LZ4 output length mismatch");
        }
    }
}
