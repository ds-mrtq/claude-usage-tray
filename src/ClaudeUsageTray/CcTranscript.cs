using System.Text;
using System.Text.RegularExpressions;

namespace ClaudeUsageTray;

/// <summary>
/// Đọc transcript Claude Code (~/.claude/projects/&lt;thư mục mã hoá&gt;/&lt;sessionId&gt;.jsonl) để lấy
/// tiêu đề phiên (dòng customTitle mới nhất, là tên hiện ở sidebar app) và giờ mở phiên (timestamp đầu tiên).
/// Transcript có thể vài chục MB nên chỉ đọc đầu và đuôi file, cache theo độ dài file.
/// </summary>
public static partial class CcTranscript
{
    private const int Chunk = 512 * 1024;
    private static readonly Dictionary<string, (long Len, string? Title, DateTimeOffset? Started)> Cache = new();

    [GeneratedRegex("\"customTitle\":\"((?:[^\"\\\\]|\\\\.)*)\"")]
    private static partial Regex TitleRx();
    [GeneratedRegex("\"timestamp\":\"([^\"]+)\"")]
    private static partial Regex TimeRx();

    public static string ProjectsDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");

    public static (string? Title, DateTimeOffset? Started) Info(string? sessionId, string? cwd)
    {
        if (string.IsNullOrEmpty(sessionId)) return (null, null);
        var path = Find(sessionId, cwd);
        if (path == null) return (null, null);
        try
        {
            var len = new FileInfo(path).Length;
            lock (Cache)
                if (Cache.TryGetValue(path, out var c) && c.Len == len) return (c.Title, c.Started);

            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var head = Read(fs, 0, (int)Math.Min(Chunk, len));
            var tail = len > Chunk ? Read(fs, Math.Max(Chunk, len - Chunk), (int)Math.Min(Chunk, len - Chunk)) : "";

            DateTimeOffset? started = null;
            var tm = TimeRx().Match(head);
            if (tm.Success && DateTimeOffset.TryParse(tm.Groups[1].Value, out var t)) started = t;

            var title = LastTitle(tail) ?? LastTitle(head);
            lock (Cache) Cache[path] = (len, title, started);
            return (title, started);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return (null, null); }
    }

    private static string? LastTitle(string text)
    {
        var ms = TitleRx().Matches(text);
        if (ms.Count == 0) return null;
        try { return Regex.Unescape(ms[^1].Groups[1].Value).Trim() is { Length: > 0 } s ? s : null; }
        catch (ArgumentException) { return ms[^1].Groups[1].Value; }
    }

    private static string Read(FileStream fs, long offset, int count)
    {
        if (count <= 0) return "";
        var buf = new byte[count];
        fs.Seek(offset, SeekOrigin.Begin);
        var n = 0;
        while (n < count) { var r = fs.Read(buf, n, count - n); if (r == 0) break; n += r; }
        return Encoding.UTF8.GetString(buf, 0, n);
    }

    private static string? Find(string sessionId, string? cwd)
    {
        var root = ProjectsDir;
        if (!Directory.Exists(root)) return null;
        if (!string.IsNullOrEmpty(cwd))
        {
            // Claude Code mã hoá đường dẫn: mọi ký tự không phải chữ/số thành '-'
            var enc = Regex.Replace(cwd, "[^A-Za-z0-9]", "-");
            var p = Path.Combine(root, enc, sessionId + ".jsonl");
            if (File.Exists(p)) return p;
        }
        try
        {
            foreach (var d in Directory.EnumerateDirectories(root))
            {
                var p = Path.Combine(d, sessionId + ".jsonl");
                if (File.Exists(p)) return p;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return null;
    }
}
