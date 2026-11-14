using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Diagnostics;
using System.Runtime.InteropServices;
using UnityEngine;

namespace OC2DIYChef
{
    internal sealed class CachedTexture : IDisposable
    {
        internal int StorageSchema;
        internal bool Compressed;
        internal int Width, Height, Format, MipCount;
        internal byte[] Pixels;
        internal NativePixelBuffer NativePixels;
        internal int PixelLength { get { return Pixels != null ? Pixels.Length : NativePixels != null ? NativePixels.Length : 0; } }
        public void Dispose() { if (NativePixels != null) NativePixels.Dispose(); }
    }

}
