using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace PBZTrainer.Services
{
    /// <summary>
    /// 定位 PBZ 游戏进程并把 PBZBridge.dll 注入进去。
    /// 逻辑对齐原版 BridgeClient.IsGameProcess / Inject / NativeComponentLoader。
    /// </summary>
    public sealed class GameProcess
    {
        public int Pid;
        public string ExePath;
        public long WorkingSetBytes;

        public override string ToString() => $"PBZ.exe ({Pid})";
    }

    public static class Injector
    {
        public const string GameExe = "PBZ";
        public const string BridgeFileName = "PBZBridge.dll";

        private static Process[] Candidates()
        {
            try
            {
                return Process.GetProcessesByName(GameExe)
                    .Where(p => { try { return string.Equals(Path.GetFileName(p.MainModule.FileName), "PBZ.exe", StringComparison.OrdinalIgnoreCase); } catch { return false; } })
                    .ToArray();
            }
            catch
            {
                return Array.Empty<Process>();
            }
        }

        /// <summary>按内存占用挑选真正的游戏进程（原版要求模块内存 &gt; 100MB）。</summary>
        public static GameProcess FindGame()
        {
            GameProcess best = null;
            foreach (var p in Candidates())
            {
                try
                {
                    int moduleSize = p.MainModule.ModuleMemorySize;
                    if (moduleSize <= 100 * 1024 * 1024) continue;
                    var g = new GameProcess
                    {
                        Pid = p.Id,
                        ExePath = p.MainModule.FileName,
                        WorkingSetBytes = p.WorkingSet64
                    };
                    if (best == null || g.WorkingSetBytes > best.WorkingSetBytes) best = g;
                }
                catch
                {
                    // 权限不足时跳过
                }
            }
            return best;
        }

        public static string PipeName(int pid) => $@"\\.\pipe\ChuwuPBZ_v1_{pid}";

        /// <summary>用模块枚举判断组件是否已在游戏中（不触碰管道，避免打断服务端会话）。</summary>
        public static bool BridgeIsLoaded(int pid)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                foreach (ProcessModule m in p.Modules)
                {
                    if (string.Equals(m.ModuleName, BridgeFileName, StringComparison.OrdinalIgnoreCase)) return true;
                }
                return false;
            }
            catch
            {
                return Native.PipeExists(PipeName(pid));
            }
        }

        /// <summary>
        /// 找到 PBZBridge.dll。查找顺序：
        /// 1) 环境变量 PBZ_BRIDGE 指定的完整路径
        /// 2) 程序所在目录
        /// 3) 游戏安装目录（PBZ.exe 旁边）
        /// 4) 常见的小伍盒子模块目录（data/modules/**）
        /// 组件属于第三方文件，仓库里不带，需要自备。
        /// </summary>
        public static string ResolveBridgePath()
        {
            string fromEnv = Environment.GetEnvironmentVariable("PBZ_BRIDGE");
            if (!string.IsNullOrWhiteSpace(fromEnv) && File.Exists(fromEnv)) return fromEnv;

            string local = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, BridgeFileName);
            if (File.Exists(local)) return local;

            var game = FindGame();
            if (game?.ExePath != null)
            {
                string beside = Path.Combine(Path.GetDirectoryName(game.ExePath) ?? "", BridgeFileName);
                if (File.Exists(beside)) return beside;
            }

            foreach (var root in new[] { @"D:\XiaowuTrainer\data\modules", @"C:\XiaowuTrainer\data\modules" })
            {
                if (!Directory.Exists(root)) continue;
                try
                {
                    foreach (var f in Directory.EnumerateFiles(root, BridgeFileName, SearchOption.AllDirectories))
                        return f;
                }
                catch { }
            }
            return null;
        }

        /// <summary>把 DLL 注入目标进程，返回模块基址；失败抛异常并带中文原因。</summary>
        public static long Inject(int pid, string dllPath)
        {
            if (!File.Exists(dllPath)) throw new FileNotFoundException("组件文件缺失，请重新下载完整程序", dllPath);

            IntPtr proc = Native.OpenProcess(Native.PROCESS_ALL_ACCESS, false, pid);
            if (!Native.IsValidHandle(proc))
            {
                int err = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                throw new InvalidOperationException($"访问游戏进程失败（错误 {err}），请保持相同运行权限");
            }

            try
            {
                if (Native.IsWow64Process(proc, out bool wow64) && wow64)
                    throw new InvalidOperationException("组件加载失败：需要 Windows x64 游戏进程");

                byte[] pathBytes = System.Text.Encoding.Unicode.GetBytes(dllPath + "\0");
                IntPtr remote = Native.VirtualAllocEx(proc, IntPtr.Zero, (UIntPtr)pathBytes.Length,
                    Native.MEM_COMMIT | Native.MEM_RESERVE, Native.PAGE_READWRITE);
                if (remote == IntPtr.Zero) throw new InvalidOperationException("分配加载参数失败");

                try
                {
                    if (!Native.WriteProcessMemory(proc, remote, pathBytes, (UIntPtr)pathBytes.Length, out _))
                        throw new InvalidOperationException("写入加载参数失败");

                    IntPtr k32 = Native.GetModuleHandleW("kernel32.dll");
                    if (k32 == IntPtr.Zero) k32 = Native.GetModuleHandleW("kernel32");
                    IntPtr loadLibrary = k32 == IntPtr.Zero ? IntPtr.Zero : Native.GetProcAddress(k32, "LoadLibraryW");
                    if (loadLibrary == IntPtr.Zero) throw new InvalidOperationException("系统加载接口不匹配");

                    IntPtr thread = Native.CreateRemoteThread(proc, IntPtr.Zero, UIntPtr.Zero, loadLibrary, remote, 0, out _);
                    if (thread == IntPtr.Zero) throw new InvalidOperationException("系统拒绝加载请求");

                    try
                    {
                        uint wait = Native.WaitForSingleObject(thread, 15000);
                        if (wait != 0) throw new InvalidOperationException("组件加载尚未完成，请稍候重连；不会重复提交加载请求");
                        Native.GetExitCodeThread(thread, out uint code);
                        long handle = code;
                        if (handle == 0) throw new InvalidOperationException("组件初始化未通过");
                        return handle;
                    }
                    finally
                    {
                        Native.CloseHandle(thread);
                    }
                }
                finally
                {
                    Native.VirtualFreeEx(proc, remote, UIntPtr.Zero, Native.MEM_RELEASE);
                }
            }
            finally
            {
                Native.CloseHandle(proc);
            }
        }

        public static string Sha256(string path)
        {
            using var sha = SHA256.Create();
            using var fs = File.OpenRead(path);
            return Convert.ToHexString(sha.ComputeHash(fs));
        }
    }
}
