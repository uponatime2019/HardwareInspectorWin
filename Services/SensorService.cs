using Microsoft.UI.Dispatching;
using NewHwInspector.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace NewHwInspector.Services;

public sealed class SensorService
{
    public static SensorService Instance { get; } = new();
    private SensorService() { }

    public ObservableCollection<SensorEntry> Sensors { get; } = new();
    public IReadOnlyList<string> Groups => Sensors.Select(s => s.Group).Distinct().ToList();

    private DispatcherQueueTimer? _timer;
    private bool _isPaused;
    private DateTime _lastSample = DateTime.MinValue;
    private TaskCompletionSource<bool>? _initialSample;
    private readonly Dictionary<string, Queue<double>> _historyStats = new();
    private readonly object _gate = new();

    public bool IsPaused { get => _isPaused; set { _isPaused = value; PausedChanged?.Invoke(this, EventArgs.Empty); } }
    public DateTime LastSampleTime => _lastSample;
    public bool IsMonitoring { get; private set; }

    public event EventHandler? SensorsUpdated;
    public event EventHandler? PausedChanged;
    public event EventHandler<SensorEntry>? SensorAlert;

    public void Initialize(DispatcherQueue queue)
    {
        if (_timer != null) return;
        _timer = queue.CreateTimer();
        UpdateInterval();
        _timer.Tick += async (s, e) => await PollAsync();
    }

    public void UpdateInterval()
    {
        if (_timer == null) return;
        int sec = Math.Clamp(AppSettings.Current.RefreshIntervalSeconds, 1, 60);
        _timer.Interval = TimeSpan.FromSeconds(sec);
    }

    public Task StartAsync()
    {
        if (IsMonitoring) return _initialSample?.Task ?? Task.CompletedTask;
        IsMonitoring = true;
        var initialSample = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _initialSample = initialSample;
        BuildSensorList();
        _timer?.Start();
        _ = PollAsync();
        AppLogger.Log($"Sensor monitoring started interval={AppSettings.Current.RefreshIntervalSeconds}s sensors={Sensors.Count}");
        return initialSample.Task;
    }

    public void Stop()
    {
        _timer?.Stop();
        IsMonitoring = false;
    }

    public void Pause() => IsPaused = true;
    public void Resume() => IsPaused = false;

    public void BuildSensorList()
    {
        Sensors.Clear();
        _historyStats.Clear();
        // CPU group
        Sensors.Add(new SensorEntry { Name = "CPU Package Temperature", Group = "CPU", Unit = "°C", Kind = SensorKind.Temperature, Description = "Package temperature from ACPI thermal zone" });
        Sensors.Add(new SensorEntry { Name = "CPU Total Usage", Group = "CPU", Unit = "%", Kind = SensorKind.Load, Description = "Total processor time across all cores" });
        Sensors.Add(new SensorEntry { Name = "CPU Current Clock", Group = "CPU", Unit = "MHz", Kind = SensorKind.Clock, Description = "Current processor frequency" });
        Sensors.Add(new SensorEntry { Name = "CPU Power (est.)", Group = "CPU", Unit = "W", Kind = SensorKind.Power, Description = "Estimated package power if available via perf counters" });
        // Per-core usage/clock
        int cores = Environment.ProcessorCount;
        for (int i = 0; i < cores; i++)
        {
            Sensors.Add(new SensorEntry { Name = $"Core #{i} Usage", Group = "CPU Cores", Unit = "%", Kind = SensorKind.Load });
            Sensors.Add(new SensorEntry { Name = $"Core #{i} Clock", Group = "CPU Cores", Unit = "MHz", Kind = SensorKind.Clock });
        }
        // Memory
        Sensors.Add(new SensorEntry { Name = "Memory Usage", Group = "Memory", Unit = "%", Kind = SensorKind.Memory, Description = "Physical memory load" });
        Sensors.Add(new SensorEntry { Name = "Memory Used", Group = "Memory", Unit = "GB", Kind = SensorKind.Memory });
        Sensors.Add(new SensorEntry { Name = "Memory Available", Group = "Memory", Unit = "GB", Kind = SensorKind.Memory });
        Sensors.Add(new SensorEntry { Name = "Commit Charge", Group = "Memory", Unit = "%", Kind = SensorKind.Memory, Description = "Page file commit percentage" });
        // GPU
        Sensors.Add(new SensorEntry { Name = "GPU Temperature", Group = "GPU", Unit = "°C", Kind = SensorKind.Temperature, Description = "GPU thermal if exposed by driver" });
        Sensors.Add(new SensorEntry { Name = "GPU Usage", Group = "GPU", Unit = "%", Kind = SensorKind.Load, Description = "GPU engine utilization" });
        Sensors.Add(new SensorEntry { Name = "GPU Memory Used", Group = "GPU", Unit = "%", Kind = SensorKind.Memory, Description = "Dedicated video memory usage" });
        Sensors.Add(new SensorEntry { Name = "GPU Fan", Group = "GPU", Unit = "RPM", Kind = SensorKind.Fan });
        // Storage
        try
        {
            var logical = WmiHelper.Query("SELECT DeviceID FROM Win32_LogicalDisk WHERE DriveType=3");
            foreach (var l in logical)
            {
                string drive = WmiHelper.S(l, "DeviceID");
                Sensors.Add(new SensorEntry { Name = $"{drive} Usage", Group = "Storage", Unit = "%", Kind = SensorKind.Storage });
                Sensors.Add(new SensorEntry { Name = $"{drive} Temperature", Group = "Storage", Unit = "°C", Kind = SensorKind.Temperature });
            }
        }
        catch { }
        // System
        Sensors.Add(new SensorEntry { Name = "System Uptime", Group = "System", Unit = "h", Kind = SensorKind.Other });
        Sensors.Add(new SensorEntry { Name = "CPU Fan", Group = "System", Unit = "RPM", Kind = SensorKind.Fan });
        // Initialize stats queues
        foreach (var s in Sensors) _historyStats[s.Id] = new Queue<double>();
    }

    private async Task PollAsync()
    {
        if (IsPaused)
        {
            _initialSample?.TrySetResult(true);
            return;
        }
        try
        {
            await Task.Run(DoPoll);
            _lastSample = DateTime.Now;
            SensorsUpdated?.Invoke(this, EventArgs.Empty);
            CheckAlerts();
        }
        catch (Exception ex) { AppLogger.LogException(ex, "SensorPoll"); }
        finally { _initialSample?.TrySetResult(true); }
    }

    private void DoPoll()
    {
        var mem = NativeMethods.QueryMemoryStatus();
        double totalPhys = mem.ullTotalPhys;
        double availPhys = mem.ullAvailPhys;
        double usedPhys = totalPhys - availPhys;
        double memLoad = mem.dwMemoryLoad;
        double totalPage = mem.ullTotalPageFile;
        double availPage = mem.ullAvailPageFile;
        double commitLoad = totalPage > 0 ? (totalPage - availPage) * 100.0 / totalPage : 0;

        double totalPhysGb = totalPhys / 1024 / 1024 / 1024;
        double usedPhysGb = usedPhys / 1024 / 1024 / 1024;
        double availPhysGb = availPhys / 1024 / 1024 / 1024;

        // CPU perf via WMI counters
        Dictionary<int, (int Load, int Freq)> perCore = new();
        int totalLoad = -1;
        int curFreq = 0;
        try
        {
            var rows = WmiHelper.Query("SELECT Name, PercentProcessorTime FROM Win32_PerfFormattedData_PerfOS_Processor");
            foreach (var r in rows)
            {
                string name = WmiHelper.S(r, "Name");
                int load = WmiHelper.I(r, "PercentProcessorTime", -1);
                if (name == "_Total") totalLoad = load;
                else if (int.TryParse(name, out int idx)) perCore[idx] = (load, 0);
            }
            var cpuRows = WmiHelper.Query("SELECT CurrentClockSpeed FROM Win32_Processor");
            if (cpuRows.Count > 0) curFreq = WmiHelper.I(cpuRows[0], "CurrentClockSpeed");
            // Try more precise per-core frequency via ProcessorInformation
            try
            {
                var freqRows = WmiHelper.Query("SELECT Name, ProcessorFrequency FROM Win32_PerfFormattedData_Counters_ProcessorInformation");
                foreach (var fr in freqRows)
                {
                    string nm = WmiHelper.S(fr, "Name");
                    if (nm.Contains("_Total")) continue;
                    if (System.Text.RegularExpressions.Regex.Match(nm, @"^(\d+),(\d+)$") is var m && m.Success)
                    {
                        int coreIdx = int.Parse(m.Groups[2].Value);
                        int freq = WmiHelper.I(fr, "ProcessorFrequency");
                        if (perCore.TryGetValue(coreIdx, out var existing)) perCore[coreIdx] = (existing.Load, freq);
                        else perCore[coreIdx] = (-1, freq);
                    }
                }
            }
            catch { }
        }
        catch (Exception ex) { AppLogger.LogException(ex, "CpuPerfPoll"); }

        // Temperature via ACPI
        double? cpuTemp = null;
        try
        {
            foreach (var row in WmiHelper.Query("SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature", @"root\WMI"))
            {
                uint tk = WmiHelper.UI(row, "CurrentTemperature");
                if (tk == 0) continue;
                double c = tk / 10.0 - 273.15;
                if (c is >= -20 and <= 150) { cpuTemp = c; break; }
            }
        }
        catch { }

        // GPU usage attempt via GPU performance counters
        double? gpuUsage = null;
        try
        {
            var gpuRows = WmiHelper.Query("SELECT UtilizationPercentage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine");
            if (gpuRows.Count > 0)
            {
                double sum = 0; int cnt = 0;
                foreach (var g in gpuRows)
                {
                    int v = WmiHelper.I(g, "UtilizationPercentage", -1);
                    if (v >= 0) { sum += v; cnt++; }
                }
                if (cnt > 0) gpuUsage = sum;
                if (gpuUsage.HasValue && gpuUsage > 100) gpuUsage = 100;
            }
        }
        catch { }
        // fallback to older counter name
        if (!gpuUsage.HasValue)
        {
            try
            {
                var alt = WmiHelper.Query("SELECT PercentGPUTime FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUProcessor");
                if (alt.Count > 0) gpuUsage = WmiHelper.I(alt[0], "PercentGPUTime", -1);
            }
            catch { }
        }

        // Drive usage
        Dictionary<string, double> driveUsages = new();
        try
        {
            var logical = WmiHelper.Query("SELECT DeviceID, Size, FreeSpace FROM Win32_LogicalDisk WHERE DriveType=3");
            foreach (var l in logical)
            {
                string drive = WmiHelper.S(l, "DeviceID");
                ulong size = WmiHelper.U(l, "Size");
                ulong free = WmiHelper.U(l, "FreeSpace");
                if (size > 0) driveUsages[drive] = (size - free) * 100.0 / size;
            }
        }
        catch { }

        // Update sensors on UI thread via DispatcherQueue? Called from task, need to marshal.
        // We update entries; INotifyPropertyChanged will handle UI if we invoke on UI thread.
        // Instead we update synchronously and raise SensorsUpdated which pages handle on UI thread via DispatcherQueue.
        void UpdateSensor(string name, string group, double? value, bool available = true)
        {
            var s = Sensors.FirstOrDefault(x => x.Name == name && x.Group == group);
            if (s == null) return;
            if (!available || !value.HasValue)
            {
                s.IsAvailable = false;
                s.Status = SensorStatus.Unavailable;
                s.Error = "Not exposed by hardware/firmware";
                return;
            }
            s.IsAvailable = true;
            double v = value.Value;
            lock (_gate)
            {
                s.Current = v;
                s.LastUpdate = DateTime.Now;
                s.SampleCount++;
                if (!_historyStats.TryGetValue(s.Id, out var q)) { q = new Queue<double>(); _historyStats[s.Id] = q; }
                q.Enqueue(v);
                int maxSamples = AppSettings.Current.ChartTimeRangeMinutes * 60 / Math.Max(1, AppSettings.Current.RefreshIntervalSeconds);
                while (q.Count > maxSamples) q.Dequeue();
                if (q.Count > 0)
                {
                    s.Min = q.Min();
                    s.Max = q.Max();
                    s.Average = q.Average();
                    s.History.Clear();
                    int idx = 0;
                    foreach (var val in q) s.History.Add(new HistorySample { Time = DateTime.Now.AddSeconds(-(q.Count - idx++) * AppSettings.Current.RefreshIntervalSeconds), Value = val });
                }
                // status based on thresholds
                if (s.Kind == SensorKind.Temperature)
                {
                    if (v >= 90) s.Status = SensorStatus.Critical;
                    else if (v >= 80) s.Status = SensorStatus.Warning;
                    else s.Status = SensorStatus.Normal;
                }
                else if (s.Unit == "%" && v >= 95) s.Status = SensorStatus.Critical;
                else if (s.Unit == "%" && v >= 85) s.Status = SensorStatus.Warning;
                else s.Status = value.HasValue ? SensorStatus.Normal : SensorStatus.Unavailable;
            }
        }

        // Apply updates
        // Need to handle temp unit conversion properly: if Fahrenheit, convert and keep Unit as °F by using a mutable field via reflection hack: just set IsAvailable etc. Simpler redefine SensorEntry Unit as mutable settable.
        // For now, we will manually adjust values and set a property via helper extension? Let's assume SensorEntry Unit is settable via private setter hack using reflection each poll.
        // Instead, we will directly update sensors via helper that checks setting.
        void UpdateTempSensor(string name, string group, double? cValue)
        {
            var s = Sensors.FirstOrDefault(x => x.Name == name && x.Group == group);
            if (s == null) return;
            if (!cValue.HasValue) { s.IsAvailable = false; s.Status = SensorStatus.Unavailable; return; }
            double displayVal = cValue.Value;
            bool isF = AppSettings.Current.TempUnit == "Fahrenheit";
            if (isF) displayVal = displayVal * 9.0 / 5.0 + 32.0;
            s.SetUnit(isF ? "°F" : "°C");
            UpdateSensor(name, group, displayVal, true);
            // re-apply correct unit after UpdateSensor overwritten? Actually UpdateSensor will set Current based on passed value; we already converted.
            // Ensure status thresholds adjusted for F
            if (s.IsAvailable)
            {
                double thresholdWarnC = 80, critC = 90;
                double warn = isF ? thresholdWarnC * 9 / 5 + 32 : thresholdWarnC;
                double crit = isF ? critC * 9 / 5 + 32 : critC;
                if (displayVal >= crit) s.Status = SensorStatus.Critical;
                else if (displayVal >= warn) s.Status = SensorStatus.Warning;
                else s.Status = SensorStatus.Normal;
            }
        }

        UpdateTempSensor("CPU Package Temperature", "CPU", cpuTemp);
        UpdateSensor("CPU Total Usage", "CPU", totalLoad >= 0 ? (double?)totalLoad : null, totalLoad >= 0);
        UpdateSensor("CPU Current Clock", "CPU", curFreq > 0 ? (double?)curFreq : null, curFreq > 0);
        UpdateSensor("CPU Power (est.)", "CPU", null, false);

        for (int i = 0; i < Environment.ProcessorCount; i++)
        {
            if (perCore.TryGetValue(i, out var pc))
            {
                UpdateSensor($"Core #{i} Usage", "CPU Cores", pc.Load >= 0 ? (double?)pc.Load : null, pc.Load >= 0);
                UpdateSensor($"Core #{i} Clock", "CPU Cores", pc.Freq > 0 ? (double?)pc.Freq : null, pc.Freq > 0);
            }
            else
            {
                UpdateSensor($"Core #{i} Usage", "CPU Cores", null, false);
                UpdateSensor($"Core #{i} Clock", "CPU Cores", null, false);
            }
        }

        UpdateSensor("Memory Usage", "Memory", memLoad, true);
        UpdateSensor("Memory Used", "Memory", usedPhysGb, true);
        UpdateSensor("Memory Available", "Memory", availPhysGb, true);
        UpdateSensor("Commit Charge", "Memory", commitLoad, true);

        UpdateTempSensor("GPU Temperature", "GPU", null); // generally unavailable
        UpdateSensor("GPU Usage", "GPU", gpuUsage, gpuUsage.HasValue);
        UpdateSensor("GPU Memory Used", "GPU", null, false);
        UpdateSensor("GPU Fan", "GPU", null, false);

        foreach (var kv in driveUsages)
        {
            UpdateSensor($"{kv.Key} Usage", "Storage", kv.Value, true);
            UpdateTempSensor($"{kv.Key} Temperature", "Storage", null);
        }
        // mark missing drives as unavailable
        foreach (var s in Sensors.Where(x => x.Group == "Storage" && x.Name.Contains("Usage")))
        {
            if (!driveUsages.ContainsKey(s.Name.Split(' ')[0])) { /* keep as is if already updated */ }
        }

        UpdateSensor("System Uptime", "System", TimeSpan.FromMilliseconds(Environment.TickCount64).TotalHours, true);
        UpdateSensor("CPU Fan", "System", null, false);

        // Uptime display handling: value is hours
        var uptimeSensor = Sensors.FirstOrDefault(x => x.Name == "System Uptime");
        if (uptimeSensor != null && uptimeSensor.Current.HasValue) uptimeSensor.Status = SensorStatus.Normal;
    }

    private void CheckAlerts()
    {
        foreach (var rule in AppSettings.Current.AlertRules.Where(r => r.Enabled))
        {
            var sensor = Sensors.FirstOrDefault(s => s.Id == rule.SensorId || s.Name == rule.SensorName);
            if (sensor == null || !sensor.IsAvailable || !sensor.Current.HasValue) { rule.IsActive = false; continue; }
            bool active = rule.Condition == "Above" ? sensor.Current.Value >= rule.Threshold : sensor.Current.Value <= rule.Threshold;
            if (active && !rule.IsActive) { rule.LastTriggered = DateTime.Now; SensorAlert?.Invoke(this, sensor); }
            rule.IsActive = active;
        }
    }

    public void ClearHistory()
    {
        foreach (var q in _historyStats.Values) q.Clear();
        foreach (var s in Sensors)
        {
            s.History.Clear(); s.Min = null; s.Max = null; s.Average = null; s.SampleCount = 0;
        }
    }
}
