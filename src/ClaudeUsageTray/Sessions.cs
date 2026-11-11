namespace ClaudeUsageTray;

/// <summary>Một phiên đang chạy (Claude Code hoặc OMP) để liệt kê và chọn xem riêng.</summary>
public sealed class SessionEntry
{
    public required string Key { get; init; }          // "cc:&lt;sessionId&gt;" | "omp:&lt;đường dẫn file&gt;"
    public required string Kind { get; init; }         // "CC" | "OMP"
    public required string Name { get; init; }
    public string? Model { get; init; }
    public string? Effort { get; init; }
    public double? Tokens { get; init; }
    public double? Window { get; init; }
    public double? AutoCompactAt { get; init; }
    public double? CostUsd { get; init; }
    public DateTimeOffset At { get; init; }
    /// <summary>Phiên CC chưa có file riêng (mod chưa nạp lại): lấy từ file chung.</summary>
    public bool Legacy { get; init; }
    public UsageData? Cc { get; init; }
    public OmpContextInfo? Omp { get; init; }
    public double? Percent => Tokens != null && Window is > 0 ? Math.Round(Tokens.Value / Window.Value * 100) : null;
}

public static class SessionList
{
    /// <summary>Phiên CC còn sống: chưa có endedAt và có nhịp (mod ghi mỗi phút) trong khoảng này.</summary>
    public static readonly TimeSpan CcAlive = TimeSpan.FromMinutes(3);
    /// <summary>Phiên OMP coi là đang chạy khi file phiên được ghi trong khoảng này (OMP không ghi dấu kết thúc).</summary>
    public static readonly TimeSpan OmpRecent = TimeSpan.FromHours(2);
    /// <summary>File chung của mod cũ (chưa ghi file theo phiên) coi là còn chạy nếu ghi trong khoảng này.</summary>
    public static readonly TimeSpan LegacyRecent = TimeSpan.FromMinutes(30);

    public static string Dir(string mainJson) => Path.Combine(Path.GetDirectoryName(mainJson)!, "usage-meter", "sessions");

    public static List<SessionEntry> List(string mainJson, UsageData? main, string? ompAgentDir, DateTimeOffset now)
    {
        var list = new List<SessionEntry>();
        var known = new HashSet<string>(); // phiên đã có file riêng (còn chạy, đã kết thúc hay mất nhịp)
        var dir = Dir(mainJson);
        if (Directory.Exists(dir))
        {
            foreach (var f in SafeFiles(dir))
            {
                if (now - new DateTimeOffset(File.GetLastWriteTimeUtc(f)) > TimeSpan.FromHours(12)) continue;
                var u = UsageData.TryLoad(f);
                if (u?.SessionId != null) known.Add(u.SessionId);
                if (u == null || u.EndedAt != null) continue;
                var beat = u.HeartbeatAt ?? u.WrittenAt;
                if (now - beat > CcAlive) continue;
                list.Add(FromCc(u, beat, legacy: false));
            }
        }
        // Phiên đang chạy mod cũ chỉ ghi file chung: vẫn hiện, ghi chú "chưa nạp lại mod"
        if (main?.SessionId is { } sid && !known.Contains(sid) && now - main.WrittenAt < LegacyRecent)
            list.Add(FromCc(main, main.WrittenAt, legacy: true));

        if (ompAgentDir != null)
            foreach (var o in OmpContextReader.Active(ompAgentDir, OmpRecent))
                list.Add(new SessionEntry
                {
                    Key = "omp:" + o.Path, Kind = "OMP", Name = o.Title is { Length: > 0 } t ? t : "phiên OMP",
                    Model = o.Model, Tokens = o.Tokens, Window = o.Window, AutoCompactAt = o.AutoCompactAt,
                    CostUsd = o.CostUsd, At = o.At, Omp = o,
                });
        return list.OrderByDescending(e => e.At).ToList();
    }

    private static SessionEntry FromCc(UsageData u, DateTimeOffset at, bool legacy) => new()
    {
        Key = "cc:" + u.SessionId, Kind = "CC",
        Name = u.Cwd is { Length: > 0 } c ? Path.GetFileName(c.TrimEnd('\\', '/')) : (u.SessionId?[..Math.Min(8, u.SessionId.Length)] ?? "phiên Claude Code"),
        Model = u.MainModel, Effort = u.MainEffort,
        Tokens = u.Context?.Tokens, Window = u.Context?.Window, AutoCompactAt = u.Context?.AutoCompactAt,
        CostUsd = u.Cost?.Session, At = at, Legacy = legacy, Cc = u,
    };

    private static IEnumerable<string> SafeFiles(string dir)
    {
        try { return Directory.EnumerateFiles(dir, "*.json").ToList(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }
}
