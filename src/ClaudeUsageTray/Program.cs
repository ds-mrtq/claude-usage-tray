namespace ClaudeUsageTray;

internal static class Program
{
    /// <summary>
    /// Chạy: ClaudeUsageTray.exe [--show] [--data &lt;file json&gt;] [--omp &lt;agent.db của OMP | none&gt;]
    /// --show: mở bảng ngay (shortcut dùng); app đã chạy rồi thì lần chạy sau chỉ báo bản đang chạy mở bảng.
    /// Kiểm tra không cần giao diện: --render &lt;thư mục&gt; [--api] xuất panel.png và icon.png rồi thoát
    /// (--api: lấy hạn mức mới nhất từ API Anthropic trước, như bấm nút làm mới).
    /// </summary>
    [STAThread]
    private static int Main(string[] args)
    {
        var path = Arg(args, "--data") ?? UsageData.DefaultPath;
        var omp = Arg(args, "--omp") ?? OmpUsage.DefaultPath;
        if (omp == "none") omp = null;

        var renderDir = Arg(args, "--render");
        if (renderDir != null) return RenderToFiles(path, omp, renderDir);

        using var mutex = new Mutex(true, @"Local\ClaudeUsageTray", out var isNew);
        if (!isNew)
        {
            // Đã có một bản đang chạy: nhờ bản đó mở bảng rồi thoát
            try { using var ev = EventWaitHandle.OpenExisting(ShowEventName); ev.Set(); }
            catch (WaitHandleCannotBeOpenedException) { }
            return 0;
        }

        using var show = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApp(path, omp, show, args.Contains("--show")));
        return 0;
    }

    private const string ShowEventName = @"Local\ClaudeUsageTray.Show";

    private static int RenderToFiles(string path, string? omp, string dir)
    {
        Directory.CreateDirectory(dir);
        var now = DateTimeOffset.Now;
        // --api: gọi API làm mới một lần (như bấm nút) trước khi vẽ
        ApiUsage.Result? api = null;
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "--api") >= 0)
        {
            var (r, err) = ApiUsage.FetchAsync(now).GetAwaiter().GetResult();
            Console.WriteLine(r != null ? "API: " + string.Join(", ", r.Limits.Select(l => $"{l.Kind} {l.PercentUsed}% reset {l.ResetsAt:HH:mm dd/MM}")) : "API lỗi: " + err);
            api = r;
        }
        var d = UsageData.Load(path, omp, now, api);
        const float scale = 1.5f;
        var w = (int)(340 * scale);
        var h = Renderer.DrawPanel(null, d, w, scale, now);
        using (var bmp = new Bitmap(w, h))
        using (var g = Graphics.FromImage(bmp))
        {
            g.SetClip(new Rectangle(0, 0, w, h));
            Renderer.DrawPanel(g, d, w, scale, now);
            bmp.Save(Path.Combine(dir, "panel.png"), System.Drawing.Imaging.ImageFormat.Png);
        }
        using (var icon = Renderer.DrawIcon(d, 64, now))
            icon.Save(Path.Combine(dir, "icon.png"), System.Drawing.Imaging.ImageFormat.Png);
        Console.WriteLine(Renderer.Tooltip(d, now));
        return d == null ? 2 : 0;
    }

    private static string? Arg(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
