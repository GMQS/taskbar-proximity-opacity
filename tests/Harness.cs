using TaskbarProximityOpacity;

internal static class Harness
{
    [STAThread]
    static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        if (args.Contains("--blur-demo")) { BlurDemo.Run(); return; }
        try { BlurChecks.Run(Check); }
        catch (Exception ex) { Console.WriteLine(ex); Environment.ExitCode = 1; }
    }

    static void Check(bool condition, string name)
    {
        Console.WriteLine((condition ? "PASS: " : "FAIL: ") + name);
        if (!condition) Environment.ExitCode = 1;
    }
}
