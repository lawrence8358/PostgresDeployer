namespace PostgresDeployer.Wpf.Services;

using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

public class AppSettingsService
{
    private const int MaxRecentFiles = 10;

    private readonly string _settingsPath;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public AppSettingsService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "PostgresDeployer");
        Directory.CreateDirectory(dir);
        _settingsPath = Path.Combine(dir, "appsettings.json");
    }

    public class AppSettings
    {
        public List<RecentFile> RecentFiles { get; set; } = [];
        public string? LastConfigPath { get; set; }
        public double WindowWidth { get; set; } = 960;
        public double WindowHeight { get; set; } = 660;
        public double? WindowLeft { get; set; }
        public double? WindowTop { get; set; }
        public string WindowState { get; set; } = "Normal";
        /// <summary>Preferred UI language code, e.g. "en" or "zh-TW". Null = auto-detect from OS.</summary>
        public string? Language { get; set; }
    }

    public class RecentFile
    {
        public string Path { get; set; } = "";
        public DateTime LastUsed { get; set; }
    }

    public AppSettings Load()
    {
        if (!File.Exists(_settingsPath))
            return new AppSettings();

        try
        {
            var json = File.ReadAllText(_settingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions)
                ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(_settingsPath, json);
    }

    public void AddRecentFile(string configPath)
    {
        var settings = Load();
        var fullPath = Path.GetFullPath(configPath);

        settings.RecentFiles.RemoveAll(r =>
            string.Equals(r.Path, fullPath, StringComparison.OrdinalIgnoreCase));

        settings.RecentFiles.Insert(0, new RecentFile
        {
            Path = fullPath,
            LastUsed = DateTime.Now
        });

        if (settings.RecentFiles.Count > MaxRecentFiles)
            settings.RecentFiles = settings.RecentFiles.Take(MaxRecentFiles).ToList();

        settings.LastConfigPath = fullPath;
        Save(settings);
    }
}
