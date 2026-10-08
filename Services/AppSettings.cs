using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace NewHwInspector.Services;

public sealed class NewHwInspectorSettings
{
    [JsonProperty("refreshIntervalSeconds")] public int RefreshIntervalSeconds { get; set; } = 2;
    [JsonProperty("theme")] public string Theme { get; set; } = "Dark"; // Dark, Light, System
    [JsonProperty("tempUnit")] public string TempUnit { get; set; } = "Celsius"; // Celsius, Fahrenheit
    [JsonProperty("groupSensors")] public bool GroupSensors { get; set; } = true;
    [JsonProperty("reportFolder")] public string ReportFolder { get; set; } = string.Empty;
    [JsonProperty("startMinimized")] public bool StartMinimized { get; set; } = false;
    [JsonProperty("alertRules")] public List<Models.AlertRule> AlertRules { get; set; } = new();
    [JsonProperty("chartTimeRangeMinutes")] public int ChartTimeRangeMinutes { get; set; } = 5;
    [JsonProperty("autoFitCharts")] public bool AutoFitCharts { get; set; } = true;
    [JsonProperty("isFirstTimeRun")] public bool IsFirstTimeRun { get; set; } = true;
    [JsonProperty("runCount")] public int RunCount { get; set; } = 0;
}

public static class AppSettings
{
    private static readonly object Gate = new();
    private static NewHwInspectorSettings? _current;

    public static string SettingsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NewHwInspector");

    public static string SettingsFilePath =>
        Path.Combine(SettingsDirectory, "settings.json");

    public static NewHwInspectorSettings Current => _current ??= new NewHwInspectorSettings();

    public static async Task LoadAsync()
    {
        await Task.Run(() =>
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    string json = File.ReadAllText(SettingsFilePath);
                    var loaded = JsonConvert.DeserializeObject<NewHwInspectorSettings>(json);
                    if (loaded != null)
                    {
                        _current = loaded;
                        _current.RunCount++;
                        _current.IsFirstTimeRun = false;
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "LoadSettings");
            }

            if (_current == null)
            {
                _current = new NewHwInspectorSettings { RunCount = 1, IsFirstTimeRun = true };
            }

            if (_current.RefreshIntervalSeconds < 1) _current.RefreshIntervalSeconds = 1;
            if (_current.RefreshIntervalSeconds > 60) _current.RefreshIntervalSeconds = 60;
        });
    }

    public static async Task SaveAsync()
    {
        await Task.Run(() =>
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(SettingsDirectory);
                    string json = JsonConvert.SerializeObject(Current, Formatting.Indented);
                    File.WriteAllText(SettingsFilePath, json);
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "SaveSettings");
            }
        });
    }
}
