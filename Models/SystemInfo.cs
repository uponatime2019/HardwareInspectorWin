using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;

namespace NewHwInspector.Models;

public enum SensorStatus { Unknown, Normal, Warning, Critical, Unavailable }

public enum SensorKind { Temperature, Load, Clock, Memory, Storage, Power, Fan, Voltage, Other }

public sealed class HistorySample
{
    public DateTime Time { get; set; }
    public double Value { get; set; }
}

public sealed class SensorEntry : INotifyPropertyChanged
{
    private double? _current;
    private double? _min;
    private double? _max;
    private double? _average;
    private SensorStatus _status;
    private bool _isAvailable = true;

    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = string.Empty;
    public string Group { get; init; } = string.Empty;
    private string _unit = string.Empty;
    public string Unit { get => _unit; init => _unit = value; }
    public void SetUnit(string u) { if (_unit != u) { _unit = u; OnPropertyChanged(nameof(Unit)); OnPropertyChanged(nameof(DisplayCurrent)); OnPropertyChanged(nameof(DisplayMin)); OnPropertyChanged(nameof(DisplayMax)); OnPropertyChanged(nameof(DisplayAverage)); } }
    public SensorKind Kind { get; init; } = SensorKind.Other;
    public string Description { get; init; } = string.Empty;

    public double? Current { get => _current; set { if (_current != value) { _current = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayCurrent)); } } }
    public double? Min { get => _min; set { if (_min != value) { _min = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayMin)); } } }
    public double? Max { get => _max; set { if (_max != value) { _max = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayMax)); } } }
    public double? Average { get => _average; set { if (_average != value) { _average = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayAverage)); } } }
    public SensorStatus Status { get => _status; set { if (_status != value) { _status = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusGlyph)); OnPropertyChanged(nameof(StatusColor)); } } }
    public bool IsAvailable { get => _isAvailable; set { if (_isAvailable != value) { _isAvailable = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayCurrent)); } } }
    public DateTime LastUpdate { get; set; } = DateTime.Now;
    public string? Error { get; set; }

    public List<HistorySample> History { get; } = new();
    public int SampleCount { get; set; }

    public string DisplayCurrent => !IsAvailable ? "n/a" : Current.HasValue ? Format(Current.Value) : "—";
    public string DisplayMin => !IsAvailable ? "n/a" : Min.HasValue ? Format(Min.Value) : "—";
    public string DisplayMax => !IsAvailable ? "n/a" : Max.HasValue ? Format(Max.Value) : "—";
    public string DisplayAverage => !IsAvailable ? "n/a" : Average.HasValue ? Format(Average.Value) : "—";

    private string Format(double v)
    {
        if (Unit == "°C" || Unit == "°F") return $"{v:0.0} {Unit}";
        if (Unit == "%") return $"{v:0.0} {Unit}";
        if (Unit == "MHz") return $"{v:N0} {Unit}";
        if (Unit == "GB") return $"{v:0.00} {Unit}";
        if (Unit == "MB/s") return $"{v:0.0} {Unit}";
        if (Unit == "RPM") return $"{v:N0} {Unit}";
        if (Unit == "V") return $"{v:0.000} {Unit}";
        if (Unit == "W") return $"{v:0.0} {Unit}";
        return $"{v:0.##} {Unit}".Trim();
    }

    public string StatusGlyph => Status switch { SensorStatus.Normal => "\uE73E", SensorStatus.Warning => "\uE7BA", SensorStatus.Critical => "\uEA39", SensorStatus.Unavailable => "\uE711", _ => "" };
    public string StatusColor => Status switch { SensorStatus.Normal => "#4CC38A", SensorStatus.Warning => "#E8B33C", SensorStatus.Critical => "#E8574E", SensorStatus.Unavailable => "#6B7280", _ => "#9AA0A6" };

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public sealed class HardwareProperty : INotifyPropertyChanged
{
    private string _value = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Value { get => _value; set { if (_value != value) { _value = value; OnPropertyChanged(); } } }
    public string? Note { get; init; }
    public SensorStatus Status { get; init; } = SensorStatus.Unknown;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public sealed class HardwareNode : INotifyPropertyChanged
{
    public string Name { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string Icon { get; init; } = "\uE770";
    public ObservableCollection<HardwareProperty> Properties { get; } = new();
    public ObservableCollection<HardwareNode> Children { get; } = new();
    private bool _isExpanded = true;
    public bool IsExpanded { get => _isExpanded; set { if (_isExpanded != value) { _isExpanded = value; OnPropertyChanged(); } } }
    public string Summary { get; set; } = string.Empty;
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public sealed class OverviewData
{
    public string CpuName { get; set; } = "—";
    public string CpuCores { get; set; } = "—";
    public string CpuBaseClock { get; set; } = "—";
    public string CpuSocket { get; set; } = "—";
    public double? CpuTempC { get; set; }
    public string CpuTempText => CpuTempC.HasValue ? $"{CpuTempC:0} °C" : "n/a";

    public string BoardVendor { get; set; } = "—";
    public string BoardModel { get; set; } = "—";
    public string BiosVersion { get; set; } = "—";
    public string BiosDate { get; set; } = "—";

    public ulong TotalRamBytes { get; set; }
    public string TotalRamText => TotalRamBytes > 0 ? $"{TotalRamBytes / 1024.0 / 1024 / 1024:0.0} GB" : "—";
    public string RamType { get; set; } = "—";
    public string RamSpeed { get; set; } = "—";
    public int MemoryLoad { get; set; }
    public string MemoryUsedText { get; set; } = "—";

    public string GpuName { get; set; } = "—";
    public string GpuMemory { get; set; } = "—";
    public string GpuDriver { get; set; } = "—";
    public string GpuTempText { get; set; } = "n/a";

    public int DriveCount { get; set; }
    public string StorageSummary { get; set; } = "—";
    public string SystemDriveFree { get; set; } = "—";

    public string OsName { get; set; } = "—";
    public string OsVersion { get; set; } = "—";
    public string OsBuild { get; set; } = "—";
    public string OsArch { get; set; } = "—";

    public string FirmwareMode { get; set; } = "—";
    public string SecureBoot { get; set; } = "—";
    public string TpmText { get; set; } = "—";
    public string Virtualization { get; set; } = "—";

    public List<string> Warnings { get; } = new();
    public DateTime CollectedAt { get; set; } = DateTime.Now;
}

public sealed class AlertRule : INotifyPropertyChanged
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [JsonProperty("sensorId")] public string SensorId { get; set; } = string.Empty;
    [JsonProperty("sensorName")] public string SensorName { get; set; } = string.Empty;
    [JsonProperty("group")] public string Group { get; set; } = string.Empty;
    [JsonProperty("enabled")] public bool Enabled { get; set; } = true;
    [JsonProperty("condition")] public string Condition { get; set; } = "Above"; // Above, Below
    [JsonProperty("threshold")] public double Threshold { get; set; }
    [JsonProperty("unit")] public string Unit { get; set; } = string.Empty;
    private bool _isActive;
    [JsonIgnore] public bool IsActive { get => _isActive; set { if (_isActive != value) { _isActive = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusText)); } } }
    [JsonIgnore] public string StatusText => IsActive ? "ALERT" : "OK";
    [JsonIgnore] public DateTime? LastTriggered { get; set; }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public sealed class ReportOptions
{
    public bool IncludeOverview { get; set; } = true;
    public bool IncludeHardware { get; set; } = true;
    public bool IncludeSensors { get; set; } = true;
    public bool IncludeAlerts { get; set; } = true;
    public string Format { get; set; } = "TXT"; // TXT, HTML, JSON, CSV
}

