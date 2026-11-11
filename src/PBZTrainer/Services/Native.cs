using System;
using System.Runtime.InteropServices;

namespace PBZTrainer.Services
{
    internal static class Native
    {
        public const int PROCESS_ALL_ACCESS = 0x1F0FFF;
        public const uint MEM_COMMIT = 0x1000;
        public const uint MEM_RESERVE = 0x2000;
        public const uint MEM_RELEASE = 0x8000;
        public const uint PAGE_READWRITE = 0x04;
        public const uint GENERIC_READ = 0x80000000;
        public const uint GENERIC_WRITE = 0x40000000;
        public const uint OPEN_EXISTING = 3;
        public const int ERROR_MORE_DATA = 234;

        [DllImport("kernel32", SetLastError = true)]
        public static extern IntPtr OpenProcess(int access, bool inherit, int pid);

        [DllImport("kernel32", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr h);

        [DllImport("kernel32", SetLastError = true)]
        public static extern IntPtr VirtualAllocEx(IntPtr proc, IntPtr addr, UIntPtr size, uint type, uint protect);

        [DllImport("kernel32", SetLastError = true)]
        public static extern bool VirtualFreeEx(IntPtr proc, IntPtr addr, UIntPtr size, uint type);

        [DllImport("kernel32", SetLastError = true)]
        public static extern bool VirtualProtectEx(IntPtr proc, IntPtr addr, UIntPtr size, uint newProtect, out uint oldProtect);

        [DllImport("kernel32", SetLastError = true)]
        public static extern bool WriteProcessMemory(IntPtr proc, IntPtr addr, byte[] buffer, UIntPtr size, out UIntPtr written);

        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr GetModuleHandleW(string name);

        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr CreateFileW(string name, uint access, uint share, IntPtr sa,
                                                uint disposition, uint flags, IntPtr template);

        /// <summary>管道是否存在但被占用（ERROR_PIPE_BUSY = 231）。</summary>
        public static bool PipeBusy(string pipeName)
        {
            IntPtr h = CreateFileW(pipeName, 0x80000000u | 0x40000000u, 0, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (h != new IntPtr(-1))
            {
                CloseHandle(h);
                return false;
            }
            return Marshal.GetLastWin32Error() == 231;
        }

        [DllImport("kernel32", SetLastError = true)]
        public static extern bool ReadProcessMemory(IntPtr proc, IntPtr addr, byte[] buffer, UIntPtr size, out UIntPtr read);

        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true)]
        public static extern IntPtr GetProcAddress(IntPtr module, string name);

        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr CreateRemoteThread(IntPtr proc, IntPtr attrs, UIntPtr stack, IntPtr start, IntPtr param, uint flags, out uint tid);

        [DllImport("kernel32", SetLastError = true)]
        public static extern uint WaitForSingleObject(IntPtr handle, uint ms);

        [DllImport("kernel32", SetLastError = true)]
        public static extern bool GetExitCodeThread(IntPtr thread, out uint code);


        [DllImport("kernel32", SetLastError = true)]
        public static extern bool IsWow64Process(IntPtr proc, out bool wow64);

        [DllImport("kernel32")]
        public static extern void GetNativeSystemInfo(ref SYSTEM_INFO info);

        [StructLayout(LayoutKind.Sequential)]
        public struct SYSTEM_INFO
        {
            public ushort wProcessorArchitecture;
            public ushort wReserved;
            public uint dwPageSize;
            public IntPtr lpMinimumApplicationAddress;
            public IntPtr lpMaximumApplicationAddress;
            public IntPtr dwActiveProcessorMask;
            public uint dwNumberOfProcessors;
            public uint dwProcessorType;
            public uint dwAllocationGranularity;
            public ushort wProcessorLevel;
            public ushort wProcessorRevision;
        }

        public static bool IsValidHandle(IntPtr h) => h != IntPtr.Zero && h != new IntPtr(-1);

        public static bool PipeExists(string name)
        {
            IntPtr h = CreateFileW(name, GENERIC_READ | GENERIC_WRITE, 0, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (IsValidHandle(h))
            {
                CloseHandle(h);
                return true;
            }
            return false;
        }
    }
}
