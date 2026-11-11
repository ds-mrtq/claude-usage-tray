using System.Globalization;

namespace ClaudeUsageTray;

internal static class Fmt
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Usd(double v) =>
        v >= 100 ? "$" + v.ToString("0", Inv) : v >= 1 ? "$" + v.ToString("0.00", Inv) : "$" + v.ToString("0.000", Inv);

    public static string Tok(double n) =>
        n >= 1e6 ? (n / 1e6).ToString("0.0", Inv) + "M" : n >= 1e3 ? Math.Round(n / 1e3).ToString(Inv) + "k" : n.ToString("0", Inv);

    public static string Dur(TimeSpan t)
    {
        if (t <= TimeSpan.Zero) return "0m";
        var m = (int)Math.Round(t.TotalMinutes);
        int d = m / 1440, h = m % 1440 / 60, mm = m % 60;
        return d > 0 ? $"{d}d {h}h" : h > 0 ? $"{h}h {mm}m" : $"{mm}m";
    }

    public static string Ago(TimeSpan t) => t.TotalSeconds < 60 ? $"{Math.Max(1, (int)t.TotalSeconds)}s" : Dur(t);
}
