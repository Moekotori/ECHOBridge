using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Echo.Core.Media;

/// <summary>Only the scanner joins this unnamed job. Core owns its non-inheritable handle.</summary>
internal sealed class WindowsScannerProcessOwnership : IDisposable
{
    private readonly JobHandle _job;
    private WindowsScannerProcessOwnership(JobHandle job) => _job = job;

    public static WindowsScannerProcessOwnership? TryAttach(Process scanner, out int error)
    {
        error = 0;
        if (!OperatingSystem.IsWindows()) return null;
        var job = CreateJobObjectW(0, 0);
        if (job.IsInvalid) { error = Marshal.GetLastWin32Error(); job.Dispose(); return null; }
        var limits = new ExtendedLimits { Basic = new BasicLimits { Flags = 0x2000 } }; // KILL_ON_JOB_CLOSE
        if (!SetInformationJobObject(job, 9, ref limits, (uint)Marshal.SizeOf<ExtendedLimits>())
            || !AssignProcessToJobObject(job, scanner.SafeHandle))
        {
            error = Marshal.GetLastWin32Error();
            job.Dispose();
            return null;
        }
        return new WindowsScannerProcessOwnership(job);
    }

    public void Dispose() => _job.Dispose();

    // https://learn.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-jobobject_extended_limit_information
    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimits
    {
        public long ProcessTime, JobTime;
        public uint Flags;
        public UIntPtr MinimumWorkingSet, MaximumWorkingSet;
        public uint ActiveProcesses;
        public UIntPtr Affinity;
        public uint Priority, Scheduling;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimits
    {
        public BasicLimits Basic;
        public IoCounters Io;
        public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
    }
    private sealed class JobHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public JobHandle() : base(true) { }
        protected override bool ReleaseHandle() => CloseHandle(handle);
    }
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern JobHandle CreateJobObjectW(nint attributes, nint name);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(JobHandle job, int informationClass, ref ExtendedLimits limits, uint length);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(JobHandle job, SafeProcessHandle process);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);
}
