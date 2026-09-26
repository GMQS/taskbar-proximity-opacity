using System.Runtime.InteropServices;
using TaskbarProximityOpacity;
internal static class BlurChecks
{
    public static void Run(Action<bool, string> check)
    {
        using var taskbar = new Form { Text = "Blur lifecycle verification", ShowInTaskbar = false, FormBorderStyle = FormBorderStyle.None, Bounds = new Rectangle(100, 200, 500, 60), Opacity = 0.5 };
        taskbar.Show();
        Application.DoEvents();
        using var blur = new TaskbarBlur();
        nint hwnd = taskbar.Handle;
        uint originalStyle = Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE);
        Native.GetLayeredWindowAttributes(hwnd, out _, out byte originalAlpha, out _);
        nint focus = GetForegroundWindow();
        blur.Apply(hwnd, taskbar.Bounds, 128, false);
        check(blur.Surfaces.Count == 0, "Transparency OFF has no blur surface");
        blur.Apply(hwnd, taskbar.Bounds, 128, true);
        var surface = blur.Surfaces.Single();
        nint surfaceHandle = surface.Handle;
        check(surface.EffectAvailable, "Acrylic composition API accepted");
        check((Native.GetWindowLong(surfaceHandle, Native.GWL_EXSTYLE) & Native.WS_EX_LAYERED) == 0, "Acrylic surface is non-layered so blur is preserved");
        check(GetWindow(hwnd, 2) == surfaceHandle, "Blur surface is behind the taskbar");
        check(GetForegroundWindow() == focus, "Blur does not steal focus");
        Native.GetWindowRect(surfaceHandle, out var rect);
        check(Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom) == taskbar.Bounds, "Blur is confined to taskbar bounds");
        blur.Apply(hwnd, taskbar.Bounds, 64, true);
        check(blur.Surfaces.Single().Handle == surfaceHandle, "Opacity changes reuse the blur surface");
        Native.GetLayeredWindowAttributes(hwnd, out _, out byte afterAlpha, out _);
        check(originalStyle == Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE) && originalAlpha == afterAlpha, "Blur does not change taskbar opacity or styles");
        blur.Apply(hwnd, taskbar.Bounds, 0, true);
        check(blur.Surfaces.Count == 0 && !Native.IsWindow(surfaceHandle), "Fully transparent taskbar removes blur");
        blur.Apply(hwnd, taskbar.Bounds, 128, true);
        blur.Apply(hwnd, taskbar.Bounds, 128, false);
        check(blur.Surfaces.Count == 0, "Turning transparency OFF removes existing blur");
        blur.Apply(hwnd, taskbar.Bounds, 128, true);
        blur.Retain([]);
        check(blur.Surfaces.Count == 0, "Removed taskbar removes blur surface");
        blur.Apply(hwnd, taskbar.Bounds, 128, true);
        surfaceHandle = blur.Surfaces.Single().Handle;
        blur.Dispose();
        check(!Native.IsWindow(surfaceHandle), "Pause and exit destroy the blur surface");
        taskbar.Close();
    }
    [DllImport("user32.dll")] static extern nint GetWindow(nint hwnd, uint command);
    [DllImport("user32.dll")] static extern nint GetForegroundWindow();
}
