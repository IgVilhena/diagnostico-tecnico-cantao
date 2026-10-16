using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Diagnostico
{
    // Chamadas Win32 usadas para hotkeys, captura de janelas e controle de janelas externas.
    static class Native
    {
        public const int WM_HOTKEY = 0x0312;
        public const int WM_NCHITTEST = 0x0084;
        public const int WM_NCLBUTTONDOWN = 0x00A1;
        public const int HTCAPTION = 2;
        public const int HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13, HTTOPRIGHT = 14,
                         HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;

        public const int SW_SHOWNORMAL = 1, SW_SHOWMINIMIZED = 2, SW_MAXIMIZE = 3, SW_RESTORE = 9;
        public const int VK_F8 = 0x77, VK_F9 = 0x78;

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        public static readonly bool IsWindows = Environment.OSVersion.Platform == PlatformID.Win32NT;

        static class Win32
        {
            [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);
            [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
            [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
            [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
            [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr hWnd);
            [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
            [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr hWnd);
            [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
            [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
            [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
            [DllImport("user32.dll")] public static extern int GetWindowThreadProcessId(IntPtr hWnd, out int pid);
            [DllImport("kernel32.dll")] public static extern int GetCurrentThreadId();
            [DllImport("user32.dll")] public static extern bool AttachThreadInput(int idAttach, int idAttachTo, bool fAttach);
            [DllImport("user32.dll")] public static extern bool ReleaseCapture();
            [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wp, IntPtr lp);
            [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, int flags, IntPtr extra);
            [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr hWnd);
            [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder sb, int max);
            [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX status);

            [DllImport("user32.dll", CharSet = CharSet.Unicode)]
            public static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE devMode);

            [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr SetWindowsHookEx(int idHook, HookProc proc, IntPtr hMod, int threadId);
            [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr hook);
            [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
            [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string name);

            [DllImport("winmm.dll", CharSet = CharSet.Unicode)] public static extern int mciSendString(string cmd, StringBuilder ret, int retLen, IntPtr hwnd);
            [DllImport("winmm.dll", CharSet = CharSet.Unicode)] public static extern bool mciGetErrorString(int err, StringBuilder text, int len);

            [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT rect, int size);
            [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

            [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
            public static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);
        }

        public static bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk) { return IsWindows && Win32.RegisterHotKey(hWnd, id, fsModifiers, vk); }
        public static bool UnregisterHotKey(IntPtr hWnd, int id) { return IsWindows && Win32.UnregisterHotKey(hWnd, id); }
        public static IntPtr GetForegroundWindow() { return IsWindows ? Win32.GetForegroundWindow() : IntPtr.Zero; }
        public static bool SetForegroundWindow(IntPtr hWnd) { return IsWindows && Win32.SetForegroundWindow(hWnd); }
        public static bool BringWindowToTop(IntPtr hWnd) { return IsWindows && Win32.BringWindowToTop(hWnd); }
        public static bool ShowWindow(IntPtr hWnd, int nCmdShow) { return IsWindows && Win32.ShowWindow(hWnd, nCmdShow); }
        public static bool IsZoomed(IntPtr hWnd) { return IsWindows && Win32.IsZoomed(hWnd); }
        public static bool IsIconic(IntPtr hWnd) { return IsWindows && Win32.IsIconic(hWnd); }
        public static bool IsWindowVisible(IntPtr hWnd) { return IsWindows && Win32.IsWindowVisible(hWnd); }
        public static bool GetWindowRect(IntPtr hWnd, out RECT rect) { if (IsWindows) return Win32.GetWindowRect(hWnd, out rect); rect = new RECT(); return false; }
        public static int GetWindowThreadProcessId(IntPtr hWnd, out int pid) { if (IsWindows) return Win32.GetWindowThreadProcessId(hWnd, out pid); pid = 0; return 0; }
        public static int GetCurrentThreadId() { return IsWindows ? Win32.GetCurrentThreadId() : 0; }
        public static bool AttachThreadInput(int idAttach, int idAttachTo, bool fAttach) { return IsWindows && Win32.AttachThreadInput(idAttach, idAttachTo, fAttach); }
        public static bool ReleaseCapture() { return IsWindows && Win32.ReleaseCapture(); }
        public static IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wp, IntPtr lp) { return IsWindows ? Win32.SendMessage(hWnd, msg, wp, lp) : IntPtr.Zero; }
        public static void keybd_event(byte vk, byte scan, int flags, IntPtr extra) { if (IsWindows) Win32.keybd_event(vk, scan, flags, extra); }
        public static int GetWindowTextLength(IntPtr hWnd) { return IsWindows ? Win32.GetWindowTextLength(hWnd) : 0; }
        public static int GetWindowText(IntPtr hWnd, StringBuilder sb, int max) { return IsWindows ? Win32.GetWindowText(hWnd, sb, max) : 0; }
        public static bool SetProcessDPIAware() { return IsWindows && Win32.SetProcessDPIAware(); }

        [StructLayout(LayoutKind.Sequential)]
        public struct MEMORYSTATUSEX
        {
            public uint Length, MemoryLoad;
            public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
        }

        public static bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX status) { return IsWindows && Win32.GlobalMemoryStatusEx(ref status); }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
            public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
            public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
            public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
        }

        public const int ENUM_CURRENT_SETTINGS = -1;

        public static bool EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE devMode) { return IsWindows && Win32.EnumDisplaySettings(deviceName, modeNum, ref devMode); }

        public delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct KBDLLHOOKSTRUCT { public int vkCode, scanCode, flags, time; public IntPtr dwExtraInfo; }

        public static IntPtr SetWindowsHookEx(int idHook, HookProc proc, IntPtr hMod, int threadId) { return IsWindows ? Win32.SetWindowsHookEx(idHook, proc, hMod, threadId) : IntPtr.Zero; }
        public static bool UnhookWindowsHookEx(IntPtr hook) { return IsWindows && Win32.UnhookWindowsHookEx(hook); }
        public static IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam) { return IsWindows ? Win32.CallNextHookEx(hook, code, wParam, lParam) : IntPtr.Zero; }
        public static IntPtr GetModuleHandle(string name) { return IsWindows ? Win32.GetModuleHandle(name) : IntPtr.Zero; }

        public static int mciSendString(string cmd, StringBuilder ret, int retLen, IntPtr hwnd) { return IsWindows ? Win32.mciSendString(cmd, ret, retLen, hwnd) : 1; }
        public static bool mciGetErrorString(int err, StringBuilder text, int len) { return IsWindows && Win32.mciGetErrorString(err, text, len); }

        public static int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT rect, int size) { if (IsWindows) return Win32.DwmGetWindowAttribute(hwnd, attr, out rect, size); rect = new RECT(); return -1; }
        public static int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size) { return IsWindows ? Win32.DwmSetWindowAttribute(hwnd, attr, ref value, size) : -1; }

        public static int SetWindowTheme(IntPtr hwnd, string app, string idList) { return IsWindows ? Win32.SetWindowTheme(hwnd, app, idList) : -1; }

        public static string WindowTitle(IntPtr h)
        {
            int len = GetWindowTextLength(h);
            if (len <= 0) return "";
            var sb = new StringBuilder(len + 1);
            GetWindowText(h, sb, sb.Capacity);
            return sb.ToString();
        }

        // Retângulo visível da janela (sem a sombra do Windows 10/11).
        public static System.Drawing.Rectangle WindowBounds(IntPtr h)
        {
            RECT r;
            bool ok = false;
            try { ok = DwmGetWindowAttribute(h, 9 /* DWMWA_EXTENDED_FRAME_BOUNDS */, out r, Marshal.SizeOf(typeof(RECT))) == 0; }
            catch { r = new RECT(); }
            if (!ok) GetWindowRect(h, out r);
            return System.Drawing.Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
        }

        // SetForegroundWindow costuma ser bloqueado pelo Windows; estes truques contornam isso.
        public static void ForceForeground(IntPtr h)
        {
            if (h == IntPtr.Zero) return;
            if (IsIconic(h)) ShowWindow(h, SW_RESTORE);
            IntPtr fg = GetForegroundWindow();
            int dummy;
            int fgThread = GetWindowThreadProcessId(fg, out dummy);
            int myThread = GetCurrentThreadId();
            keybd_event(0x12, 0, 0, IntPtr.Zero);       // ALT down
            keybd_event(0x12, 0, 2, IntPtr.Zero);       // ALT up
            if (fgThread != myThread) AttachThreadInput(myThread, fgThread, true);
            BringWindowToTop(h);
            SetForegroundWindow(h);
            if (fgThread != myThread) AttachThreadInput(myThread, fgThread, false);
        }
    }
}
