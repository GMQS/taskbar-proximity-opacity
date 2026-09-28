using TaskbarProximityOpacity;
using System.Text.Json;

internal static class Harness
{
    [STAThread]
    static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        if (args.Contains("--blur-demo")) { BlurDemo.Run(); return; }
        try { BlurChecks.Run(Check); }
        catch (Exception ex) { Console.WriteLine(ex); Environment.ExitCode = 1; }
        try { ChromiumResizeChecks.Run(Check); }
        catch (Exception ex) { Console.WriteLine(ex); Environment.ExitCode = 1; }
        Check(WorkAreaManager.ExpandTaskbarEdge(new Rectangle(0, 0, 1920, 1080),
            new Rectangle(0, 0, 1920, 1032), new Rectangle(0, 1032, 1920, 48)) ==
            new Rectangle(0, 0, 1920, 1080), "Hidden bottom taskbar releases work area");
        Check(WorkAreaManager.ExpandTaskbarEdge(new Rectangle(-1280, 0, 1280, 1024),
            Rectangle.FromLTRB(-1240, 0, 0, 1024), Rectangle.FromLTRB(-1280, 0, -1240, 1024)) ==
            new Rectangle(-1280, 0, 1280, 1024), "Hidden left taskbar releases secondary monitor work area");
        Check(JsonSerializer.Deserialize<Rectangle>(JsonSerializer.Serialize(new Rectangle(-1280, 0, 1280, 1024))) ==
            new Rectangle(-1280, 0, 1280, 1024), "Work area recovery rectangle survives JSON round trip");
        Check(WorkAreaManager.ExpandTaskbarEdge(new Rectangle(0, 0, 1920, 1080),
            new Rectangle(0, 0, 1920, 1000), new Rectangle(0, 1032, 1920, 48)) ==
            new Rectangle(0, 0, 1920, 1000), "Another appbar on the same edge keeps its reserved area");
        Check(WorkAreaManager.IsNearWorkArea(Rectangle.FromLTRB(-8, -8, 1928, 1040),
            Rectangle.FromLTRB(0, 0, 1920, 1032)), "Maximized invisible window borders remain eligible");
        Check(WorkAreaManager.MoveWithWorkArea(Rectangle.FromLTRB(-8, -8, 1928, 1040),
            Rectangle.FromLTRB(0, 0, 1920, 1032), Rectangle.FromLTRB(0, 0, 1920, 1080)) ==
            Rectangle.FromLTRB(-8, -8, 1928, 1088), "Existing maximized window expands with work area");
        Check(WorkAreaManager.Interpolate(new Rectangle(0, 0, 1920, 1032),
            new Rectangle(0, 0, 1920, 1080), 0.5) == new Rectangle(0, 0, 1920, 1056),
            "Work area window size changes gradually");
        Check(WorkAreaManager.Interpolate(new Rectangle(0, 0, 1920, 1056),
            new Rectangle(0, 0, 1920, 1032), 1) == new Rectangle(0, 0, 1920, 1032),
            "Reversed animation reaches its new target");
        Check(WorkAreaManager.AnimationDuration(new Rectangle(0, 0, 1920, 1080),
            new Rectangle(0, 0, 1920, 1032)) == 400 &&
            WorkAreaManager.AnimationDuration(new Rectangle(0, 0, 1920, 1032),
            new Rectangle(0, 0, 1920, 1080)) == 250,
            "Shrinking takes longer than expanding");
        Check(WorkAreaManager.AnchorToWorkArea(new Rectangle(0, 0, 1920, 1032), 8, 8, 8, 8) ==
            Rectangle.FromLTRB(-8, -8, 1928, 1040),
            "Maximized target uses the work area and frame margins instead of accumulating size changes");
    }

    static void Check(bool condition, string name)
    {
        Console.WriteLine((condition ? "PASS: " : "FAIL: ") + name);
        if (!condition) Environment.ExitCode = 1;
    }
}
