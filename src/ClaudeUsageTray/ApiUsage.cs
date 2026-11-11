using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;

namespace ClaudeUsageTray;

/// <summary>
/// Lấy hạn mức mới nhất thẳng từ Anthropic (GET api.anthropic.com/api/oauth/usage), chỉ khi anh bấm làm mới.
/// Token OAuth đọc chỉ-đọc từ ~/.claude/.credentials.json do Claude Code giữ; app không tự làm mới token,
/// không ghi file credential, không ghi token ra log, chỉ gửi token tới api.anthropic.com.
/// Endpoint không công bố và có giới hạn tần suất theo User-Agent: gửi "claude-code/&lt;phiên bản&gt;" lấy từ
/// `claude --version` (không bịa phiên bản), và tự giãn cách các lần gọi.
/// </summary>
public static class ApiUsage
{
    public sealed record Result(DateTimeOffset RecordedAt, List<LimitInfo> Limits);

    public static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(30);

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static string? _ua;
    private static bool _uaTried;
    private static DateTimeOffset _notBefore = DateTimeOffset.MinValue;

    private static string ConfigDir =>
        Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") is { Length: > 0 } d
            ? d
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");

    /// <summary>Trả (kết quả, null) hoặc (null, lý do bằng tiếng Việt ngắn).</summary>
    public static async Task<(Result? result, string? error)> FetchAsync(DateTimeOffset now)
    {
        if (now < _notBefore) return (null, $"đợi {Math.Ceiling((_notBefore - now).TotalSeconds)}s");

        string? token;
        try { token = ReadToken(now, out var err); if (token == null) return (null, err); }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return (null, "không đọc được credential của Claude Code");
        }

        _ua ??= await Task.Run(ClaudeVersion);
        using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.anthropic.com/api/oauth/usage");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Headers.Add("anthropic-beta", "oauth-2025-04-20");
        if (_ua != null) req.Headers.TryAddWithoutValidation("User-Agent", _ua);

        try
        {
            using var res = await Http.SendAsync(req);
            _notBefore = now + MinInterval;
            if ((int)res.StatusCode == 429)
            {
                var wait = res.Headers.RetryAfter?.Delta ?? TimeSpan.FromMinutes(5);
                _notBefore = now + wait;
                return (null, $"Anthropic giới hạn tần suất, thử lại sau {Fmt.Dur(wait)}");
            }
            if ((int)res.StatusCode == 401) return (null, "token hết hạn: chạy một lượt Claude Code rồi làm mới");
            if (!res.IsSuccessStatusCode) return (null, $"API trả HTTP {(int)res.StatusCode}");
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStreamAsync());
            var limits = Parse(doc.RootElement, now);
            return limits.Count == 0 ? (null, "API không trả hạn mức nào") : (new Result(now, limits), null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _notBefore = now + TimeSpan.FromSeconds(5);
            return (null, "không gọi được API (mạng?)");
        }
    }

    private static string? ReadToken(DateTimeOffset now, out string? error)
    {
        error = null;
        var path = Path.Combine(ConfigDir, ".credentials.json");
        if (!File.Exists(path)) { error = "chưa có credential: đăng nhập Claude Code trước"; return null; }
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var doc = JsonDocument.Parse(fs);
        var o = doc.RootElement.TryGetProperty("claudeAiOauth", out var inner) ? inner : doc.RootElement;
        if (!o.TryGetProperty("accessToken", out var t) || t.GetString() is not { Length: > 0 } token)
        {
            error = "credential không có access token";
            return null;
        }
        if (o.TryGetProperty("expiresAt", out var exp) && exp.ValueKind == JsonValueKind.Number
            && DateTimeOffset.FromUnixTimeMilliseconds(exp.GetInt64()) <= now)
        {
            error = "token hết hạn: chạy một lượt Claude Code rồi làm mới";
            return null;
        }
        return token;
    }

    /// <summary>"claude-code/2.1.322" từ `claude --version`; không tìm được thì null (không gửi User-Agent).</summary>
    private static string? ClaudeVersion()
    {
        if (_uaTried) return null;
        _uaTried = true;
        var exe = FindClaude();
        if (exe == null) return null;
        try
        {
            using var p = Process.Start(new ProcessStartInfo(exe, "--version")
            {
                RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true,
            });
            if (p == null) return null;
            var outText = p.StandardOutput.ReadToEnd();
            if (!p.WaitForExit(5000)) { p.Kill(); return null; }
            var m = System.Text.RegularExpressions.Regex.Match(outText, @"^\s*(\d+\.\d+\.\d+)");
            return m.Success ? "claude-code/" + m.Groups[1].Value : null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }

    private static string? FindClaude()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Prepend(Path.Combine(home, ".local", "bin"));
        foreach (var d in dirs)
        {
            try
            {
                var f = Path.Combine(d.Trim('"'), "claude.exe");
                if (File.Exists(f)) return f;
            }
            catch (ArgumentException) { /* PATH có mục hỏng */ }
        }
        return null;
    }

    private static List<LimitInfo> Parse(JsonElement root, DateTimeOffset now)
    {
        var list = new List<LimitInfo>();
        void Add(string kind, string label, double pct, string? resets, TimeSpan? window)
        {
            if (list.Any(l => l.Kind == kind)) return;
            var l = new LimitInfo { Kind = kind, Label = label, PercentUsed = Math.Round(pct, 1), Source = "API" };
            if (resets != null && DateTimeOffset.TryParse(resets, out var r))
            {
                l.ResetsAt = r.ToLocalTime();
                if (window != null)
                {
                    var left = (r - now).TotalMilliseconds;
                    if (left > 0) l.ExpectedPercent = Math.Clamp((1 - left / window.Value.TotalMilliseconds) * 100, 0, 100);
                }
            }
            list.Add(l);
        }
        static string? Str(JsonElement e, string name) =>
            e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        static double? Num(JsonElement e, string name) =>
            e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

        // Dạng mới: mảng limits (session, weekly_all, weekly_scoped theo model)
        if (root.TryGetProperty("limits", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in arr.EnumerateArray())
            {
                var pct = Num(e, "percent");
                if (pct == null) continue;
                var resets = Str(e, "resets_at");
                switch (Str(e, "kind"))
                {
                    case "session": Add("five_hour", "PHIÊN 5 GIỜ", pct.Value, resets, TimeSpan.FromHours(5)); break;
                    case "weekly_all": Add("seven_day", "TUẦN", pct.Value, resets, TimeSpan.FromDays(7)); break;
                    case "weekly_scoped":
                        string? name = null;
                        if (e.TryGetProperty("scope", out var sc) && sc.ValueKind == JsonValueKind.Object
                            && sc.TryGetProperty("model", out var mo)) name = Str(mo, "display_name");
                        if (name == null) break;
                        Add("seven_day_" + name.ToLowerInvariant(), "TUẦN · " + name.ToUpperInvariant(), pct.Value, resets, TimeSpan.FromDays(7));
                        break;
                }
            }
        }
        // Dạng cũ: five_hour / seven_day { utilization, resets_at }
        foreach (var (key, kind, label, win) in new[]
                 {
                     ("five_hour", "five_hour", "PHIÊN 5 GIỜ", TimeSpan.FromHours(5)),
                     ("seven_day", "seven_day", "TUẦN", TimeSpan.FromDays(7)),
                 })
        {
            if (root.TryGetProperty(key, out var o) && Num(o, "utilization") is { } u) Add(kind, label, u, Str(o, "resets_at"), win);
        }
        // Extra usage chỉ hiện khi đang bật
        if (root.TryGetProperty("extra_usage", out var ex) && ex.ValueKind == JsonValueKind.Object
            && ex.TryGetProperty("is_enabled", out var en) && en.ValueKind == JsonValueKind.True && Num(ex, "utilization") is { } xu)
            Add("extra", "EXTRA USAGE", xu, null, null);

        list.Sort((a, b) => OmpUsage.Order(a.Kind).CompareTo(OmpUsage.Order(b.Kind)));
        return list;
    }
}
