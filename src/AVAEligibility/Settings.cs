using System.Text.Json;
using AVAEligibility.Core.Inquiries;

namespace AVAEligibility;

/// <summary>What the program remembers between runs: who's asking (the inquiry settings). Kept in the settings folder.</summary>
public sealed class AppSettings
{
    public InquirySettings Inquiry { get; set; } = new();

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string Path => System.IO.Path.Combine(MainWindow.SettingsFolder, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            return File.Exists(Path) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path), Options) ?? new() : new();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return new();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(MainWindow.SettingsFolder);
        File.WriteAllText(Path, JsonSerializer.Serialize(this, Options));
    }
}

public sealed partial class MainWindow
{
    /// <summary>Tests point the settings folder at a temporary one.</summary>
    public static string? FolderOverride { get; set; }

    /// <summary>Where the program keeps its settings: the user's application data folder, AVAEligibility.</summary>
    public static string SettingsFolder => FolderOverride ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AVAEligibility");
}
