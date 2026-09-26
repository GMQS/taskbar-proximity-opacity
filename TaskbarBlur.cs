using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace TaskbarProximityOpacity;

internal static class TransparencyPreference
{
    public static bool Read(bool fallback = true)
    {
        try
        {
            if (SystemInformation.HighContrast) return false;
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("EnableTransparency") is int value ? value != 0 : true;
        }
        catch (System.Security.SecurityException) { return fallback; }
        catch (UnauthorizedAccessException) { return fallback; }
        catch (IOException) { return fallback; }
    }
}

// The Windows 11 XAML taskbar ignores legacy accent policies on its own HWND.
// An independent acrylic surface immediately behind it blurs only the background.
// Its lifetime belongs to this process, so even forced exit removes the effect.
internal sealed class TaskbarBlur : IDisposable
{
    private readonly Dictionary<nint, BlurSurface> _surfaces = new();
    internal IReadOnlyCollection<BlurSurface> Surfaces => _surfaces.Values;

    public void Apply(nint taskbar, Rectangle bounds, byte alpha, bool enabled)
    {
        if (!enabled || alpha == 0 || !Native.IsWindow(taskbar) || !IsWindowVisible(taskbar))
        {
            if (_surfaces.Remove(taskbar, out var previous)) previous.Dispose();
            return;
        }
        if (!_surfaces.TryGetValue(taskbar, out var surface))
        {
            surface = new BlurSurface();
            _surfaces.Add(taskbar, surface);
        }
        surface.Place(taskbar, bounds);
    }

    public void Retain(IEnumerable<nint> taskbars)
    {
        var live = taskbars.ToHashSet();
        foreach (var hwnd in _surfaces.Keys.Where(hwnd => !live.Contains(hwnd)).ToArray())
        {
            _surfaces[hwnd].Dispose();
            _surfaces.Remove(hwnd);
        }
    }

    public void Dispose()
    {
        foreach (var surface in _surfaces.Values) surface.Dispose();
        _surfaces.Clear();
    }

    internal sealed class BlurSurface : Form
    {
        public bool EffectAvailable { get; private set; }
        public BlurSurface()
        {
            Text = "Taskbar Proximity Blur";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.Black;
        }
        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                // WS_EX_LAYERED disables the acrylic blur on Windows 11. Keep this
                // surface non-layered; the taskbar above it handles pointer input.
                cp.ExStyle |= 0x08000000 | 0x00000080 | (int)Native.WS_EX_TRANSPARENT;
                return cp;
            }
        }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            if (DwmExtendFrameIntoClientArea(Handle, ref margins) != 0) return;
            // Acrylic (4) remains available on Windows 11, unlike legacy blur (3).
            var policy = new AccentPolicy { State = 4, GradientColor = 0x01000000 };
            var data = new CompositionData { Attribute = 19, Size = (nuint)Marshal.SizeOf<AccentPolicy>() };
            data.Data = Marshal.AllocHGlobal((int)data.Size);
            try
            {
                Marshal.StructureToPtr(policy, data.Data, false);
                EffectAvailable = SetWindowCompositionAttribute(Handle, ref data);
            }
            catch (EntryPointNotFoundException) { EffectAvailable = false; }
            finally { Marshal.FreeHGlobal(data.Data); }
        }
        public void Place(nint taskbar, Rectangle bounds)
        {
            _ = Handle;
            if (!EffectAvailable) return;
            if (Native.GetWindowRect(Handle, out var current) &&
                Rectangle.FromLTRB(current.Left, current.Top, current.Right, current.Bottom) == bounds &&
                IsWindowVisible(Handle) && GetWindow(taskbar, 2) == Handle) return;
            // Insert directly BELOW the taskbar. Never activate or cover its icons.
            Native.SetWindowPos(Handle, taskbar, bounds.X, bounds.Y, bounds.Width, bounds.Height,
                Native.SWP_NOACTIVATE | 0x0040 | 0x0200);
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0084) { m.Result = -1; return; } // HTTRANSPARENT
            if (m.Msg == 0x0021) { m.Result = 3; return; } // MA_NOACTIVATE
            base.WndProc(ref m);
        }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Margins { public int Left, Right, Top, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct AccentPolicy { public int State, Flags; public uint GradientColor; public int Animation; }
    [StructLayout(LayoutKind.Sequential)] private struct CompositionData { public int Attribute; public nint Data; public nuint Size; }
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] private static extern nint GetWindow(nint hwnd, uint command);
    [DllImport("user32.dll")] private static extern bool SetWindowCompositionAttribute(nint hwnd, ref CompositionData data);
    [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(nint hwnd, ref Margins margins);
}
