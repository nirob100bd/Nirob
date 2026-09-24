using System.Runtime.InteropServices;

namespace Swifter.Core.Engine;

public static class NativeMemoryBridge
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessWorkingSetSize(IntPtr hProcess, IntPtr minSize, IntPtr maxSize);

    [DllImport("psapi.dll", SetLastError = true)]
    private static extern int EmptyWorkingSet(IntPtr hProc);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessWorkingSetSizeEx(IntPtr hProcess, IntPtr minSize, IntPtr maxSize, uint flags);

    [DllImport("kernel32.dll")]
    private static extern uint GetProcessAffinityMask(IntPtr hProcess, out IntPtr processMask, out IntPtr systemMask);

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemInfo(out SystemInfo info);

    [DllImport("kernel32.dll")]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualAlloc(IntPtr lpAddress, UIntPtr dwSize, uint flAllocationType, uint flProtect);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualFree(IntPtr lpAddress, UIntPtr dwSize, uint dwFreeType);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalAlloc(uint uFlags, UIntPtr uBytes);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);

    [StructLayout(LayoutKind.Sequential)]
    public struct SystemInfo
    {
        public ushort ProcessorArchitecture;
        public ushort Reserved;
        public uint PageSize;
        public IntPtr MinimumApplicationAddress;
        public IntPtr MaximumApplicationAddress;
        public IntPtr ActiveProcessorMask;
        public uint NumberOfProcessors;
        public uint ProcessorType;
        public uint AllocationGranularity;
        public ushort ProcessorLevel;
        public ushort ProcessorRevision;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    public const uint QUOTA_LIMITS_HARDWS_MIN_DISABLE = 0x00000002;
    public const uint MEM_COMMIT = 0x1000;
    public const uint MEM_RESERVE = 0x2000;
    public const uint MEM_RELEASE = 0x8000;
    public const uint PAGE_READWRITE = 0x04;
    public const uint LMEM_FIXED = 0x0000;

    public static bool TrimWorkingSet()
    {
        var handle = GetCurrentProcess();
        var ok = SetProcessWorkingSetSize(handle, (IntPtr)(-1), (IntPtr)(-1));
        try
        {
            EmptyWorkingSet(handle);
        }
        catch
        {
        }
        return ok;
    }

    public static bool TrimProcess(IntPtr processHandle)
    {
        if (processHandle == IntPtr.Zero) return false;
        var ok = SetProcessWorkingSetSize(processHandle, (IntPtr)(-1), (IntPtr)(-1));
        try
        {
            EmptyWorkingSet(processHandle);
        }
        catch
        {
        }
        return ok;
    }

    public static ulong GetTotalPhysicalBytes()
    {
        var status = new MemoryStatusEx { dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (GlobalMemoryStatusEx(ref status)) return status.ullTotalPhys;
        return 0;
    }

    public static ulong GetAvailablePhysicalBytes()
    {
        var status = new MemoryStatusEx { dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (GlobalMemoryStatusEx(ref status)) return status.ullAvailPhys;
        return 0;
    }

    public static int GetLogicalCoreCount()
    {
        var info = new SystemInfo();
        if (GetSystemInfo(ref info) && info.NumberOfProcessors > 0) return (int)info.NumberOfProcessors;
        return Environment.ProcessorCount;
    }

    public static bool IsLowEndCpu()
    {
        return GetLogicalCoreCount() <= 2;
    }

    public static IntPtr AllocLocal(IntPtr size)
    {
        return LocalAlloc(LMEM_FIXED, (UIntPtr)size.ToInt64());
    }

    public static void FreeLocal(IntPtr ptr)
    {
        if (ptr != IntPtr.Zero) LocalFree(ptr);
    }

    public static IntPtr VirtualAllocReadWrite(UIntPtr size)
    {
        return VirtualAlloc(IntPtr.Zero, size, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
    }

    public static bool VirtualRelease(IntPtr ptr)
    {
        return ptr != IntPtr.Zero && VirtualFree(ptr, UIntPtr.Zero, MEM_RELEASE);
    }

    public static byte[] ReadBytes(IntPtr source, int length)
    {
        var buffer = new byte[length];
        Marshal.Copy(source, buffer, 0, length);
        return buffer;
    }

    public static void WriteBytes(IntPtr destination, byte[] data)
    {
        Marshal.Copy(data, 0, destination, data.Length);
    }
}
