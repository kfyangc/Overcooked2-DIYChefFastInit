using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace OC2DIYChef
{
    // CryptoAPI uses the same SHA-256 bytes as SHA256Managed, without the old Mono hot loop.
    internal sealed class AssetHash : IDisposable
    {
        static volatile bool nativeUnavailable;
        internal static Action<string> Warning;
        IntPtr provider, hash;
        HashAlgorithm managed;
        bool finished;
        internal bool IsNative { get { return managed == null; } }

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
                    if (!CryptAcquireContext(out instance.provider, null, null, 24, 0xf0000000) ||
                        !CryptCreateHash(instance.provider, 0x800c, IntPtr.Zero, 0, out instance.hash))
                        throw NativeError();
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
            if (finished) throw new InvalidOperationException("Hash already completed");
            if (buffer == null || offset < 0 || count < 0 || offset > buffer.Length - count)
                throw new ArgumentOutOfRangeException();
            if (count == 0) return;
            if (managed != null) { managed.TransformBlock(buffer, offset, count, buffer, offset); return; }
            var pin = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                var start = new IntPtr(pin.AddrOfPinnedObject().ToInt64() + offset);
                if (!CryptHashData(hash, start, (uint)count, 0)) throw NativeError();
            }
            finally { pin.Free(); }
        }

        internal byte[] Finish()
        {
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
                if (!CryptGetHashParam(hash, 2, result, ref length, 0) || length != 32) throw NativeError();
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
            if (hash != IntPtr.Zero) { CryptDestroyHash(hash); hash = IntPtr.Zero; }
            if (provider != IntPtr.Zero) { CryptReleaseContext(provider, 0); provider = IntPtr.Zero; }
            if (managed != null) { ((IDisposable)managed).Dispose(); managed = null; }
        }
    }
}
