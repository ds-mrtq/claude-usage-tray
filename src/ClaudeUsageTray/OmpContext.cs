using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ClaudeUsageTray;

/// <summary>Context của phiên OMP vừa ghi gần nhất và ngưỡng auto-compact OMP sẽ dùng cho phiên đó.</summary>
public sealed class OmpContextInfo
{
    public double Tokens { get; init; }
    public double Window { get; init; }
    public double AutoCompactAt { get; init; }
    public string? Model { get; init; }
    public string? Title { get; init; }
    public DateTimeOffset At { get; init; }
    public double Percent => Window > 0 ? Math.Round(Tokens / Window * 100) : 0;
}

/// <summary>
/// Đọc ~/.omp/agent/sessions/&lt;thư mục làm việc&gt;/&lt;phiên&gt;.jsonl: lấy usage của câu trả lời assistant cuối
/// (bỏ câu bị huỷ/lỗi) như OMP tính context: contextTokens, không có thì input + cacheRead + cacheWrite.
/// Cửa sổ model lấy từ models.db (contextWindow). Ngưỡng theo compaction-threshold của OMP 18.18:
/// thresholdTokens nếu đặt; thresholdPercent nếu đặt; mặc định cửa sổ − max(15% cửa sổ, reserveTokens 16384).
/// Bỏ các thư mục bắt đầu bằng "__" (phiên nội bộ như advisor).
/// </summary>
public static class OmpContextReader
{
    private const int DefaultReserve = 16384;

    public static OmpContextInfo? TryLoad(string agentDir)
    {
        try
        {
            var sessions = Path.Combine(agentDir, "sessions");
            if (!Directory.Exists(sessions)) return null;
            var file = Directory.EnumerateDirectories(sessions)
                .Where(d => !Path.GetFileName(d).StartsWith("__"))
                .SelectMany(d => Directory.EnumerateFiles(d, "*.jsonl"))
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault();
            if (file == null) return null;

            var (usage, model) = LastUsage(file.FullName);
            if (usage == null) return null;
            var window = ModelWindow(Path.Combine(agentDir, "models.db"), model) ?? 200_000;
            return new OmpContextInfo
            {
                Tokens = usage.Value,
                Window = window,
                AutoCompactAt = Threshold(window, ReadCompaction(Path.Combine(agentDir, "config.yml"))),
                Model = model,
                Title = Title(file.FullName),
                At = new DateTimeOffset(file.LastWriteTimeUtc).ToLocalTime(),
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Đọc ngược phần cuối file (tối đa 4 MB) tìm usage của câu trả lời assistant gần nhất.</summary>
    private static (double? tokens, string? model) LastUsage(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        for (long take = 256 * 1024; ; take *= 4)
        {
            var start = Math.Max(0, fs.Length - take);
            fs.Seek(start, SeekOrigin.Begin);
            var buf = new byte[fs.Length - start];
            fs.ReadExactly(buf);
            var lines = Encoding.UTF8.GetString(buf).Split('\n');
            for (var i = lines.Length - 1; i >= (start > 0 ? 1 : 0); i--)
            {
                var l = lines[i];
                if (!l.Contains("\"usage\"") || !l.Contains("\"assistant\"")) continue;
                try
                {
                    using var doc = JsonDocument.Parse(l);
                    var r = doc.RootElement;
                    if (Str(r, "type") != "message" || !r.TryGetProperty("message", out var m) || Str(m, "role") != "assistant") continue;
                    if (Str(m, "stopReason") is "aborted" or "error") continue;
                    if (!m.TryGetProperty("usage", out var u) || u.ValueKind != JsonValueKind.Object) continue;
                    double t = Num(u, "contextTokens") ?? 0;
                    if (t <= 0) t = (Num(u, "input") ?? 0) + (Num(u, "cacheRead") ?? 0) + (Num(u, "cacheWrite") ?? 0);
                    if (t <= 0) continue;
                    return (t, Str(m, "model"));
                }
                catch (JsonException) { }
            }
            if (start == 0 || take >= 4 * 1024 * 1024) return (null, null);
        }
    }

    private static string? Title(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var sr = new StreamReader(fs, Encoding.UTF8);
        var first = sr.ReadLine();
        if (first == null) return null;
        try
        {
            using var doc = JsonDocument.Parse(first);
            if (Str(doc.RootElement, "type") != "title") return null;
            var t = Str(doc.RootElement, "title");
            // Bỏ emoji/ký hiệu đầu tiêu đề (font monospace của bảng không vẽ được)
            return t == null ? null : Regex.Replace(t, @"[^\p{L}\p{N}\p{P}\s]", "").Trim();
        }
        catch (JsonException) { return null; }
    }

    private static double? ModelWindow(string modelsDb, string? model)
    {
        if (model == null) return null;
        var json = OmpUsage.ScalarText(modelsDb, "select models from model_cache where provider_id = 'anthropic'");
        if (json == null) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            IEnumerable<JsonElement> items = doc.RootElement.ValueKind switch
            {
                JsonValueKind.Array => doc.RootElement.EnumerateArray(),
                JsonValueKind.Object => doc.RootElement.EnumerateObject().Select(p => p.Value),
                _ => [],
            };
            foreach (var e in items)
                if (e.ValueKind == JsonValueKind.Object && Str(e, "id") == model && Num(e, "contextWindow") is { } w && w > 0) return w;
        }
        catch (JsonException) { }
        return null;
    }

    /// <summary>Đọc compaction.thresholdPercent / thresholdTokens / reserveTokens trong khối "compaction:" của config.yml.</summary>
    private static (double? percent, double? tokens, double? reserve) ReadCompaction(string configYml)
    {
        if (!File.Exists(configYml)) return (null, null, null);
        double? pct = null, tok = null, res = null;
        var inBlock = false;
        foreach (var raw in File.ReadLines(configYml))
        {
            if (raw.Length > 0 && !char.IsWhiteSpace(raw[0])) inBlock = raw.TrimEnd().StartsWith("compaction:");
            if (!inBlock) continue;
            var m = Regex.Match(raw, @"^\s+(thresholdPercent|thresholdTokens|reserveTokens):\s*[""']?([0-9.]+)\s*(%|[kKmM])?[""']?\s*(#.*)?$");
            if (!m.Success || !double.TryParse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture, out var v)) continue;
            var unit = m.Groups[3].Value.ToLowerInvariant();
            v *= unit == "k" ? 1000 : unit == "m" ? 1e6 : 1;
            switch (m.Groups[1].Value)
            {
                case "thresholdPercent": pct = v; break;
                case "thresholdTokens": tok = v; break;
                case "reserveTokens": res = v; break;
            }
        }
        return (pct, tok, res);
    }

    private static double Threshold(double window, (double? percent, double? tokens, double? reserve) c)
    {
        double Default()
        {
            var reserve = Math.Max(Math.Floor(window * 0.15), c.reserve ?? DefaultReserve);
            var floor15 = Math.Max(1, Math.Floor(window * 0.15));
            if ((c.reserve == null && reserve >= window - floor15) || reserve >= window) reserve = floor15;
            return Math.Max(0, Math.Min(window - 1, window - reserve));
        }
        if (c.tokens is > 0) return c.tokens >= window ? Default() : Math.Min(window - 1, c.tokens.Value);
        if (c.percent is > 0) return Math.Floor(window * Math.Clamp(c.percent.Value, 1, 99) / 100);
        return Default();
    }

    private static string? Str(JsonElement e, string n) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static double? Num(JsonElement e, string n) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
}
