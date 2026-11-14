using System;
using System.Runtime.InteropServices;

namespace OC2DIYChef
{
    internal static class ProcessMemory
    {
        [StructLayout(LayoutKind.Sequential)]
        struct Counters
        {
            internal uint Size, PageFaults;
            internal UIntPtr PeakWorkingSet, WorkingSet, PeakPagedPool, PagedPool,
                PeakNonPagedPool, NonPagedPool, PagefileUsage, PeakPagefileUsage, PrivateUsage;
        }
        [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
        [DllImport("psapi.dll")] static extern bool GetProcessMemoryInfo(IntPtr process, ref Counters counters, uint size);
        internal static string Read()
        {
            var counters = new Counters();
            counters.Size = (uint)Marshal.SizeOf(typeof(Counters));
            return GetProcessMemoryInfo(GetCurrentProcess(), ref counters, counters.Size) ?
                " privateBytes=" + counters.PrivateUsage.ToUInt64() + " workingSet=" + counters.WorkingSet.ToUInt64() :
                " processMemory=unavailable";
        }
    }
}
