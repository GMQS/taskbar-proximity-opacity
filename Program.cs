using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace TaskbarProximityOpacity;

internal static class Program
{
    private static Mutex? _mutex;

    [STAThread]
    private static void Main()
    {
        _mutex = new Mutex(true, "Local\\TaskbarProximityOpacity.Singleton", out bool createdNew);
        if (!createdNew) return;

        ApplicationConfiguration.Initialize();
        using var context = new TrayContext();
        Application.Run(context);
    }
}

internal sealed class TrayContext : ApplicationContext
{
    internal const string BuildExitEventName = "Local\\TaskbarProximityOpacity.BuildExit";
    private readonly TaskbarController _controller;
    private readonly NotifyIcon _tray;
    private readonly ToolStripMenuItem _pauseItem;
    private readonly EventWaitHandle _buildExitEvent;
    private readonly System.Windows.Forms.Timer _buildExitTimer;

    public TrayContext()
    {
        var settings = SettingsStore.Load();
        settings.StartWithWindows = StartupEnabled();
        _controller = new TaskbarController(settings);
        _pauseItem = new ToolStripMenuItem("一時停止");
        _pauseItem.Click += (_, _) =>
        {
            _controller.Paused = !_controller.Paused;
            _pauseItem.Checked = _controller.Paused;
            _pauseItem.Text = _controller.Paused ? "再開" : "一時停止";
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("設定...", null, (_, _) => OpenSettings()));
        menu.Items.Add(_pauseItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("終了", null, (_, _) => ExitThread()));

        _tray = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "Taskbar Proximity Opacity",
            ContextMenuStrip = menu,
            Visible = true
        };
        _tray.DoubleClick += (_, _) => OpenSettings();

        _buildExitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, BuildExitEventName);
        _buildExitTimer = new System.Windows.Forms.Timer { Interval = 250 };
        _buildExitTimer.Tick += (_, _) =>
        {
            if (_buildExitEvent.WaitOne(0)) ExitThread();
        };
        _buildExitTimer.Start();
    }

    private void OpenSettings()
    {
        using var form = new SettingsForm(_controller.Settings);
        if (form.ShowDialog() == DialogResult.OK)
        {
            _controller.UpdateSettings(form.Result);
            SettingsStore.Save(form.Result);
            SetStartup(form.Result.StartWithWindows);
        }
    }

    private static void SetStartup(bool enabled)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
        if (key is null) return;
        const string valueName = "TaskbarProximityOpacity";
        if (enabled) key.SetValue(valueName, $"\"{Environment.ProcessPath}\"");
        else key.DeleteValue(valueName, throwOnMissingValue: false);
    }

    private static bool StartupEnabled()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        return key?.GetValue("TaskbarProximityOpacity") is string;
    }

    protected override void ExitThreadCore()
    {
        _buildExitTimer.Stop();
        _buildExitTimer.Dispose();
        _buildExitEvent.Dispose();
        _controller.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        base.ExitThreadCore();
    }
}

internal sealed class SettingsForm : Form
{
    private readonly NumericUpDown _distance;
    private readonly NumericUpDown _idleDelay;
    private readonly CheckBox _startup;
    private readonly CheckedListBox _monitors;
    public AppSettings Result { get; private set; }

    public SettingsForm(AppSettings settings)
    {
        Result = settings.Clone();
        Text = "Taskbar Proximity Opacity 設定";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(490, 350);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 2, RowCount = 6 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        Controls.Add(layout);

        layout.Controls.Add(new Label { Text = "透過を開始する距離（タスクバーと垂直な画面寸法の %）", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        _distance = Number(1, 100, (decimal)(settings.FadeDistanceRatio * 100), 1);
        layout.Controls.Add(_distance, 1, 0);
        layout.Controls.Add(new Label { Text = "表示範囲内で停止してから消えるまで（秒）", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        _idleDelay = Number(0.1m, 60, settings.IdleDelayMilliseconds / 1000m, 0.1m);
        _idleDelay.DecimalPlaces = 1;
        layout.Controls.Add(_idleDelay, 1, 1);

        layout.Controls.Add(new Label { Text = "制御するディスプレイ", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
        _monitors = new CheckedListBox { CheckOnClick = true, Height = 150, Dock = DockStyle.Fill };
        foreach (var display in MonitorCatalog.GetDisplays())
        {
            bool enabled = !settings.DisabledDisplays.Contains(display.DeviceName, StringComparer.OrdinalIgnoreCase);
            _monitors.Items.Add(new MonitorChoice(display), enabled);
        }
        layout.SetColumnSpan(_monitors, 2);
        layout.Controls.Add(_monitors, 0, 3);

        _startup = new CheckBox { Text = "Windowsへのサインイン時に起動", AutoSize = true, Checked = settings.StartWithWindows, Anchor = AnchorStyles.Left };
        layout.SetColumnSpan(_startup, 2);
        layout.Controls.Add(_startup, 0, 4);

        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
        var ok = new Button { Text = "保存", DialogResult = DialogResult.OK, Width = 90 };
        var cancel = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel, Width = 90 };
        ok.Click += (_, _) => CollectSettings();
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        layout.SetColumnSpan(buttons, 2);
        layout.Controls.Add(buttons, 0, 5);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    private void CollectSettings()
    {
        Result.FadeDistanceRatio = (double)_distance.Value / 100.0;
        Result.IdleDelayMilliseconds = (int)(_idleDelay.Value * 1000);
        Result.StartWithWindows = _startup.Checked;
        Result.DisabledDisplays = _monitors.Items.Cast<MonitorChoice>()
            .Where((_, index) => !_monitors.GetItemChecked(index))
            .Select(item => item.Display.DeviceName).ToList();
    }

    private static NumericUpDown Number(decimal min, decimal max, decimal value, decimal increment) =>
        new() { Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max), Increment = increment, Width = 110, Anchor = AnchorStyles.Left };

    private sealed record MonitorChoice(DisplayInfo Display)
    {
        public override string ToString() => $"{Display.FriendlyName} ({Display.Bounds.Width}×{Display.Bounds.Height})";
    }
}

internal sealed class TaskbarController : IDisposable
{
    private readonly System.Windows.Forms.Timer _timer;
    private readonly Dictionary<nint, TaskbarState> _taskbars = new();
    private readonly Dictionary<nint, byte> _currentOpacity = new();
    private readonly HashSet<nint> _idleTaskbars = [];
    private readonly Dictionary<nint, FadeTransition> _fadeTransitions = new();
    private readonly TaskbarBlur _blur = new();
    private const int FadeDurationMilliseconds = 250;
    private bool _transparencyEnabled = TransparencyPreference.Read();
    private long _lastAppearanceScan = -1000;
    private Point _lastCursor = new(int.MinValue, int.MinValue);
    private long _lastMovementAt;
    private bool _hasCursorSample;
    private long _lastTopologyScan;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    public AppSettings Settings { get; private set; }
    private bool _paused;
    public bool Paused
    {
        get => _paused;
        set
        {
            _paused = value;
            _lastCursor = new Point(int.MinValue, int.MinValue);
            _hasCursorSample = false;
            _idleTaskbars.Clear();
            _fadeTransitions.Clear();
            _lastAppearanceScan = -1000;
            if (!value) return;
            _blur.Dispose();
            foreach (var state in _taskbars.Values)
            {
                if (Native.IsWindow(state.Hwnd) && ApplyOpacity(state.Hwnd, 255, state.OriginalExStyle))
                    _currentOpacity[state.Hwnd] = 255;
            }
        }
    }

    public TaskbarController(AppSettings settings)
    {
        Settings = settings;
        Directory.CreateDirectory(SettingsStore.DirectoryPath);
        RecoverInterruptedRun();
        RefreshTaskbars();
        _timer = new System.Windows.Forms.Timer { Interval = 30 };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    public void UpdateSettings(AppSettings settings)
    {
        Settings = settings;
        _lastCursor = new Point(int.MinValue, int.MinValue);
        _hasCursorSample = false;
        _idleTaskbars.Clear();
        _fadeTransitions.Clear();
        _lastAppearanceScan = -1000;
    }

    private void Tick()
    {
        if (Paused) return;
        long now = _clock.ElapsedMilliseconds;
        bool refreshAppearance = now - _lastAppearanceScan >= 1000;
        if (refreshAppearance)
        {
            _transparencyEnabled = TransparencyPreference.Read(_transparencyEnabled);
            _lastAppearanceScan = now;
        }
        bool refreshTopology = now - _lastTopologyScan >= 2000;
        if (refreshTopology)
        {
            RefreshTaskbars();
            _lastTopologyScan = now;
        }

        if (!Native.GetCursorPos(out var point)) return;
        bool cursorMoved = !_hasCursorSample || _lastCursor != point;
        if (cursorMoved) _lastMovementAt = now;
        _hasCursorSample = true;
        _lastCursor = point;
        bool idleDue = now - _lastMovementAt >= Math.Clamp(Settings.IdleDelayMilliseconds, 100, 60000);
        bool idleTransitionDue = idleDue && _taskbars.Values.Any(state =>
            CalculateOpacity(point, state) > 0 && !_idleTaskbars.Contains(state.Hwnd) &&
            !Settings.DisabledDisplays.Contains(state.Display.DeviceName, StringComparer.OrdinalIgnoreCase));
        if (!cursorMoved && !refreshAppearance && !refreshTopology &&
            _fadeTransitions.Count == 0 && !idleTransitionDue) return;

        foreach (var pair in _taskbars.ToArray())
        {
            var hwnd = pair.Key;
            var state = pair.Value;
            if (!Native.IsWindow(hwnd))
            {
                _taskbars.Remove(hwnd);
                _currentOpacity.Remove(hwnd);
                _idleTaskbars.Remove(hwnd);
                _fadeTransitions.Remove(hwnd);
                continue;
            }

            bool enabled = !Settings.DisabledDisplays.Contains(state.Display.DeviceName, StringComparer.OrdinalIgnoreCase);
            byte normalOpacity = enabled ? CalculateOpacity(point, state) : (byte)255;
            bool idle = enabled && normalOpacity > 0 && idleDue;
            byte target = idle ? (byte)0 : normalOpacity;
            if (!_currentOpacity.TryGetValue(hwnd, out var current)) current = state.OriginalAlpha;
            if (idle != _idleTaskbars.Contains(hwnd))
            {
                if (idle) _idleTaskbars.Add(hwnd);
                else _idleTaskbars.Remove(hwnd);
                _fadeTransitions[hwnd] = new FadeTransition(current, now);
            }
            if (_fadeTransitions.TryGetValue(hwnd, out var fade))
            {
                double progress = Math.Clamp((double)(now - fade.StartTime) / FadeDurationMilliseconds, 0, 1);
                target = (byte)Math.Clamp(Math.Round(fade.StartOpacity + (target - fade.StartOpacity) * progress), 0, 255);
                if (progress >= 1) _fadeTransitions.Remove(hwnd);
            }
            if (target != current && ApplyOpacity(hwnd, target, state.OriginalExStyle)) _currentOpacity[hwnd] = target;
            _blur.Apply(hwnd, state.Bounds, _currentOpacity.GetValueOrDefault(hwnd, current), enabled && _transparencyEnabled);
        }
    }

    private readonly record struct FadeTransition(byte StartOpacity, long StartTime);

    private byte CalculateOpacity(Point cursor, TaskbarState state)
    {
        var r = state.Bounds;
        double dx = Math.Max(Math.Max(r.Left - cursor.X, 0), cursor.X - r.Right);
        double dy = Math.Max(Math.Max(r.Top - cursor.Y, 0), cursor.Y - r.Bottom);
        double distance = Math.Sqrt(dx * dx + dy * dy);
        double reference = state.Bounds.Height >= state.Bounds.Width ? state.Display.Bounds.Width : state.Display.Bounds.Height;
        double range = Math.Max(1, reference * Settings.FadeDistanceRatio);
        double progress = Math.Clamp(1.0 - distance / range, 0, 1);
        // Bring the taskbar up to a perceptible opacity early in the approach.
        progress = Math.Pow(progress, 0.65);
        return (byte)Math.Clamp(Math.Round(255 * progress), 0, 255);
    }

    private void RefreshTaskbars()
    {
        var displays = MonitorCatalog.GetDisplays().ToDictionary(d => d.Monitor, d => d);
        var found = new Dictionary<nint, TaskbarState>();
        bool addedTaskbar = false;
        nint primary = Native.FindWindow("Shell_TrayWnd", null);
        Add(primary);
        Native.EnumWindows((hwnd, _) =>
        {
            var className = Native.GetClassName(hwnd);
            if (className == "Shell_SecondaryTrayWnd") Add(hwnd);
            return true;
        }, 0);

        void Add(nint hwnd)
        {
            if (hwnd == 0 || !Native.GetWindowRect(hwnd, out var rect)) return;
            nint monitor = Native.MonitorFromWindow(hwnd, Native.MONITOR_DEFAULTTONEAREST);
            if (!displays.TryGetValue(monitor, out var display)) return;
            uint exStyle = Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE);
            bool layered = (exStyle & Native.WS_EX_LAYERED) != 0;
            byte alpha = 255;
            uint flags = 0;
            uint colorKey = 0;
            if (layered) Native.GetLayeredWindowAttributes(hwnd, out colorKey, out alpha, out flags);
            var bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
            TaskbarState state;
            if (_taskbars.TryGetValue(hwnd, out var previous))
            {
                state = previous.WithCurrentBounds(display, bounds);
            }
            else
            {
                state = new TaskbarState(hwnd, display, bounds, exStyle, layered, colorKey, alpha, flags);
                addedTaskbar = true;
            }
            found[hwnd] = state;
            if (!_currentOpacity.ContainsKey(hwnd)) _currentOpacity[hwnd] = alpha;
        }

        _taskbars.Clear();
        foreach (var pair in found) _taskbars[pair.Key] = pair.Value;
        _idleTaskbars.RemoveWhere(hwnd => !found.ContainsKey(hwnd));
        foreach (var hwnd in _fadeTransitions.Keys.Where(hwnd => !found.ContainsKey(hwnd)).ToArray())
            _fadeTransitions.Remove(hwnd);
        _blur.Retain(found.Keys);
        if (addedTaskbar) SaveRecovery();
    }

    private static bool ApplyOpacity(nint hwnd, byte alpha, uint originalExStyle)
    {
        uint style = Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE);
        style |= Native.WS_EX_LAYERED;
        if ((originalExStyle & Native.WS_EX_TRANSPARENT) == 0)
        {
            if (alpha == 0) style |= Native.WS_EX_TRANSPARENT;
            else style &= ~Native.WS_EX_TRANSPARENT;
        }
        Native.SetWindowLong(hwnd, Native.GWL_EXSTYLE, style);
        return Native.SetLayeredWindowAttributes(hwnd, 0, alpha, Native.LWA_ALPHA);
    }

    private static string RecoveryPath => Path.Combine(SettingsStore.DirectoryPath, "recovery.json");

    private void RecoverInterruptedRun()
    {
        try
        {
            if (!File.Exists(RecoveryPath)) return;
            var entries = JsonSerializer.Deserialize<List<RecoveryEntry>>(File.ReadAllText(RecoveryPath)) ?? [];
            var unresolved = new List<RecoveryEntry>();
            foreach (var entry in entries)
            {
                nint hwnd = FindTaskbarForDevice(entry.ClassName, entry.DeviceName);
                if (hwnd == 0) { unresolved.Add(entry); continue; }
                Restore(hwnd, entry);
            }
            if (unresolved.Count == 0) File.Delete(RecoveryPath);
            else File.WriteAllText(RecoveryPath, JsonSerializer.Serialize(unresolved, SettingsStore.JsonOptions));
        }
        catch { }
    }

    private static nint FindTaskbarForDevice(string className, string deviceName)
    {
        nint result = 0;
        Native.EnumWindows((hwnd, _) =>
        {
            if (Native.GetClassName(hwnd) != className) return true;
            nint monitor = Native.MonitorFromWindow(hwnd, Native.MONITOR_DEFAULTTONEAREST);
            var display = MonitorCatalog.GetDisplays().FirstOrDefault(d => d.Monitor == monitor);
            if (display?.DeviceName.Equals(deviceName, StringComparison.OrdinalIgnoreCase) == true)
            { result = hwnd; return false; }
            return true;
        }, 0);
        return result;
    }

    private static void Restore(nint hwnd, RecoveryEntry state)
    {
        Native.SetWindowLong(hwnd, Native.GWL_EXSTYLE, state.ExStyle);
        if (state.Layered)
            Native.SetLayeredWindowAttributes(hwnd, state.ColorKey, state.Alpha, state.Flags);
        else
            Native.SetLayeredWindowAttributes(hwnd, 0, 255, Native.LWA_ALPHA);
        Native.SetWindowPos(hwnd, 0, 0, 0, 0, 0,
            Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_FRAMECHANGED);
    }

    private void SaveRecovery()
    {
        var entries = _taskbars.Values.Select(s => new RecoveryEntry
        {
            DeviceName = s.Display.DeviceName,
            ClassName = Native.GetClassName(s.Hwnd),
            ExStyle = s.OriginalExStyle,
            Layered = s.WasLayered,
            ColorKey = s.ColorKey,
            Alpha = s.OriginalAlpha,
            Flags = s.Flags
        }).ToList();
        File.WriteAllText(RecoveryPath, JsonSerializer.Serialize(entries, SettingsStore.JsonOptions));
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Dispose();
        _blur.Dispose();
        foreach (var state in _taskbars.Values)
        {
            if (Native.IsWindow(state.Hwnd))
            {
                var entry = new RecoveryEntry
                {
                    DeviceName = state.Display.DeviceName,
                    ClassName = Native.GetClassName(state.Hwnd),
                    ExStyle = state.OriginalExStyle,
                    Layered = state.WasLayered,
                    ColorKey = state.ColorKey,
                    Alpha = state.OriginalAlpha,
                    Flags = state.Flags
                };
                Restore(state.Hwnd, entry);
            }
        }
        try { if (File.Exists(RecoveryPath)) File.Delete(RecoveryPath); } catch { }
    }

    private sealed class TaskbarState
    {
        public TaskbarState(nint hwnd, DisplayInfo display, Rectangle bounds, uint originalExStyle,
            bool wasLayered, uint colorKey, byte originalAlpha, uint flags)
        {
            Hwnd = hwnd;
            Display = display;
            Bounds = bounds;
            OriginalExStyle = originalExStyle;
            WasLayered = wasLayered;
            ColorKey = colorKey;
            OriginalAlpha = originalAlpha;
            Flags = flags;
        }

        public nint Hwnd { get; }
        public DisplayInfo Display { get; }
        public Rectangle Bounds { get; }
        public uint OriginalExStyle { get; }
        public bool WasLayered { get; }
        public uint ColorKey { get; }
        public byte OriginalAlpha { get; }
        public uint Flags { get; }
        public TaskbarState WithCurrentBounds(DisplayInfo currentDisplay, Rectangle currentBounds)
        {
            return new TaskbarState(Hwnd, currentDisplay, currentBounds, OriginalExStyle, WasLayered, ColorKey, OriginalAlpha, Flags);
        }
    }

    private sealed class RecoveryEntry
    {
        public string DeviceName { get; set; } = "";
        public string ClassName { get; set; } = "";
        public uint ExStyle { get; set; }
        public bool Layered { get; set; }
        public uint ColorKey { get; set; }
        public byte Alpha { get; set; }
        public uint Flags { get; set; }
    }
}

internal sealed class AppSettings
{
    public double FadeDistanceRatio { get; set; } = 0.35;
    public int IdleDelayMilliseconds { get; set; } = 2000;
    public bool StartWithWindows { get; set; }
    public List<string> DisabledDisplays { get; set; } = [];
    public AppSettings Clone() => new()
    {
        FadeDistanceRatio = FadeDistanceRatio,
        IdleDelayMilliseconds = IdleDelayMilliseconds,
        StartWithWindows = StartWithWindows,
        DisabledDisplays = [.. DisabledDisplays]
    };
}

internal static class SettingsStore
{
    public static string DirectoryPath { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TaskbarProximityOpacity");
    public static string SettingsPath => Path.Combine(DirectoryPath, "settings.json");
    public static JsonSerializerOptions JsonOptions { get; } = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath)) return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new AppSettings();
        }
        catch { }
        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(DirectoryPath);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
    }
}

internal sealed record DisplayInfo(nint Monitor, string DeviceName, string FriendlyName, Rectangle Bounds);

internal static class MonitorCatalog
{
    public static List<DisplayInfo> GetDisplays()
    {
        var list = new List<DisplayInfo>();
        Native.EnumDisplayMonitors(0, 0, (monitor, _, _, _) =>
        {
            var info = new Native.MONITORINFOEX { Size = Marshal.SizeOf<Native.MONITORINFOEX>() };
            if (Native.GetMonitorInfo(monitor, ref info))
            {
                var bounds = Rectangle.FromLTRB(info.Monitor.Left, info.Monitor.Top, info.Monitor.Right, info.Monitor.Bottom);
                string name = Screen.AllScreens.FirstOrDefault(s => s.DeviceName.Equals(info.DeviceName, StringComparison.OrdinalIgnoreCase))?.DeviceName ?? info.DeviceName;
                list.Add(new DisplayInfo(monitor, info.DeviceName, name, bounds));
            }
            return true;
        }, 0);
        return list;
    }
}

internal static class Native
{
    public const int GWL_EXSTYLE = -20;
    public const uint WS_EX_LAYERED = 0x00080000;
    public const uint WS_EX_TRANSPARENT = 0x00000020;
    public const uint LWA_ALPHA = 0x00000002;
    public const uint LWA_COLORKEY = 0x00000001;
    public const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010, SWP_FRAMECHANGED = 0x0020;
    public const uint MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)] public struct MONITORINFOEX
    {
        public int Size; public RECT Monitor; public RECT Work; public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
    }

    public delegate bool MonitorEnumProc(nint monitor, nint hdc, nint rect, nint data);
    public delegate bool EnumWindowsProc(nint hwnd, nint data);

    [DllImport("user32.dll")] public static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] public static extern nint MonitorFromPoint(Point point, uint flags);
    [DllImport("user32.dll")] public static extern nint MonitorFromWindow(nint hwnd, uint flags);
    [DllImport("user32.dll")] public static extern bool EnumDisplayMonitors(nint hdc, nint clip, MonitorEnumProc callback, nint data);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] public static extern bool GetMonitorInfo(nint monitor, ref MONITORINFOEX info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern nint FindWindow(string? className, string? windowName);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc callback, nint data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetClassNameW")] private static extern int GetClassNameNative(nint hwnd, System.Text.StringBuilder className, int maxCount);
    public static string GetClassName(nint hwnd) { var sb = new System.Text.StringBuilder(256); GetClassNameNative(hwnd, sb, sb.Capacity); return sb.ToString(); }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(nint hwnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong32(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr64(nint hwnd, int index);
    public static uint GetWindowLong(nint hwnd, int index) => IntPtr.Size == 8 ? unchecked((uint)GetWindowLongPtr64(hwnd, index).ToInt64()) : unchecked((uint)GetWindowLong32(hwnd, index));
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong32(nint hwnd, int index, int value);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr64(nint hwnd, int index, nint value);
    public static void SetWindowLong(nint hwnd, int index, uint value)
    {
        if (IntPtr.Size == 8) SetWindowLongPtr64(hwnd, index, (nint)value);
        else SetWindowLong32(hwnd, index, unchecked((int)value));
    }
    [DllImport("user32.dll")] public static extern bool SetLayeredWindowAttributes(nint hwnd, uint colorKey, byte alpha, uint flags);
    [DllImport("user32.dll")] public static extern bool GetLayeredWindowAttributes(nint hwnd, out uint colorKey, out byte alpha, out uint flags);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);
}
