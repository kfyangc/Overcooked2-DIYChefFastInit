using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Diagnostics;
using System.Threading;

namespace OC2DIYChef
{
    // CryptoAPI uses the same SHA-256 bytes as SHA256Managed, without the old Mono hot loop.
    internal sealed class AssetHash : IDisposable
    {
        static volatile bool nativeUnavailable;
        internal static Action<string> Warning;
        NativeProvider provider;
        IntPtr hash;
        HashAlgorithm managed;
        bool finished;
        bool disposed;
        internal bool IsNative { get { return managed == null; } }
        [ThreadStatic] internal static NativeTimings Timings;
        [ThreadStatic] static Batch batch;

        // Scope a provider to one import, not one hash. Each digest still has its
        // own hash handle; threads and overlapping digests never share hash state.
        internal sealed class Batch : IDisposable
        {
            readonly Batch parent;
            NativeProvider provider;
            bool disposed;
            internal Batch() { parent = batch; batch = this; }
            internal NativeProvider Provider()
            {
                if (provider == null) provider = parent == null ? NativeProvider.Open() : parent.Provider().Retain();
                return provider;
            }
            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                batch = parent;
                if (provider != null) { provider.Release(); provider = null; }
            }
        }

        internal static Batch BeginBatch() { return new Batch(); }

        internal sealed class NativeProvider
        {
            internal IntPtr Handle;
            int references = 1;
            internal static NativeProvider Open()
            {
                var provider = new NativeProvider();
                long start = Stopwatch.GetTimestamp();
                bool success = CryptAcquireContext(out provider.Handle, null, null, 24, 0xf0000000);
                if (Timings != null) { Timings.Contexts++; Timings.AcquireTicks += Stopwatch.GetTimestamp() - start; }
                if (!success)
                {
                    var error = NativeError();
                    if (provider.Handle != IntPtr.Zero) provider.Release();
                    throw error;
                }
                return provider;
            }
            internal NativeProvider Retain() { Interlocked.Increment(ref references); return this; }
            internal void Release()
            {
                if (Interlocked.Decrement(ref references) != 0) return;
                long start = Stopwatch.GetTimestamp(); CryptReleaseContext(Handle, 0); Handle = IntPtr.Zero;
                if (Timings != null) { Timings.Releases++; Timings.ReleaseTicks += Stopwatch.GetTimestamp() - start; }
            }
        }

        internal sealed class NativeTimings
        {
            internal long AcquireTicks, CreateTicks, FinishTicks, DestroyTicks, ReleaseTicks;
            internal int Contexts, Hashes, Releases;
            static double Ms(long ticks) { return ticks * 1000.0 / Stopwatch.Frequency; }
            internal double AcquireMs { get { return Ms(AcquireTicks); } }
            internal double CreateMs { get { return Ms(CreateTicks); } }
            internal double FinishMs { get { return Ms(FinishTicks); } }
            internal double DestroyMs { get { return Ms(DestroyTicks); } }
            internal double ReleaseMs { get { return Ms(ReleaseTicks); } }
        }

        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool CryptAcquireContext(out IntPtr provider, string container, string name, uint type, uint flags);
        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool CryptCreateHash(IntPtr provider, uint algorithm, IntPtr key, uint flags, out IntPtr hash);
        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool CryptHashData(IntPtr hash, IntPtr data, uint length, uint flags);
        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool CryptGetHashParam(IntPtr hash, uint parameter, byte[] value, ref uint length, uint flags);
        [DllImport("advapi32.dll")] static extern bool CryptDestroyHash(IntPtr hash);
        [DllImport("advapi32.dll")] static extern bool CryptReleaseContext(IntPtr provider, uint flags);

        static void DisableNative(Exception error)
        {
            if (!nativeUnavailable && Warning != null)
                Warning("Native SHA-256 unavailable; using managed fallback: " + error.Message);
            nativeUnavailable = true;
        }

        static CryptographicException NativeError()
        {
            var error = new CryptographicException("Windows SHA-256 error " + Marshal.GetLastWin32Error());
            DisableNative(error);
            return error;
        }

        internal static AssetHash Create(bool preferNative = true)
        {
            if (preferNative && !nativeUnavailable && Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                var instance = new AssetHash();
                try
                {
                    instance.provider = batch == null ? NativeProvider.Open() : batch.Provider().Retain();
                    long start = Stopwatch.GetTimestamp();
                    bool created = CryptCreateHash(instance.provider.Handle, 0x800c, IntPtr.Zero, 0, out instance.hash);
                    if (Timings != null) { Timings.Hashes++; Timings.CreateTicks += Stopwatch.GetTimestamp() - start; }
                    if (!created) throw NativeError();
                    return instance;
                }
                catch (DllNotFoundException ex) { DisableNative(ex); }
                catch (EntryPointNotFoundException ex) { DisableNative(ex); }
                catch (BadImageFormatException ex) { DisableNative(ex); }
                catch (CryptographicException ex) { DisableNative(ex); }
                instance.Dispose();
            }
            return new AssetHash { managed = new SHA256Managed() };
        }

        internal void Append(byte[] buffer, int offset, int count)
        {
            if (disposed) throw new ObjectDisposedException("AssetHash");
            if (finished) throw new InvalidOperationException("Hash already completed");
            if (buffer == null || offset < 0 || count < 0 || offset > buffer.Length - count)
                throw new ArgumentOutOfRangeException();
            if (count == 0) return;
            if (managed != null) { managed.TransformBlock(buffer, offset, count, buffer, offset); return; }
            var pin = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                var start = NativePixelBuffer.Offset(pin.AddrOfPinnedObject(), offset);
                if (!CryptHashData(hash, start, (uint)count, 0)) throw NativeError();
            }
            finally { pin.Free(); }
        }

        internal void Append(IntPtr pointer, int count)
        {
            if (disposed) throw new ObjectDisposedException("AssetHash");
            if (finished) throw new InvalidOperationException("Hash already completed");
            if (count < 0 || pointer == IntPtr.Zero && count != 0) throw new ArgumentOutOfRangeException();
            if (count == 0) return;
            if (managed == null)
            {
                if (!CryptHashData(hash, pointer, (uint)count, 0)) throw NativeError();
                return;
            }
            byte[] chunk = new byte[Math.Min(count, 65536)];
            int offset = 0;
            while (offset < count)
            {
                int length = Math.Min(chunk.Length, count - offset);
                Marshal.Copy(NativePixelBuffer.Offset(pointer, offset), chunk, 0, length);
                managed.TransformBlock(chunk, 0, length, chunk, 0);
                offset += length;
            }
        }

        internal byte[] Finish()
        {
            if (disposed) throw new ObjectDisposedException("AssetHash");
            if (finished) throw new InvalidOperationException("Hash already completed");
            byte[] result;
            if (managed != null)
            {
                managed.TransformFinalBlock(new byte[0], 0, 0);
                result = managed.Hash;
            }
            else
            {
                result = new byte[32];
                uint length = 32;
                long start = Stopwatch.GetTimestamp();
                bool success = CryptGetHashParam(hash, 2, result, ref length, 0);
                if (Timings != null) Timings.FinishTicks += Stopwatch.GetTimestamp() - start;
                if (!success || length != 32) throw NativeError();
            }
            finished = true;
            return result;
        }

        internal static byte[] Compute(byte[] bytes)
        {
            try
            {
                using (var digest = Create()) { digest.Append(bytes, 0, bytes.Length); return digest.Finish(); }
            }
            catch (CryptographicException)
            {
                using (var digest = Create(false)) { digest.Append(bytes, 0, bytes.Length); return digest.Finish(); }
            }
        }

        static byte[] Compute(Stream source, bool native)
        {
            using (var digest = Create(native))
            {
                byte[] buffer = new byte[65536];
                int count;
                while ((count = source.Read(buffer, 0, buffer.Length)) != 0) digest.Append(buffer, 0, count);
                return digest.Finish();
            }
        }

        internal static byte[] Compute(Stream source)
        {
            long start = source.CanSeek ? source.Position : -1;
            try { return Compute(source, true); }
            catch (CryptographicException)
            {
                if (start < 0) throw;
                source.Position = start;
                return Compute(source, false);
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (hash != IntPtr.Zero)
            {
                long start = Stopwatch.GetTimestamp(); CryptDestroyHash(hash); hash = IntPtr.Zero;
                if (Timings != null) Timings.DestroyTicks += Stopwatch.GetTimestamp() - start;
            }
            if (provider != null) { provider.Release(); provider = null; }
            if (managed != null) { ((IDisposable)managed).Dispose(); managed = null; }
        }
    }
}
