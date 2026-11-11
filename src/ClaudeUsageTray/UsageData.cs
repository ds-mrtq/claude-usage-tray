using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClaudeUsageTray;

/// <summary>Khớp schema 1 của file ~/.claude/usage-meter.json do mod usage-meter ghi.</summary>
public sealed class UsageData
{
    [JsonPropertyName("schema")] public int Schema { get; set; }
    [JsonPropertyName("writtenAt")] public DateTimeOffset WrittenAt { get; set; }
    [JsonPropertyName("measuredAt")] public DateTimeOffset? MeasuredAt { get; set; }
    [JsonPropertyName("limits")] public List<LimitInfo> Limits { get; set; } = [];
    [JsonPropertyName("context")] public ContextInfo? Context { get; set; }
    [JsonPropertyName("cost")] public CostInfo? Cost { get; set; }
    [JsonPropertyName("models")] public List<ModelInfo> Models { get; set; } = [];
    [JsonPropertyName("mainModel")] public string? MainModel { get; set; }
    [JsonPropertyName("mainEffort")] public string? MainEffort { get; set; }

    public LimitInfo? FiveHour => Limits.FirstOrDefault(l => l.Kind == "five_hour");

    /// <summary>Nguồn đã ghép: "CC" (mod Claude Code), "OMP", hoặc cả hai.</summary>
    [JsonIgnore] public string Sources { get; set; } = "CC";

    /// <summary>
    /// Ghép hai nguồn: file của mod Claude Code và usage_history của OMP.
    /// Mỗi hạn mức lấy bản đo mới hơn; Tuần · Fable chỉ OMP có.
    /// Context, chi phí, model chỉ có từ phiên Claude Code.
    /// </summary>
    public static UsageData? Load(string jsonPath, string? ompPath, DateTimeOffset now)
    {
        var cc = TryLoad(jsonPath);
        var omp = ompPath == null ? null : OmpUsage.TryLoad(ompPath, now);
        if (omp == null) return cc;
        if (cc == null)
            return new UsageData { Schema = 1, WrittenAt = omp.RecordedAt, MeasuredAt = omp.RecordedAt, Limits = omp.Limits, Sources = "OMP" };

        var ccAt = cc.MeasuredAt ?? cc.WrittenAt;
        var usedOmp = false;
        foreach (var o in omp.Limits)
        {
            var i = cc.Limits.FindIndex(l => l.Kind == o.Kind);
            if (i < 0) { cc.Limits.Add(o); usedOmp = true; }
            else if (omp.RecordedAt > ccAt) { cc.Limits[i] = o; usedOmp = true; }
        }
        cc.Limits.Sort((a, b) => OmpUsage.Order(a.Kind).CompareTo(OmpUsage.Order(b.Kind)));
        if (usedOmp) cc.Sources = "CC+OMP";
        if (omp.RecordedAt > cc.WrittenAt) cc.WrittenAt = omp.RecordedAt;
        return cc;
    }

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "usage-meter.json");

    /// <summary>Đọc file; mod có thể đang ghi dở nên thử lại vài lần, lỗi thì trả null.</summary>
    public static UsageData? TryLoad(string path)
    {
        for (var i = 0; i < 3; i++)
        {
            try
            {
                if (!File.Exists(path)) return null;
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                return JsonSerializer.Deserialize<UsageData>(fs);
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                Thread.Sleep(150);
            }
        }
        return null;
    }
}

public sealed class LimitInfo
{
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("percentUsed")] public double PercentUsed { get; set; }
    [JsonPropertyName("resetsAt")] public DateTimeOffset? ResetsAt { get; set; }
    [JsonPropertyName("expectedPercent")] public double? ExpectedPercent { get; set; }
    [JsonPropertyName("ratePerUnit")] public double? RatePerUnit { get; set; }
    [JsonPropertyName("unit")] public string? Unit { get; set; }
    [JsonPropertyName("fullInMs")] public double? FullInMs { get; set; }
    [JsonIgnore] public string Source { get; set; } = "CC";
}

public sealed class ContextInfo
{
    [JsonPropertyName("tokens")] public double? Tokens { get; set; }
    [JsonPropertyName("window")] public double Window { get; set; }
    [JsonPropertyName("percent")] public double? Percent { get; set; }
    [JsonPropertyName("autoCompactAt")] public double? AutoCompactAt { get; set; }
}

public sealed class CostInfo
{
    [JsonPropertyName("session")] public double Session { get; set; }
    [JsonPropertyName("perHour")] public double PerHour { get; set; }
    [JsonPropertyName("today")] public double Today { get; set; }
    [JsonPropertyName("week")] public double Week { get; set; }
}

public sealed class ModelInfo
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("effort")] public string? Effort { get; set; }
    [JsonPropertyName("usd")] public double Usd { get; set; }
    [JsonPropertyName("share")] public double Share { get; set; }
}
