namespace ClaudeUsageTray;

/// <summary>Bảng màu Darcula, cùng giá trị với pane của mod usage-meter.</summary>
internal static class Theme
{
    public static readonly Color Bg = ColorTranslator.FromHtml("#2b2b2b");
    public static readonly Color Border = ColorTranslator.FromHtml("#3c3f41");
    public static readonly Color Track = ColorTranslator.FromHtml("#3c3f41");
    public static readonly Color Tick = ColorTranslator.FromHtml("#515658");
    public static readonly Color Text = ColorTranslator.FromHtml("#a9b7c6");
    public static readonly Color Dim = ColorTranslator.FromHtml("#808080");
    public static readonly Color Faint = ColorTranslator.FromHtml("#606366");
    public static readonly Color Green = ColorTranslator.FromHtml("#6a8759");
    public static readonly Color GreenDark = ColorTranslator.FromHtml("#4f6f3f");
    public static readonly Color Orange = ColorTranslator.FromHtml("#cc7832");
    public static readonly Color Red = ColorTranslator.FromHtml("#bc3f3c");
    public static readonly Color Blue = ColorTranslator.FromHtml("#6897bb");
    public static readonly Color BlueDark = ColorTranslator.FromHtml("#4a6d8c");
    public static readonly Color Purple = ColorTranslator.FromHtml("#9876aa");
    public static readonly Color Yellow = ColorTranslator.FromHtml("#ffc66d");

    /// <summary>Màu số %: vàng bình thường, cam từ 65%, đỏ từ 85%.</summary>
    public static Color Heat(double p) => p >= 85 ? Red : p >= 65 ? Orange : Yellow;

    /// <summary>Màu thanh: xanh lá bình thường, cam từ 65%, đỏ từ 85%.</summary>
    public static Color BarColor(double p) => p >= 85 ? Red : p >= 65 ? Orange : Green;

    public static Color Effort(string? e) => e switch
    {
        "low" => ColorTranslator.FromHtml("#515658"),
        "medium" => ColorTranslator.FromHtml("#3d5a73"),
        "high" => ColorTranslator.FromHtml("#5c4a6b"),
        "xhigh" => ColorTranslator.FromHtml("#7a5a38"),
        "max" => ColorTranslator.FromHtml("#7a3634"),
        _ => ColorTranslator.FromHtml("#515658"),
    };

    public static Font Mono(float size, FontStyle style = FontStyle.Regular)
    {
        foreach (var name in new[] { "JetBrains Mono", "Cascadia Mono", "Consolas" })
        {
            using var probe = new Font(name, size, style);
            if (probe.Name == name) return new Font(name, size, style);
        }
        return new Font(FontFamily.GenericMonospace, size, style);
    }
}
