using System.IO;
using System.Text.Json;

namespace FpsMonitor.Models;

public enum OverlayCorner
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight
}

public sealed class AppSettings
{
    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FpsMonitor");

    private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

    public string PresentMonPath { get; set; } = FindDefaultPresentMon();
    public OverlayCorner OverlayCorner { get; set; } = OverlayCorner.TopLeft;
    public double OverlayOpacity { get; set; } = 0.88;
    public bool OverlayCompact { get; set; } = false;
    public bool OverlayShowOnePercentLow { get; set; } = true;
    public bool OverlayShowGpuTemp { get; set; } = true;
    public bool OverlayShowVram { get; set; } = false;

    public static string FindDefaultPresentMon()
    {
        var baseDir = AppContext.BaseDirectory;
        string[] candidates =
        [
            Path.Combine(baseDir, "PresentMon.exe"),
            @"C:\Program Files\Intel\PresentMon\PresentMonConsoleApplication\PresentMon-2.6.0-x64.exe"
        ];

        foreach (var path in candidates)
        {
            if (File.Exists(path))
                return path;
        }

        return candidates[1];
    }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                if (loaded is not null)
                {
                    if (string.IsNullOrWhiteSpace(loaded.PresentMonPath) || !File.Exists(loaded.PresentMonPath))
                        loaded.PresentMonPath = FindDefaultPresentMon();
                    return loaded;
                }
            }
        }
        catch
        {
            // Используем настройки по умолчанию при повреждении файла.
        }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // Игнорируем ошибки записи настроек.
        }
    }
}
