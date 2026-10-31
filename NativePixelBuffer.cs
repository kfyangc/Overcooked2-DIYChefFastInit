using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace OC2DIYChef
{
    // Short-lived import bytes, released immediately after Texture2D copies them.
    // A 22 MiB cached texture must not create a 22 MiB old-Mono GC allocation.
    internal sealed class NativePixelBuffer : IDisposable
    {
        const int ChunkSize = 65536;
        static long allocatedBytes;
        IntPtr memory;
        internal readonly int Length;
        internal static long AllocatedBytes { get { return Interlocked.Read(ref allocatedBytes); } }
        internal IntPtr Pointer
        {
            get
            {
                if (memory == IntPtr.Zero) throw new ObjectDisposedException("NativePixelBuffer");
                return memory;
            }
        }

        NativePixelBuffer(int length)
        {
            if (length < 1) throw new ArgumentOutOfRangeException("length");
            Length = length;
            memory = Marshal.AllocHGlobal(length);
            Interlocked.Add(ref allocatedBytes, length);
        }

        internal static IntPtr Offset(IntPtr pointer, int offset)
        {
            return IntPtr.Size == 4 ? new IntPtr(unchecked(pointer.ToInt32() + offset)) :
                new IntPtr(pointer.ToInt64() + offset);
        }

        internal static NativePixelBuffer Read(Stream stream, int length, Func<bool> cancelled)
        {
            var pixels = new NativePixelBuffer(length);
            try
            {
                byte[] chunk = new byte[Math.Min(length, ChunkSize)];
                int offset = 0;
                while (offset < length)
                {
                    if (cancelled != null && cancelled()) throw new OperationCanceledException();
                    int read = stream.Read(chunk, 0, Math.Min(chunk.Length, length - offset));
                    if (read == 0) throw new EndOfStreamException();
                    Marshal.Copy(chunk, 0, Offset(pixels.memory, offset), read);
                    offset += read;
                }
                return pixels;
            }
            catch { pixels.Dispose(); throw; }
        }

        internal void WriteTo(BinaryWriter writer)
        {
            byte[] chunk = new byte[Math.Min(Length, ChunkSize)];
            int offset = 0;
            while (offset < Length)
            {
                int count = Math.Min(chunk.Length, Length - offset);
                Marshal.Copy(Offset(Pointer, offset), chunk, 0, count);
                writer.Write(chunk, 0, count);
                offset += count;
            }
        }

        public void Dispose()
        {
            IntPtr pointer = Interlocked.Exchange(ref memory, IntPtr.Zero);
            if (pointer == IntPtr.Zero) return;
            Marshal.FreeHGlobal(pointer);
            Interlocked.Add(ref allocatedBytes, -Length);
        }
    }
}
