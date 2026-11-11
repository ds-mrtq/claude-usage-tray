using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ClaudeUsageTray;

internal sealed class TrayApp : ApplicationContext
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunName = "ClaudeUsageTray";

    private readonly string _path;
    private readonly NotifyIcon _icon;
    private readonly PopupForm _popup = new();
    private readonly FileSystemWatcher? _watcher;
    private readonly System.Windows.Forms.Timer _tick = new() { Interval = 30_000 };
    private readonly System.Windows.Forms.Timer _debounce = new() { Interval = 300 };
    private readonly ToolStripMenuItem _startup;
    private UsageData? _data;
    private IntPtr _hIcon;

    public TrayApp(string path)
    {
        _path = path;
        _ = _popup.Handle; // tạo handle sớm để BeginInvoke từ thread của FileSystemWatcher
        _startup = new ToolStripMenuItem("Chạy cùng Windows", null, (_, _) => ToggleStartup()) { Checked = IsStartupEnabled() };
        var menu = new ContextMenuStrip();
        menu.Items.Add("Mở bảng", null, (_, _) => _popup.ShowNearTray());
        menu.Items.Add(_startup);
        menu.Items.Add("Mở thư mục dữ liệu", null, (_, _) => OpenDataFolder());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Thoát", null, (_, _) => ExitThread());

        _icon = new NotifyIcon { ContextMenuStrip = menu, Visible = true, Text = "Claude Usage" };
        _icon.MouseClick += (_, e) =>
        {
            Log.Write($"MouseClick {e.Button} visible={_popup.Visible}");
            if (e.Button != MouseButtons.Left) return;
            if (_popup.Visible) _popup.Hide(); else _popup.ShowNearTray();
        };

        var dir = Path.GetDirectoryName(path)!;
        if (Directory.Exists(dir))
        {
            _watcher = new FileSystemWatcher(dir, Path.GetFileName(path))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                EnableRaisingEvents = true,
            };
            _watcher.Changed += (_, _) => Kick();
            _watcher.Created += (_, _) => Kick();
            _watcher.Renamed += (_, _) => Kick();
        }
        _debounce.Tick += (_, _) => { _debounce.Stop(); Reload(); };
        _tick.Tick += (_, _) => Reload(); // vẽ lại đếm ngược reset và trạng thái "cũ"
        _tick.Start();
        Reload();
    }

    // FileSystemWatcher bắn sự kiện trên thread khác: đẩy về UI thread qua handle của popup
    private void Kick() => _popup.BeginInvoke(() => { _debounce.Stop(); _debounce.Start(); });

    private void Reload()
    {
        _data = UsageData.TryLoad(_path) ?? _data;
        var now = DateTimeOffset.Now;
        var size = Math.Max(16, SystemInformation.SmallIconSize.Width);
        using var bmp = Renderer.DrawIcon(_data, size, now);
        var h = bmp.GetHicon();
        _icon.Icon = Icon.FromHandle(h);
        if (_hIcon != IntPtr.Zero) DestroyIcon(_hIcon);
        _hIcon = h;
        _icon.Text = Renderer.Tooltip(_data, now);
        _popup.SetData(_data);
    }

    private static bool IsStartupEnabled()
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey);
        return k?.GetValue(RunName) is string;
    }

    private void ToggleStartup()
    {
        using var k = Registry.CurrentUser.CreateSubKey(RunKey);
        if (IsStartupEnabled()) k.DeleteValue(RunName, false);
        else k.SetValue(RunName, $"\"{Application.ExecutablePath}\"");
        _startup.Checked = IsStartupEnabled();
    }

    private void OpenDataFolder()
    {
        var dir = Path.GetDirectoryName(_path);
        if (dir != null && Directory.Exists(dir))
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{_path}\"") { UseShellExecute = true });
    }

    protected override void ExitThreadCore()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _watcher?.Dispose();
        _tick.Dispose();
        _debounce.Dispose();
        _popup.Dispose();
        if (_hIcon != IntPtr.Zero) DestroyIcon(_hIcon);
        base.ExitThreadCore();
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
