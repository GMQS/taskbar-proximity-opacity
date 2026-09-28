using System.Runtime.InteropServices;
using System.Text.Json;
using System.Diagnostics;

namespace TaskbarProximityOpacity;

// Windows uses each monitor's work area when maximizing ordinary windows.
internal sealed class WorkAreaManager : IDisposable
{
    private readonly Dictionary<string, Change> _changes = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _attemptedWhileHidden = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _restoreAttempted = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _pendingRestores = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<nint, WindowAnimation> _animations = new();
    private readonly Stopwatch _animationClock = Stopwatch.StartNew();
    private readonly System.Windows.Forms.Timer _animationTimer = new() { Interval = 20 };
    private const int ExpandMilliseconds = 250;
    private const int ShrinkMilliseconds = 400;
    private static string RecoveryPath => Path.Combine(SettingsStore.DirectoryPath, "workareas.json");

    public WorkAreaManager()
    {
        _animationTimer.Tick += (_, _) => StepAnimations();
        try
        {
            if (!File.Exists(RecoveryPath)) return;
            var changes = JsonSerializer.Deserialize<List<Change>>(File.ReadAllText(RecoveryPath)) ?? [];
            foreach (var change in changes)
            {
                var display = MonitorCatalog.GetDisplays().FirstOrDefault(d =>
                    d.DeviceName.Equals(change.DeviceName, StringComparison.OrdinalIgnoreCase) && d.Bounds == change.MonitorBounds);
                if (display is null || !TryGetWorkArea(display.Monitor, out var current) || current != change.Applied) continue;
                var windows = CaptureWorkAreaWindows(display, current);
                var original = ToNative(change.Original);
                if (Native.SetWorkArea(Native.SPI_SETWORKAREA, 0, ref original, 0))
                    ResizeUnchangedWindows(windows, current, change.Original);
            }
        }
        catch { }
        finally { try { File.Delete(RecoveryPath); } catch { } }
    }

    public void Update(DisplayInfo display, Rectangle taskbar, bool hidden)
    {
        string key = display.DeviceName;
        if (!hidden)
        {
            _attemptedWhileHidden.Remove(key);
            Restore(key);
            return;
        }
        if (_pendingRestores.Remove(key) && _changes.TryGetValue(key, out var pending))
        {
            _restoreAttempted.Remove(key);
            _attemptedWhileHidden.Add(key);
            var reversingWindows = CaptureWorkAreaWindows(display, pending.Original);
            AnimateUnchangedWindows(display, reversingWindows, pending.Original, pending.Applied);
            return;
        }
        // Explorer may reclaim its work area. Never fight it every 30 ms.
        if (!_attemptedWhileHidden.Add(key)) return;
        if (!TryGetWorkArea(display.Monitor, out var work)) return;

        var expanded = ExpandTaskbarEdge(display.Bounds, work, taskbar);
        if (expanded == work) return;
        var change = new Change(key, display.Bounds, work, expanded);
        _changes[key] = change;
        SaveRecovery(); // Persist before changing a system-wide setting.
        var windows = CaptureWorkAreaWindows(display, work);
        var native = ToNative(expanded);
        if (!Native.SetWorkArea(Native.SPI_SETWORKAREA, 0, ref native, 0))
        {
            _changes.Remove(key);
            SaveRecovery();
        }
        else if (TryGetWorkArea(display.Monitor, out var applied) && applied == expanded)
            AnimateUnchangedWindows(display, windows, work, expanded);
    }

    public void Retain(IEnumerable<string> deviceNames)
    {
        var active = new HashSet<string>(deviceNames, StringComparer.OrdinalIgnoreCase);
        foreach (var key in _changes.Keys.Where(key => !active.Contains(key)).ToArray())
            RestoreImmediately(key);
        _attemptedWhileHidden.RemoveWhere(key => !active.Contains(key));
        _restoreAttempted.RemoveWhere(key => !active.Contains(key));
        foreach (var hwnd in _animations.Where(pair => !active.Contains(pair.Value.DeviceName))
                     .Select(pair => pair.Key).ToArray()) _animations.Remove(hwnd);
        if (_animations.Count == 0 && _pendingRestores.Count == 0) _animationTimer.Stop();
    }

    public void RestoreAll()
    {
        foreach (var key in _changes.Keys.ToArray()) Restore(key);
        _attemptedWhileHidden.Clear();
    }

    private void Restore(string key)
    {
        if (!_changes.TryGetValue(key, out var change) || !_restoreAttempted.Add(key)) return;
        var display = MonitorCatalog.GetDisplays().FirstOrDefault(d =>
            d.DeviceName.Equals(key, StringComparison.OrdinalIgnoreCase) && d.Bounds == change.MonitorBounds);
        if (display is not null && TryGetWorkArea(display.Monitor, out var current) && current == change.Applied)
        {
            var windows = CaptureWorkAreaWindows(display, current);
            AnimateUnchangedWindows(display, windows, current, change.Original);
            _pendingRestores[key] = _animationClock.ElapsedMilliseconds + ShrinkMilliseconds;
            _animationTimer.Start();
            return;
        }
        _changes.Remove(key);
        _restoreAttempted.Remove(key);
        SaveRecovery();
    }

    private void RestoreImmediately(string key)
    {
        _pendingRestores.Remove(key);
        if (!_changes.TryGetValue(key, out var change)) return;
        var display = MonitorCatalog.GetDisplays().FirstOrDefault(d =>
            d.DeviceName.Equals(key, StringComparison.OrdinalIgnoreCase) && d.Bounds == change.MonitorBounds);
        if (display is not null && TryGetWorkArea(display.Monitor, out var current) && current == change.Applied)
        {
            var original = ToNative(change.Original);
            if (!Native.SetWorkArea(Native.SPI_SETWORKAREA, 0, ref original, 0)) return;
            // Chromium notices the new work area in WM_WINDOWPOSCHANGING.
            foreach (var window in CaptureWorkAreaWindows(display, change.Original))
            {
                if (!IsChromiumWindow(window.Hwnd)) continue;
                Native.SetWindowPos(window.Hwnd, 0, 0, 0, 0, 0,
                    Native.SWP_NOSIZE | Native.SWP_NOMOVE | Native.SWP_NOZORDER |
                    Native.SWP_NOACTIVATE | Native.SWP_ASYNCWINDOWPOS);
            }
        }
        _changes.Remove(key);
        _restoreAttempted.Remove(key);
        SaveRecovery();
    }

    internal static Rectangle ExpandTaskbarEdge(Rectangle monitor, Rectangle work, Rectangle taskbar)
    {
        // Preserve space reserved by other appbars on the remaining three edges.
        if (taskbar.Bottom >= monitor.Bottom && Math.Abs(taskbar.Top - work.Bottom) <= 2 && work.Bottom < monitor.Bottom)
            return Rectangle.FromLTRB(work.Left, work.Top, work.Right, monitor.Bottom);
        if (taskbar.Top <= monitor.Top && Math.Abs(taskbar.Bottom - work.Top) <= 2 && work.Top > monitor.Top)
            return Rectangle.FromLTRB(work.Left, monitor.Top, work.Right, work.Bottom);
        if (taskbar.Left <= monitor.Left && Math.Abs(taskbar.Right - work.Left) <= 2 && work.Left > monitor.Left)
            return Rectangle.FromLTRB(monitor.Left, work.Top, work.Right, work.Bottom);
        if (taskbar.Right >= monitor.Right && Math.Abs(taskbar.Left - work.Right) <= 2 && work.Right < monitor.Right)
            return Rectangle.FromLTRB(work.Left, work.Top, monitor.Right, work.Bottom);
        return work;
    }

    private static bool TryGetWorkArea(nint monitor, out Rectangle area)
    {
        var info = new Native.MONITORINFOEX { Size = Marshal.SizeOf<Native.MONITORINFOEX>() };
        if (Native.GetMonitorInfo(monitor, ref info))
        {
            area = Rectangle.FromLTRB(info.Work.Left, info.Work.Top, info.Work.Right, info.Work.Bottom);
            return true;
        }
        area = default;
        return false;
    }

    private static Native.RECT ToNative(Rectangle area) => new()
    { Left = area.Left, Top = area.Top, Right = area.Right, Bottom = area.Bottom };

    private List<WorkAreaWindow> CaptureWorkAreaWindows(DisplayInfo display, Rectangle work)
    {
        var windows = new List<WorkAreaWindow>();
        Native.EnumWindows((hwnd, _) =>
        {
            if (!Native.IsWindowVisible(hwnd) || Native.IsIconic(hwnd) ||
                Native.GetWindow(hwnd, Native.GW_OWNER) != 0 ||
                Native.MonitorFromWindow(hwnd, Native.MONITOR_DEFAULTTONEAREST) != display.Monitor ||
                !Native.GetWindowRect(hwnd, out var rect)) return true;

            var bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
            if (_animations.TryGetValue(hwnd, out var animation) &&
                animation.DeviceName.Equals(display.DeviceName, StringComparison.OrdinalIgnoreCase))
                windows.Add(new WorkAreaWindow(hwnd, bounds, animation.Target));
            else if (IsNearWorkArea(bounds, work) || IsManagedMaximized(hwnd))
                windows.Add(new WorkAreaWindow(hwnd, bounds, bounds));
            return true;
        }, 0);
        return windows;
    }

    internal static bool IsNearWorkArea(Rectangle window, Rectangle work) =>
        Math.Abs(window.Left - work.Left) <= 32 && Math.Abs(window.Top - work.Top) <= 32 &&
        Math.Abs(window.Right - work.Right) <= 32 && Math.Abs(window.Bottom - work.Bottom) <= 32;

    private static void ResizeUnchangedWindows(IEnumerable<WorkAreaWindow> windows, Rectangle oldWork, Rectangle newWork)
    {
        foreach (var window in windows)
        {
            if (!Native.IsWindow(window.Hwnd) ||
                !Native.GetWindowRect(window.Hwnd, out var rect)) continue;
            var current = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
            if (current != window.Bounds) continue; // Windows or the application already resized it.
            var target = TargetForWorkArea(window.Hwnd, window.ReferenceBounds, oldWork, newWork);
            Native.SetWindowPos(window.Hwnd, 0, target.Left, target.Top, target.Width, target.Height,
                Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_ASYNCWINDOWPOS);
        }
    }

    private void AnimateUnchangedWindows(DisplayInfo display, IEnumerable<WorkAreaWindow> windows,
        Rectangle oldWork, Rectangle newWork)
    {
        long now = _animationClock.ElapsedMilliseconds;
        foreach (var window in windows)
        {
            if (!Native.IsWindow(window.Hwnd) || !Native.GetWindowRect(window.Hwnd, out var rect)) continue;
            var current = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
            if (current != window.Bounds) continue; // Windows or the application already resized it.
            var target = TargetForWorkArea(window.Hwnd, window.ReferenceBounds, oldWork, newWork);
            if (current == target) { _animations.Remove(window.Hwnd); continue; }
            _animations[window.Hwnd] = new WindowAnimation(display.DeviceName, current, target, now,
                IsChromiumWindow(window.Hwnd) && AnimationDuration(current, target) == ShrinkMilliseconds);
        }
        if (_animations.Count > 0) _animationTimer.Start();
    }

    private void StepAnimations()
    {
        long now = _animationClock.ElapsedMilliseconds;
        foreach (var (hwnd, animation) in _animations.ToArray())
        {
            if (!Native.IsWindow(hwnd) || !Native.IsWindowVisible(hwnd) || Native.IsIconic(hwnd) ||
                !Native.GetWindowRect(hwnd, out var rect))
            {
                _animations.Remove(hwnd);
                continue;
            }
            var current = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
            double progress = Math.Clamp((double)(now - animation.StartTime) /
                AnimationDuration(animation.Start, animation.Target), 0, 1);
            var next = Interpolate(animation.Start, animation.Target, progress);
            if (next != animation.LastSent)
            {
                uint flags = Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_ASYNCWINDOWPOS;
                if (animation.BypassPositionChanging) flags |= Native.SWP_NOSENDCHANGING;
                Native.SetWindowPos(hwnd, 0, next.Left, next.Top, next.Width, next.Height,
                    flags);
                _animations[hwnd] = animation with { LastSent = next };
            }
            if (current == animation.Target ||
                now - animation.StartTime >= AnimationDuration(animation.Start, animation.Target) + 200)
                _animations.Remove(hwnd);
        }
        CommitPendingRestores(now);
        if (_animations.Count == 0 && _pendingRestores.Count == 0) _animationTimer.Stop();
    }

    private void CommitPendingRestores(long now)
    {
        foreach (var key in _pendingRestores.Where(pair => pair.Value <= now)
                     .Select(pair => pair.Key).ToArray())
        {
            if (_animations.Values.Any(animation =>
                    animation.DeviceName.Equals(key, StringComparison.OrdinalIgnoreCase)) &&
                now < _pendingRestores[key] + 200) continue;
            _pendingRestores.Remove(key);
            RestoreImmediately(key);
        }
    }

    internal static Rectangle Interpolate(Rectangle start, Rectangle target, double progress)
    {
        double t = Math.Clamp(progress, 0, 1);
        t = t * t * (3 - 2 * t);
        int Blend(int a, int b) => a + (int)Math.Round((b - a) * t);
        return Rectangle.FromLTRB(Blend(start.Left, target.Left), Blend(start.Top, target.Top),
            Blend(start.Right, target.Right), Blend(start.Bottom, target.Bottom));
    }

    internal static int AnimationDuration(Rectangle start, Rectangle target) =>
        (long)target.Width * target.Height < (long)start.Width * start.Height
            ? ShrinkMilliseconds : ExpandMilliseconds;

    private static bool IsChromiumWindow(nint hwnd) =>
        Native.GetClassName(hwnd) == "Chrome_WidgetWin_1";

    private static bool IsManagedMaximized(nint hwnd) =>
        Native.IsZoomed(hwnd) && (Native.GetWindowLong(hwnd, Native.GWL_STYLE) & Native.WS_CAPTION) != 0;

    private static Rectangle TargetForWorkArea(nint hwnd, Rectangle reference,
        Rectangle oldWork, Rectangle newWork)
    {
        if (IsManagedMaximized(hwnd) && Native.GetWindowRect(hwnd, out var rect) &&
            Native.DwmGetWindowAttribute(hwnd, 9, out var visible, Marshal.SizeOf<Native.RECT>()) == 0)
        {
            var margins = new FrameMargins(visible.Left - rect.Left, visible.Top - rect.Top,
                rect.Right - visible.Right, rect.Bottom - visible.Bottom);
            if (margins.IsValid) return AnchorToWorkArea(newWork, margins);
        }
        return MoveWithWorkArea(reference, oldWork, newWork);
    }

    private static Rectangle AnchorToWorkArea(Rectangle work, FrameMargins margins) =>
        Rectangle.FromLTRB(work.Left - margins.Left, work.Top - margins.Top,
            work.Right + margins.Right, work.Bottom + margins.Bottom);

    internal static Rectangle AnchorToWorkArea(Rectangle work, int left, int top, int right, int bottom) =>
        AnchorToWorkArea(work, new FrameMargins(left, top, right, bottom));

    public void Dispose()
    {
        RestoreAll();
        foreach (var key in _changes.Keys.ToArray()) RestoreImmediately(key);
        _animationTimer.Stop();
        _animationTimer.Dispose();
        foreach (var (hwnd, animation) in _animations)
            Native.SetWindowPos(hwnd, 0, animation.Target.Left, animation.Target.Top,
                animation.Target.Width, animation.Target.Height,
                Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_ASYNCWINDOWPOS);
        _animations.Clear();
    }

    internal static Rectangle MoveWithWorkArea(Rectangle window, Rectangle oldWork, Rectangle newWork) =>
        Rectangle.FromLTRB(
            window.Left + newWork.Left - oldWork.Left,
            window.Top + newWork.Top - oldWork.Top,
            window.Right + newWork.Right - oldWork.Right,
            window.Bottom + newWork.Bottom - oldWork.Bottom);

    private void SaveRecovery()
    {
        try
        {
            if (_changes.Count == 0) File.Delete(RecoveryPath);
            else File.WriteAllText(RecoveryPath, JsonSerializer.Serialize(_changes.Values.ToArray(), SettingsStore.JsonOptions));
        }
        catch { }
    }

    private sealed record Change(string DeviceName, Rectangle MonitorBounds, Rectangle Original, Rectangle Applied);
    private readonly record struct WorkAreaWindow(nint Hwnd, Rectangle Bounds, Rectangle ReferenceBounds);
    private readonly record struct FrameMargins(int Left, int Top, int Right, int Bottom)
    {
        public bool IsValid => Left is >= 0 and <= 32 && Top is >= 0 and <= 32 &&
            Right is >= 0 and <= 32 && Bottom is >= 0 and <= 32;
    }
    private readonly record struct WindowAnimation(string DeviceName, Rectangle Start, Rectangle Target,
        long StartTime, bool BypassPositionChanging)
    {
        public Rectangle LastSent { get; init; } = Start;
    }
}
