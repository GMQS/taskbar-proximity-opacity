using System.Runtime.InteropServices;
using System.Text.Json;

namespace TaskbarProximityOpacity;

internal sealed class WorkAreaController
{
    private readonly string _recoveryPath;
    private readonly Dictionary<string, Entry> _original = new(StringComparer.OrdinalIgnoreCase);
    public sealed record Entry(string DeviceName, Native.RECT Original, Native.RECT Applied);
    private static readonly JsonSerializerOptions JsonOptions = new() { IncludeFields = true, WriteIndented = true };

    public WorkAreaController(string? recoveryPath = null)
    {
        _recoveryPath = recoveryPath ?? Path.Combine(SettingsStore.DirectoryPath, "work-area-recovery.json");
        if (File.Exists(_recoveryPath))
        {
            try
            {
                foreach (var entry in JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(_recoveryPath), JsonOptions) ?? [])
                    _original[entry.DeviceName] = entry;
                RestoreAll();
            }
            catch (JsonException) { }
        }
    }

    public void Update(HashSet<nint> enabledMonitors)
    {
        foreach (var (monitor, info) in GetMonitors())
        {
            if (!enabledMonitors.Contains(monitor))
            {
                Restore(info);
                continue;
            }
            if (info.Work.Equals(info.Monitor)) continue;
            // Explorer may recalculate the work area after a display/taskbar change.
            // Capture that new baseline before overriding it, never our own full-screen area.
            _original[info.DeviceName] = new Entry(info.DeviceName, info.Work, info.Monitor);
            SaveRecovery();
            SetWorkArea(info.Monitor);
        }
    }

    public void RestoreAll()
    {
        foreach (var (_, info) in GetMonitors()) Restore(info);
    }

    private void Restore(Native.MONITORINFOEX info)
    {
        if (!_original.TryGetValue(info.DeviceName, out var entry)) return;
        // Do not restore stale coordinates following a resolution change or override
        // a work area that Windows or another utility has since changed.
        if (info.Monitor.Equals(entry.Applied) && info.Work.Equals(entry.Applied) && !SetWorkArea(entry.Original)) return;
        _original.Remove(info.DeviceName);
        SaveRecovery();
    }

    private void SaveRecovery()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_recoveryPath)!);
        if (_original.Count == 0)
        {
            if (File.Exists(_recoveryPath)) File.Delete(_recoveryPath);
            return;
        }
        string temporary = _recoveryPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(_original.Values, JsonOptions));
        File.Move(temporary, _recoveryPath, overwrite: true);
    }

    private static List<(nint Monitor, Native.MONITORINFOEX Info)> GetMonitors()
    {
        var result = new List<(nint, Native.MONITORINFOEX)>();
        Native.EnumDisplayMonitors(0, 0, (monitor, _, _, _) =>
        {
            var info = new Native.MONITORINFOEX { Size = Marshal.SizeOf<Native.MONITORINFOEX>() };
            if (Native.GetMonitorInfo(monitor, ref info)) result.Add((monitor, info));
            return true;
        }, 0);
        return result;
    }

    // No SPIF_UPDATEINIFILE: this is a temporary session change. The rectangle
    // selects the monitor, including monitors with negative desktop coordinates.
    // Do not broadcast WM_SETTINGCHANGE: Explorer immediately reserves the taskbar
    // area again in response. Apps pick up the new area on their next maximize.
    private static bool SetWorkArea(Native.RECT rect) => SystemParametersInfo(0x002F, 0, ref rect, 0);
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, ref Native.RECT rect, uint flags);
}
