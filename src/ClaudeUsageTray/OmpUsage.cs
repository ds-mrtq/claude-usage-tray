using System.Runtime.InteropServices;

// Chỉ nạp DLL hệ thống từ System32, không tìm cạnh exe hay thư mục hiện hành (chặn DLL giả cài cạnh app)
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace ClaudeUsageTray;

/// <summary>
/// Hạn mức Claude do OMP ghi vào ~/.omp/agent/agent.db, bảng usage_history (mỗi lần OMP lấy hạn mức).
/// Đọc chỉ-đọc qua winsqlite3.dll có sẵn trong Windows 10+, không cần thư viện ngoài.
/// Không đọc cột email/account_id, không đụng bảng credential.
/// </summary>
public static class OmpUsage
{
    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".omp", "agent", "agent.db");

    public sealed record Result(DateTimeOffset RecordedAt, List<LimitInfo> Limits);

    // Lấy bản đo mới nhất của tài khoản anthropic được OMP ghi gần nhất, mỗi limit_id một dòng
    private const string Sql = """
        with acc as (select account_key from usage_history where provider = 'anthropic' order by recorded_at desc limit 1)
        select h.limit_id, h.label, h.used_fraction, h.resets_at, h.recorded_at
        from usage_history h join acc on acc.account_key = h.account_key
        where h.provider = 'anthropic'
          and h.recorded_at = (select max(x.recorded_at) from usage_history x
                               where x.account_key = h.account_key and x.limit_id = h.limit_id)
        """;

    public static Result? TryLoad(string path, DateTimeOffset now)
    {
        if (!File.Exists(path)) return null;
        IntPtr db = IntPtr.Zero, st = IntPtr.Zero;
        try
        {
            if (sqlite3_open_v2(path, out db, SQLITE_OPEN_READONLY, IntPtr.Zero) != 0) return null;
            sqlite3_busy_timeout(db, 500);
            if (sqlite3_prepare_v2(db, Sql, -1, out st, IntPtr.Zero) != 0) return null;
            var rows = new List<(LimitInfo l, long at)>();
            while (sqlite3_step(st) == SQLITE_ROW)
            {
                var id = Str(st, 0);
                var used = sqlite3_column_type(st, 2) == SQLITE_NULL ? (double?)null : sqlite3_column_double(st, 2);
                if (used == null) continue;
                long? resets = sqlite3_column_type(st, 3) == SQLITE_NULL ? null : sqlite3_column_int64(st, 3);
                var at = sqlite3_column_int64(st, 4);
                var (kind, label, window) = Map(id, Str(st, 1));
                var l = new LimitInfo { Kind = kind, Label = label, PercentUsed = Math.Round(used.Value * 100, 1), Source = "OMP" };
                if (resets != null)
                {
                    l.ResetsAt = DateTimeOffset.FromUnixTimeMilliseconds(resets.Value).ToLocalTime();
                    if (window != null)
                    {
                        var left = (l.ResetsAt.Value - now).TotalMilliseconds;
                        if (left > 0) l.ExpectedPercent = Math.Clamp((1 - left / window.Value.TotalMilliseconds) * 100, 0, 100);
                    }
                }
                rows.Add((l, at));
            }
            if (rows.Count == 0) return null;
            // Bỏ hạn mức đo đã lâu so với đợt mới nhất (vd extra usage OMP chỉ đo lâu lâu một lần)
            var newest = rows.Max(r => r.at);
            var limits = rows.Where(r => newest - r.at <= (long)TimeSpan.FromHours(6).TotalMilliseconds).Select(r => r.l).ToList();
            limits.Sort((a, b) => Order(a.Kind).CompareTo(Order(b.Kind)));
            return new Result(DateTimeOffset.FromUnixTimeMilliseconds(newest).ToLocalTime(), limits);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
        finally
        {
            if (st != IntPtr.Zero) sqlite3_finalize(st);
            if (db != IntPtr.Zero) sqlite3_close(db);
        }
    }

    private static (string kind, string label, TimeSpan? window) Map(string id, string label) => id switch
    {
        "anthropic:5h" => ("five_hour", "PHIÊN 5 GIỜ", TimeSpan.FromHours(5)),
        "anthropic:7d" => ("seven_day", "TUẦN", TimeSpan.FromDays(7)),
        "anthropic:7d:fable" => ("seven_day_fable", "TUẦN · FABLE", TimeSpan.FromDays(7)),
        "anthropic:extra" => ("extra", "EXTRA USAGE", null),
        _ => (id, label.ToUpperInvariant(), null),
    };

    public static int Order(string kind) => kind switch
    {
        "five_hour" => 0, "seven_day" => 1, "seven_day_fable" => 2, _ => 3,
    };

    private static string Str(IntPtr st, int i) => Marshal.PtrToStringUTF8(sqlite3_column_text(st, i)) ?? "";

    private const int SQLITE_OPEN_READONLY = 1, SQLITE_ROW = 100, SQLITE_NULL = 5;
    private const string Lib = "winsqlite3.dll";
    [DllImport(Lib, CharSet = CharSet.Ansi)] private static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPUTF8Str)] string f, out IntPtr db, int flags, IntPtr vfs);
    [DllImport(Lib)] private static extern int sqlite3_busy_timeout(IntPtr db, int ms);
    [DllImport(Lib)] private static extern int sqlite3_prepare_v2(IntPtr db, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, int n, out IntPtr st, IntPtr tail);
    [DllImport(Lib)] private static extern int sqlite3_step(IntPtr st);
    [DllImport(Lib)] private static extern int sqlite3_column_type(IntPtr st, int i);
    [DllImport(Lib)] private static extern double sqlite3_column_double(IntPtr st, int i);
    [DllImport(Lib)] private static extern long sqlite3_column_int64(IntPtr st, int i);
    [DllImport(Lib)] private static extern IntPtr sqlite3_column_text(IntPtr st, int i);
    [DllImport(Lib)] private static extern int sqlite3_finalize(IntPtr st);
    [DllImport(Lib)] private static extern int sqlite3_close(IntPtr db);
}
