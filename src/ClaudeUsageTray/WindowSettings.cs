using System.Text.Json;

namespace ClaudeUsageTray;

/// <summary>Vị trí, độ phóng và trạng thái phóng to của bảng, lưu ở %LOCALAPPDATA%\ClaudeUsageTray\window.json.</summary>
internal sealed class WindowSettings
{
    public int? X { get; set; }
    public int? Y { get; set; }
    public float Zoom { get; set; } = 1f;
    public bool Maximized { get; set; }
    // Vị trí và độ phóng để trả lại khi thoát chế độ phóng to tối đa
    public int? RestoreX { get; set; }
    public int? RestoreY { get; set; }
    public float RestoreZoom { get; set; } = 1f;

    private static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClaudeUsageTray", "window.json");

    public static WindowSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<WindowSettings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        return new();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log.Write("Save settings failed: " + ex.Message); }
    }
}
