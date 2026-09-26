using System.Runtime.InteropServices;
using TaskbarProximityOpacity;
internal static class Harness
{
    [STAThread]
    static void Main()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        string journal = Path.Combine(AppContext.BaseDirectory, "work-area-test.json");
        var controller = new WorkAreaController(journal);
        try
        {
            foreach (var display in MonitorCatalog.GetDisplays())
            {
                var monitor = display.Monitor;
                var before = Info(monitor);
                controller.Update([]);
                Check(Info(monitor).Work.Equals(before.Work), "Disabled display unchanged: " + display.DeviceName);
                controller.Update([monitor]);
                Check(Info(monitor).Work.Equals(before.Monitor), "Work area fills display: " + display.DeviceName);
                using var form = new Form { Text = "Taskbar maximize verification", ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(before.Monitor.Left + 100, before.Monitor.Top + 100) };
                form.Show();
                form.WindowState = FormWindowState.Maximized;
                Application.DoEvents();
                Thread.Sleep(400);
                DwmGetWindowAttribute(form.Handle, 9, out var visible, 16);
                Check(visible.Equals(before.Monitor), "Maximized window fills display: " + display.DeviceName);
                controller.Update([monitor]);
                Check(Info(monitor).Work.Equals(before.Monitor), "Repeated update remains stable");
                controller.Update([]);
                Check(Info(monitor).Work.Equals(before.Work), "Disable restores work area");
                controller.Update([monitor]);
                controller.RestoreAll();
                Check(Info(monitor).Work.Equals(before.Work), "Pause/exit restores work area");
                controller.Update([monitor]);
                var recovery = new WorkAreaController(journal);
                Check(Info(monitor).Work.Equals(before.Work), "Restart recovers journal");
                controller.RestoreAll();
                form.Close();
            }
        }
        catch (Exception ex) { Console.WriteLine(ex); Environment.ExitCode = 1; }
        finally { controller.RestoreAll(); }
    }
    static Native.MONITORINFOEX Info(nint monitor)
    {
        var info = new Native.MONITORINFOEX { Size = Marshal.SizeOf<Native.MONITORINFOEX>() };
        if (!Native.GetMonitorInfo(monitor, ref info)) throw new Exception("GetMonitorInfo failed");
        return info;
    }
    static void Check(bool condition, string name) { Console.WriteLine((condition ? "PASS: " : "FAIL: ") + name); if (!condition) Environment.ExitCode = 1; }
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(nint hwnd, int attr, out Native.RECT rect, int size);
}
