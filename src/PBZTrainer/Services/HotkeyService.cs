using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace PBZTrainer.Services
{
    /// <summary>全局快捷键：低级键盘钩子，游戏在前台时同样生效。</summary>
    public sealed class HotkeyService : IDisposable
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;

        private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint threadId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr GetModuleHandle(string name);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vk);

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        private readonly HookProc _proc;
        private IntPtr _hook = IntPtr.Zero;
        private readonly Dictionary<string, Action> _map = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>触发前置判断（例如仅当前台是游戏或本窗口时才响应）。</summary>
        public Func<string, bool> Filter { get; set; }

        public bool Suspended { get; set; }

        public HotkeyService()
        {
            _proc = Hook;
        }

        public void Start()
        {
            if (_hook != IntPtr.Zero) return;
            using var cur = Process.GetCurrentProcess();
            using var mod = cur.MainModule;
            _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(mod.ModuleName), 0);
        }

        public void Clear() => _map.Clear();

        public void Register(string combo, Action action)
        {
            if (string.IsNullOrWhiteSpace(combo) || action == null) return;
            _map[Normalize(combo)] = action;
        }

        public bool IsTaken(string combo, Action except = null)
        {
            string c = Normalize(combo);
            return _map.TryGetValue(c, out var a) && !ReferenceEquals(a, except);
        }

        private IntPtr Hook(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && !Suspended && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
            {
                var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                string combo = CurrentCombo((int)info.vkCode);
                if (combo != null && _map.TryGetValue(combo, out var act))
                {
                    try
                    {
                        if (Filter == null || Filter(combo)) act();
                    }
                    catch { }
                }
            }
            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        private static bool Down(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

        public static string CurrentCombo(int vk)
        {
            // 忽略单独的修饰键
            if (vk == 0x10 || vk == 0x11 || vk == 0x12 || vk == 0x5B || vk == 0x5C ||
                vk == 0xA0 || vk == 0xA1 || vk == 0xA2 || vk == 0xA3 || vk == 0xA4 || vk == 0xA5)
                return null;

            var parts = new List<string>();
            if (Down(0x11) || Down(0xA2) || Down(0xA3)) parts.Add("Ctrl");
            if (Down(0x12) || Down(0xA4) || Down(0xA5)) parts.Add("Alt");
            if (Down(0x10) || Down(0xA0) || Down(0xA1)) parts.Add("Shift");
            if (Down(0x5B) || Down(0x5C)) parts.Add("Win");
            parts.Add(KeyName(vk));
            return string.Join("+", parts);
        }

        public static string Normalize(string combo)
        {
            var parts = combo.Split('+', StringSplitOptions.RemoveEmptyEntries);
            var mods = new List<string>();
            string key = null;
            foreach (var raw in parts)
            {
                string p = raw.Trim();
                if (p.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || p.Equals("Control", StringComparison.OrdinalIgnoreCase)) mods.Add("Ctrl");
                else if (p.Equals("Alt", StringComparison.OrdinalIgnoreCase)) mods.Add("Alt");
                else if (p.Equals("Shift", StringComparison.OrdinalIgnoreCase)) mods.Add("Shift");
                else if (p.Equals("Win", StringComparison.OrdinalIgnoreCase)) mods.Add("Win");
                else key = p;
            }
            var ordered = new List<string>();
            if (mods.Contains("Ctrl")) ordered.Add("Ctrl");
            if (mods.Contains("Alt")) ordered.Add("Alt");
            if (mods.Contains("Shift")) ordered.Add("Shift");
            if (mods.Contains("Win")) ordered.Add("Win");
            if (key != null) ordered.Add(key);
            return string.Join("+", ordered);
        }

        private static readonly Dictionary<int, string> Names = new()
        {
            [0x08] = "Backspace", [0x09] = "Tab", [0x0D] = "Enter", [0x1B] = "Esc", [0x20] = "Space",
            [0x21] = "PageUp", [0x22] = "PageDown", [0x23] = "End", [0x24] = "Home",
            [0x25] = "Left", [0x26] = "Up", [0x27] = "Right", [0x28] = "Down",
            [0x2D] = "Insert", [0x2E] = "Delete", [0x14] = "CapsLock",
            [0x70] = "F1", [0x71] = "F2", [0x72] = "F3", [0x73] = "F4", [0x74] = "F5", [0x75] = "F6",
            [0x76] = "F7", [0x77] = "F8", [0x78] = "F9", [0x79] = "F10", [0x7A] = "F11", [0x7B] = "F12",
            [0x6A] = "Num*", [0x6B] = "Num+", [0x6D] = "Num-", [0x6E] = "Num.", [0x6F] = "Num/",
            [0xBA] = ";", [0xBB] = "=", [0xBC] = ",", [0xBD] = "-", [0xBE] = ".", [0xBF] = "/",
            [0xC0] = "`", [0xDB] = "[", [0xDC] = "\\", [0xDD] = "]", [0xDE] = "'",
        };

        public static string KeyName(int vk)
        {
            if (Names.TryGetValue(vk, out var n)) return n;
            if (vk >= 0x30 && vk <= 0x39) return ((char)vk).ToString();
            if (vk >= 0x41 && vk <= 0x5A) return ((char)vk).ToString();
            if (vk >= 0x60 && vk <= 0x69) return "Num" + (vk - 0x60);
            var key = KeyInterop.KeyFromVirtualKey(vk);
            return key == Key.None ? ("VK" + vk) : key.ToString();
        }

        public void Dispose()
        {
            if (_hook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero;
            }
        }
    }
}
