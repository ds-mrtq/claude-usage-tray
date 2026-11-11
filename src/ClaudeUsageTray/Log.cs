namespace ClaudeUsageTray;

/// <summary>Nhật ký chẩn đoán, chỉ ghi khi đặt biến môi trường CLAUDE_USAGE_TRAY_LOG=1.</summary>
internal static class Log
{
    private static readonly bool On = Environment.GetEnvironmentVariable("CLAUDE_USAGE_TRAY_LOG") == "1";
    private static readonly string File = Path.Combine(Path.GetTempPath(), "ClaudeUsageTray.log");

    public static void Write(string msg)
    {
        if (!On) return;
        try { System.IO.File.AppendAllText(File, $"{DateTime.Now:HH:mm:ss.fff} {msg}{Environment.NewLine}"); } catch { }
    }
}
