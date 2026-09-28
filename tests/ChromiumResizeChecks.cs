using System.Runtime.InteropServices;
using TaskbarProximityOpacity;

internal static class ChromiumResizeChecks
{
    public static void Run(Action<bool, string> check)
    {
        using var window = new ConstrainingWindow
        {
            FormBorderStyle = FormBorderStyle.None,
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual,
            Bounds = new Rectangle(100, 100, 500, 300)
        };
        window.Show();
        var hwnd = window.Handle;
        uint flags = Native.SWP_NOZORDER | Native.SWP_NOACTIVATE;
        Native.SetWindowPos(hwnd, 0, 100, 100, 500, 200, flags);
        Native.GetWindowRect(hwnd, out var blocked);
        check(blocked.Bottom - blocked.Top == 300,
            "A Chromium-like window can reject ordinary shrink requests");

        Native.SetWindowPos(hwnd, 0, 100, 100, 500, 200, flags | Native.SWP_NOSENDCHANGING);
        Native.GetWindowRect(hwnd, out var resized);
        check(resized.Bottom - resized.Top == 200,
            "Direct positioning shrinks a Chromium-like window without focus");
    }

    private sealed class ConstrainingWindow : Form
    {
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x0046) // WM_WINDOWPOSCHANGING
            {
                var position = Marshal.PtrToStructure<WindowPos>(message.LParam);
                position.Height = 300;
                Marshal.StructureToPtr(position, message.LParam, false);
            }
            base.WndProc(ref message);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPos
    {
        public nint Window;
        public nint After;
        public int X;
        public int Y;
        public int Width;
        public int Height;
        public uint Flags;
    }
}
