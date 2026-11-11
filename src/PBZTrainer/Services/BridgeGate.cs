using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PBZTrainer.Services
{
    /// <summary>
    /// 解除 PBZBridge.dll 的「高级功能」门禁。
    /// 原版桥接在 grant_items / spawn / completion.* / catalog(collections) 等指令前都会调用
    /// 同一个授权判定（导出跳转表中的一项），未授权即返回「请先解锁高级功能」。
    /// 我们在游戏进程里把该跳转改成 `mov al,1; ret`，判定恒为真。
    /// 只改内存，不动磁盘文件；游戏重启后重新打一次即可。
    /// </summary>
    public static class BridgeGate
    {
        /// <summary>授权判定在 PBZBridge.dll 中的 RVA（1.0 构建：跳转表项，形如 E9 rel32）。</summary>
        public const int AuthThunkRva = 0x5D62;

        private static readonly byte[] AuthorizedStub = { 0xB0, 0x01, 0xC3, 0x90, 0x90 }; // mov al,1 ; ret ; nop nop

        [DllImport("kernel32", SetLastError = true)]
        private static extern IntPtr CreateToolhelp32Snapshot(uint flags, int pid);

        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool Module32FirstW(IntPtr snap, ref MODULEENTRY32W entry);

        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool Module32NextW(IntPtr snap, ref MODULEENTRY32W entry);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct MODULEENTRY32W
        {
            public uint dwSize;
            public uint th32ModuleID;
            public uint th32ProcessID;
            public uint GlblcntUsage;
            public uint ProccntUsage;
            public IntPtr modBaseAddr;
            public uint modBaseSize;
            public IntPtr hModule;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szModule;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExePath;
        }

        public static IntPtr FindModuleBase(int pid, string moduleName)
        {
            IntPtr snap = CreateToolhelp32Snapshot(0x8 | 0x2, pid);
            if (snap == IntPtr.Zero || snap == new IntPtr(-1)) return IntPtr.Zero;
            try
            {
                var me = new MODULEENTRY32W { dwSize = (uint)Marshal.SizeOf(typeof(MODULEENTRY32W)) };
                if (!Module32FirstW(snap, ref me)) return IntPtr.Zero;
                do
                {
                    if (string.Equals(me.szModule, moduleName, StringComparison.OrdinalIgnoreCase))
                        return me.modBaseAddr;
                } while (Module32NextW(snap, ref me));
                return IntPtr.Zero;
            }
            finally
            {
                Native.CloseHandle(snap);
            }
        }

        /// <summary>
        /// 返回 true 表示门禁已解除（本次刚打或之前已打）。
        /// 组件是 VMProtect 保护的，刚注入时判定入口可能还没解密完成（读出来不是 E9），
        /// 所以这里会轮询重试；写入前先改页保护，避免只读页写不进去。
        /// </summary>
        public static bool Unlock(int pid, out string note)
        {
            note = "";
            IntPtr baseAddr = FindModuleBase(pid, Injector.BridgeFileName);
            if (baseAddr == IntPtr.Zero)
            {
                note = "游戏内未找到组件模块";
                return false;
            }

            IntPtr proc = Native.OpenProcess(0x1F0FFF, false, pid);
            if (!Native.IsValidHandle(proc))
            {
                note = "无法打开游戏进程（权限不足）";
                return false;
            }

            try
            {
                IntPtr addr = IntPtr.Add(baseAddr, AuthThunkRva);
                string last = "";

                for (int attempt = 0; attempt < 10; attempt++)
                {
                    byte[] cur = new byte[5];
                    if (!Native.ReadProcessMemory(proc, addr, cur, 5, out _))
                    {
                        last = "读取判定入口失败";
                        System.Threading.Thread.Sleep(300);
                        continue;
                    }

                    if (cur[0] == AuthorizedStub[0] && cur[1] == AuthorizedStub[1] && cur[2] == AuthorizedStub[2])
                    {
                        note = "门禁已处于解锁状态";
                        return true;
                    }

                    // 还没解密完成 / 不是跳转：等一下再读，别乱改
                    if (cur[0] != 0xE9)
                    {
                        last = $"判定入口不是预期跳转（{cur[0]:X2}），等待组件就绪";
                        System.Threading.Thread.Sleep(300);
                        continue;
                    }

                    uint oldProtect;
                    bool changed = Native.VirtualProtectEx(proc, addr, (UIntPtr)5, 0x40 /* PAGE_EXECUTE_READWRITE */, out oldProtect);
                    bool wrote = Native.WriteProcessMemory(proc, addr, AuthorizedStub, 5, out _);
                    if (changed) Native.VirtualProtectEx(proc, addr, (UIntPtr)5, oldProtect, out _);

                    if (!wrote)
                    {
                        last = "写入判定入口失败";
                        System.Threading.Thread.Sleep(300);
                        continue;
                    }

                    byte[] check = new byte[3];
                    Native.ReadProcessMemory(proc, addr, check, 3, out _);
                    if (check[0] == AuthorizedStub[0] && check[1] == AuthorizedStub[1] && check[2] == AuthorizedStub[2])
                    {
                        note = attempt == 0 ? "已解除高级功能门禁" : $"已解除高级功能门禁（重试 {attempt} 次）";
                        return true;
                    }

                    last = "写入后校验不一致";
                    System.Threading.Thread.Sleep(300);
                }

                note = last + "；组件版本可能不同";
                return false;
            }
            finally
            {
                Native.CloseHandle(proc);
            }
        }
    }
}
