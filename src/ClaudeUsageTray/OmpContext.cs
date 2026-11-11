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
    public string Path { get; init; } = "";
    /// <summary>Tổng usage.cost.total của mọi câu trả lời trong file phiên (USD, theo bảng giá OMP).</summary>
    public double CostUsd { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
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

    /// <summary>Phiên OMP ghi gần nhất (bất kể bao lâu).</summary>
    public static OmpContextInfo? TryLoad(string agentDir)
    {
        var f = Files(agentDir).OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault();
        return f == null ? null : Load(agentDir, f);
    }

    /// <summary>
    /// Phiên OMP coi là đang chạy: có tiến trình omp và file phiên được ghi trong khoảng <paramref name="within"/>.
    /// OMP không ghi dấu kết thúc phiên, nên phiên mở mà rảnh lâu hơn khoảng này sẽ không hiện.
    /// </summary>
    public static List<OmpContextInfo> Active(string agentDir, TimeSpan within)
    {
        var list = new List<OmpContextInfo>();
        try
        {
            if (System.Diagnostics.Process.GetProcessesByName("omp").Length == 0) return list;
        }
        catch (InvalidOperationException) { return list; }
        var since = DateTime.UtcNow - within;
        foreach (var f in Files(agentDir).Where(f => f.LastWriteTimeUtc >= since).OrderByDescending(f => f.LastWriteTimeUtc))
            if (Load(agentDir, f) is { } o) list.Add(o);
        return list;
    }

    private static IEnumerable<FileInfo> Files(string agentDir)
    {
        var sessions = Path.Combine(agentDir, "sessions");
        if (!Directory.Exists(sessions)) return [];
        try
        {
            return Directory.EnumerateDirectories(sessions)
                .Where(d => !Path.GetFileName(d).StartsWith("__"))
                .SelectMany(d => Directory.EnumerateFiles(d, "*.jsonl"))
                .Select(f => new FileInfo(f))
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }

    private static OmpContextInfo? Load(string agentDir, FileInfo file)
    {
        try
        {
            var sc = Scan(file);
            if (sc.Tokens == null) return null;
            var window = ModelWindow(Path.Combine(agentDir, "models.db"), sc.Model) ?? 200_000;
            var at = new DateTimeOffset(file.LastWriteTimeUtc).ToLocalTime();
            return new OmpContextInfo
            {
                Path = file.FullName,
                Tokens = sc.Tokens.Value,
                Window = window,
                AutoCompactAt = Threshold(window, ReadCompaction(Path.Combine(agentDir, "config.yml"))),
                Model = sc.Model,
                Title = Title(file.FullName),
                At = at,
                CostUsd = sc.Cost,
                StartedAt = sc.First,
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // File phiên OMP có thể tới hàng chục MB: đọc tiếp từ chỗ đã đọc lần trước (chỉ phần mới ghi thêm)
    private sealed class ScanState
    {
        public long Offset;
        public string Partial = "";
        public double Cost;
        public DateTimeOffset? First;
        public double? Tokens;
        public string? Model;
    }

    private static readonly Dictionary<string, ScanState> Cache = new(StringComparer.OrdinalIgnoreCase);

    private static ScanState Scan(FileInfo file)
    {
        if (!Cache.TryGetValue(file.FullName, out var st) || file.Length < st.Offset)
            Cache[file.FullName] = st = new ScanState();
        if (file.Length == st.Offset) return st;
        using var fs = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        fs.Seek(st.Offset, SeekOrigin.Begin);
        var buf = new byte[fs.Length - st.Offset];
        fs.ReadExactly(buf);
        st.Offset += buf.Length;
        var text = st.Partial + Encoding.UTF8.GetString(buf);
        var cut = text.LastIndexOf('\n');
        st.Partial = cut < 0 ? text : text[(cut + 1)..];
        if (cut < 0) return st;
        foreach (var l in text[..cut].Split('\n'))
        {
            if (st.First == null && l.Contains("\"timestamp\""))
            {
                var m = Regex.Match(l, "\"timestamp\":\"([^\"]+)\"");
                if (m.Success && DateTimeOffset.TryParse(m.Groups[1].Value, out var ts)) st.First = ts.ToLocalTime();
            }
            if (!l.Contains("\"usage\"") || !l.Contains("\"assistant\"")) continue;
            try
            {
                using var doc = JsonDocument.Parse(l);
                var r = doc.RootElement;
                if (Str(r, "type") != "message" || !r.TryGetProperty("message", out var msg) || Str(msg, "role") != "assistant") continue;
                if (!msg.TryGetProperty("usage", out var u) || u.ValueKind != JsonValueKind.Object) continue;
                if (u.TryGetProperty("cost", out var c) && Num(c, "total") is { } usd) st.Cost += usd;
                if (Str(msg, "stopReason") is "aborted" or "error") continue;
                double t = Num(u, "contextTokens") ?? 0;
                if (t <= 0) t = (Num(u, "input") ?? 0) + (Num(u, "cacheRead") ?? 0) + (Num(u, "cacheWrite") ?? 0);
                if (t <= 0) continue;
                st.Tokens = t;
                st.Model = Str(msg, "model") ?? st.Model;
            }
            catch (JsonException) { }
        }
        return st;
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
