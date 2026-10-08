using Microsoft.Win32;
using HardwareInspectorWin.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HardwareInspectorWin.Services;

public sealed class HardwareService
{
    public static HardwareService Instance { get; } = new();
    private HardwareService() { }

    public OverviewData Overview { get; private set; } = new();
    public List<HardwareNode> Tree { get; private set; } = new();

    public async Task RefreshAsync()
    {
        var overview = new OverviewData();
        var tree = new List<HardwareNode>();
        await Task.Run(() =>
        {
            try { CollectCpu(overview, tree); } catch (Exception ex) { AppLogger.LogException(ex, "CollectCpu"); overview.Warnings.Add($"CPU: {ex.Message}"); }
            try { CollectBoardBios(overview, tree); } catch (Exception ex) { AppLogger.LogException(ex, "CollectBoard"); overview.Warnings.Add($"Motherboard: {ex.Message}"); }
            try { CollectMemory(overview, tree); } catch (Exception ex) { AppLogger.LogException(ex, "CollectMemory"); overview.Warnings.Add($"Memory: {ex.Message}"); }
            try { CollectGpu(overview, tree); } catch (Exception ex) { AppLogger.LogException(ex, "CollectGpu"); overview.Warnings.Add($"GPU: {ex.Message}"); }
            try { CollectStorage(overview, tree); } catch (Exception ex) { AppLogger.LogException(ex, "CollectStorage"); overview.Warnings.Add($"Storage: {ex.Message}"); }
            try { CollectOs(overview, tree); } catch (Exception ex) { AppLogger.LogException(ex, "CollectOs"); overview.Warnings.Add($"OS: {ex.Message}"); }
            try { CollectFirmwareSecurity(overview, tree); } catch (Exception ex) { AppLogger.LogException(ex, "CollectSecurity"); overview.Warnings.Add($"Security: {ex.Message}"); }
            try { CollectNetworkAudio(overview, tree); } catch (Exception ex) { AppLogger.LogException(ex, "CollectPeripherals"); }
        });
        overview.CollectedAt = DateTime.Now;
        Overview = overview;
        Tree = tree;
    }

    private void CollectCpu(OverviewData ov, List<HardwareNode> tree)
    {
        var cpus = WmiHelper.Query("SELECT * FROM Win32_Processor");
        var node = new HardwareNode { Name = "CPU", Type = "Processor", Icon = "\uE950", Summary = cpus.Count > 0 ? WmiHelper.S(cpus[0], "Name") : "Not available" };
        if (cpus.Count == 0)
        {
            node.Properties.Add(new HardwareProperty { Name = "Status", Value = "No processor reported via WMI" });
            tree.Add(node);
            ov.CpuName = "Not available";
            return;
        }
        var cpu = cpus[0];
        string name = WmiHelper.S(cpu, "Name");
        string cores = $"{WmiHelper.UI(cpu, "NumberOfCores")} cores / {WmiHelper.UI(cpu, "NumberOfLogicalProcessors")} threads";
        string baseClock = WmiHelper.UI(cpu, "MaxClockSpeed") > 0 ? $"{WmiHelper.UI(cpu, "MaxClockSpeed"):N0} MHz" : "—";
        string socket = WmiHelper.S(cpu, "SocketDesignation");
        ov.CpuName = name;
        ov.CpuCores = cores;
        ov.CpuBaseClock = baseClock;
        ov.CpuSocket = socket;

        node.Properties.Add(new HardwareProperty { Name = "Name", Value = name });
        node.Properties.Add(new HardwareProperty { Name = "Manufacturer", Value = WmiHelper.S(cpu, "Manufacturer") });
        node.Properties.Add(new HardwareProperty { Name = "Architecture", Value = DescribeCpuArch(WmiHelper.UI(cpu, "Architecture")) });
        node.Properties.Add(new HardwareProperty { Name = "Socket", Value = socket });
        node.Properties.Add(new HardwareProperty { Name = "Cores / Threads", Value = cores });
        node.Properties.Add(new HardwareProperty { Name = "Base clock", Value = baseClock });
        uint cur = WmiHelper.UI(cpu, "CurrentClockSpeed");
        node.Properties.Add(new HardwareProperty { Name = "Current clock", Value = cur > 0 ? $"{cur:N0} MHz" : "—" });
        int l2 = WmiHelper.I(cpu, "L2CacheSize");
        int l3 = WmiHelper.I(cpu, "L3CacheSize");
        if (l2 > 0) node.Properties.Add(new HardwareProperty { Name = "L2 cache", Value = $"{l2:N0} KB" });
        if (l3 > 0) node.Properties.Add(new HardwareProperty { Name = "L3 cache", Value = $"{l3:N0} KB" });
        node.Properties.Add(new HardwareProperty { Name = "Processor ID", Value = WmiHelper.S(cpu, "ProcessorId") });
        node.Properties.Add(new HardwareProperty { Name = "Virtualization (firmware)", Value = WmiHelper.UI(cpu, "VirtualizationFirmwareEnabled") == 1 ? "Enabled" : "Disabled" });
        node.Properties.Add(new HardwareProperty { Name = "Second Level Address Translation", Value = WmiHelper.UI(cpu, "SecondLevelAddressTranslation") == 1 ? "Supported" : "Not reported" });
        node.Properties.Add(new HardwareProperty { Name = "Socket count", Value = cpus.Count.ToString() });

        // Per-core child nodes
        int logical = (int)WmiHelper.UI(cpu, "NumberOfLogicalProcessors");
        for (int i = 0; i < Math.Min(logical, 64); i++)
        {
            var core = new HardwareNode { Name = $"Core #{i}", Type = "Logical Processor", Icon = "\uE950" };
            core.Properties.Add(new HardwareProperty { Name = "Logical index", Value = i.ToString() });
            node.Children.Add(core);
        }

        // Temperature probe if available
        try
        {
            var temps = WmiHelper.Query("SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature", @"root\WMI");
            if (temps.Count > 0)
            {
                uint t = WmiHelper.UI(temps[0], "CurrentTemperature");
                if (t > 0)
                {
                    int c = (int)Math.Round(t / 10.0 - 273.15);
                    if (c is >= -20 and <= 150) ov.CpuTempC = c;
                }
            }
        }
        catch { }

        tree.Add(node);
    }

    private void CollectBoardBios(OverviewData ov, List<HardwareNode> tree)
    {
        var board = WmiHelper.Query("SELECT * FROM Win32_BaseBoard").FirstOrDefault();
        var bios = WmiHelper.Query("SELECT * FROM Win32_BIOS").FirstOrDefault();
        var node = new HardwareNode { Name = "Motherboard", Type = "BaseBoard", Icon = "\uE770", Summary = board != null ? $"{WmiHelper.S(board, "Manufacturer")} {WmiHelper.S(board, "Product")}".Trim() : "Not available" };
        if (board != null)
        {
            ov.BoardVendor = WmiHelper.S(board, "Manufacturer");
            ov.BoardModel = WmiHelper.S(board, "Product");
            node.Properties.Add(new HardwareProperty { Name = "Manufacturer", Value = WmiHelper.S(board, "Manufacturer") });
            node.Properties.Add(new HardwareProperty { Name = "Model", Value = WmiHelper.S(board, "Product") });
            node.Properties.Add(new HardwareProperty { Name = "Version", Value = WmiHelper.S(board, "Version") });
            node.Properties.Add(new HardwareProperty { Name = "Serial number", Value = WmiHelper.S(board, "SerialNumber") });
            node.Properties.Add(new HardwareProperty { Name = "Part number", Value = WmiHelper.S(board, "PartNumber") });
            node.Properties.Add(new HardwareProperty { Name = "Hosting board", Value = WmiHelper.UI(board, "HostingBoard") == 1 ? "Yes" : "No" });
        }
        if (bios != null)
        {
            ov.BiosVersion = WmiHelper.S(bios, "SMBIOSBIOSVersion");
            var date = WmiHelper.Dtm(bios, "ReleaseDate");
            ov.BiosDate = date.HasValue ? date.Value.ToString("yyyy-MM-dd") : "—";
            var biosNode = new HardwareNode { Name = "BIOS / Firmware", Type = "BIOS", Icon = "\uE7B8" };
            biosNode.Properties.Add(new HardwareProperty { Name = "Brand", Value = WmiHelper.S(bios, "Manufacturer") });
            biosNode.Properties.Add(new HardwareProperty { Name = "Version", Value = WmiHelper.S(bios, "SMBIOSBIOSVersion") });
            biosNode.Properties.Add(new HardwareProperty { Name = "Release date", Value = WmiHelper.ToIso(date) });
            biosNode.Properties.Add(new HardwareProperty { Name = "SMBIOS version", Value = $"{WmiHelper.UI(bios, "SMBIOSMajorVersion")}.{WmiHelper.UI(bios, "SMBIOSMinorVersion")}" });
            biosNode.Properties.Add(new HardwareProperty { Name = "Serial number", Value = WmiHelper.S(bios, "SerialNumber") });
            biosNode.Properties.Add(new HardwareProperty { Name = "Primary BIOS", Value = WmiHelper.UI(bios, "PrimaryBIOS") == 1 ? "Yes" : "No" });
            node.Children.Add(biosNode);
        }
        // System enclosure
        try
        {
            var enc = WmiHelper.Query("SELECT * FROM Win32_SystemEnclosure").FirstOrDefault();
            if (enc != null)
            {
                var enclosure = new HardwareNode { Name = "Chassis", Type = "Enclosure", Icon = "\uE7F8" };
                enclosure.Properties.Add(new HardwareProperty { Name = "Manufacturer", Value = WmiHelper.S(enc, "Manufacturer") });
                enclosure.Properties.Add(new HardwareProperty { Name = "Model", Value = WmiHelper.S(enc, "Model") });
                enclosure.Properties.Add(new HardwareProperty { Name = "Serial number", Value = WmiHelper.S(enc, "SerialNumber") });
                enclosure.Properties.Add(new HardwareProperty { Name = "Chassis type", Value = DescribeChassis(WmiHelper.I(enc, "ChassisTypes")) });
                node.Children.Add(enclosure);
            }
        }
        catch { }
        tree.Add(node);
    }

    private void CollectMemory(OverviewData ov, List<HardwareNode> tree)
    {
        var sticks = WmiHelper.Query("SELECT * FROM Win32_PhysicalMemory");
        var cs = WmiHelper.Query("SELECT * FROM Win32_ComputerSystem").FirstOrDefault();
        var mem = NativeMethods.QueryMemoryStatus();
        double totalGib = mem.ullTotalPhys / 1024.0 / 1024 / 1024;
        ov.TotalRamBytes = mem.ullTotalPhys;
        int load = (int)mem.dwMemoryLoad;
        ov.MemoryLoad = load;
        ov.MemoryUsedText = $"{(mem.ullTotalPhys - mem.ullAvailPhys) / 1024.0 / 1024 / 1024:0.0} GB used ({load}%)";

        var node = new HardwareNode { Name = "Memory", Type = "RAM", Icon = "\uE8F1", Summary = $"{totalGib:0.0} GB • {load}% in use" };
        node.Properties.Add(new HardwareProperty { Name = "Total physical", Value = $"{totalGib:0.00} GiB ({mem.ullTotalPhys:N0} bytes)" });
        node.Properties.Add(new HardwareProperty { Name = "Available", Value = $"{mem.ullAvailPhys / 1024.0 / 1024 / 1024:0.00} GiB" });
        node.Properties.Add(new HardwareProperty { Name = "Memory load", Value = $"{load} %" });
        node.Properties.Add(new HardwareProperty { Name = "Total page file (commit)", Value = $"{mem.ullTotalPageFile / 1024.0 / 1024 / 1024:0.0} GiB" });
        if (cs != null) node.Properties.Add(new HardwareProperty { Name = "Total reported by system", Value = WmiHelper.FormatGiB(WmiHelper.U(cs, "TotalPhysicalMemory")) });

        if (sticks.Count > 0)
        {
            var first = sticks[0];
            string type = DescribeMemoryType(WmiHelper.UI(first, "SMBIOSMemoryType"), WmiHelper.UI(first, "MemoryType"));
            uint speed = WmiHelper.UI(first, "ConfiguredClockSpeed") > 0 ? WmiHelper.UI(first, "ConfiguredClockSpeed") : WmiHelper.UI(first, "Speed");
            ov.RamType = type;
            ov.RamSpeed = speed > 0 ? $"{speed} MT/s" : "—";
            node.Properties.Add(new HardwareProperty { Name = "Type", Value = type });
            node.Properties.Add(new HardwareProperty { Name = "Configured speed", Value = ov.RamSpeed });
        }

        foreach (var s in sticks.OrderBy(x => WmiHelper.S(x, "DeviceLocator")))
        {
            var child = new HardwareNode { Name = $"{WmiHelper.S(s, "DeviceLocator")} ({WmiHelper.S(s, "BankLabel")})".Trim(), Type = "DIMM", Icon = "\uE8F1" };
            ulong cap = WmiHelper.U(s, "Capacity");
            child.Summary = WmiHelper.FormatGiB(cap);
            child.Properties.Add(new HardwareProperty { Name = "Capacity", Value = WmiHelper.FormatGiB(cap) });
            child.Properties.Add(new HardwareProperty { Name = "Type", Value = DescribeMemoryType(WmiHelper.UI(s, "SMBIOSMemoryType"), WmiHelper.UI(s, "MemoryType")) });
            uint sp = WmiHelper.UI(s, "ConfiguredClockSpeed") > 0 ? WmiHelper.UI(s, "ConfiguredClockSpeed") : WmiHelper.UI(s, "Speed");
            child.Properties.Add(new HardwareProperty { Name = "Speed", Value = sp > 0 ? $"{sp} MT/s" : "—" });
            child.Properties.Add(new HardwareProperty { Name = "Manufacturer", Value = WmiHelper.S(s, "Manufacturer") });
            child.Properties.Add(new HardwareProperty { Name = "Part number", Value = WmiHelper.S(s, "PartNumber") });
            child.Properties.Add(new HardwareProperty { Name = "Serial number", Value = WmiHelper.S(s, "SerialNumber") });
            child.Properties.Add(new HardwareProperty { Name = "Form factor", Value = DescribeFormFactor(WmiHelper.UI(s, "FormFactor")) });
            child.Properties.Add(new HardwareProperty { Name = "Data width", Value = WmiHelper.UI(s, "DataWidth").ToString() });
            child.Properties.Add(new HardwareProperty { Name = "Total width", Value = WmiHelper.UI(s, "TotalWidth").ToString() });
            node.Children.Add(child);
        }
        if (sticks.Count == 0) node.Properties.Add(new HardwareProperty { Name = "Modules", Value = "No SPD data exposed by firmware" });
        tree.Add(node);
    }

    private void CollectGpu(OverviewData ov, List<HardwareNode> tree)
    {
        var gpus = WmiHelper.Query("SELECT * FROM Win32_VideoController");
        var node = new HardwareNode { Name = "GPU", Type = "Graphics", Icon = "\uEB9F", Summary = gpus.Count > 0 ? WmiHelper.S(gpus[0], "Name") : "Not available" };
        if (gpus.Count == 0)
        {
            node.Properties.Add(new HardwareProperty { Name = "Status", Value = "No display adapter reported" });
            tree.Add(node);
            ov.GpuName = "Not available";
            return;
        }
        ov.GpuName = string.Join(" + ", gpus.Select(g => WmiHelper.S(g, "Name")).Where(s => s.Length > 0).Take(2));
        var first = gpus[0];
        ov.GpuDriver = WmiHelper.S(first, "DriverVersion");
        ulong vram = WmiHelper.U(first, "AdapterRAM");
        ov.GpuMemory = vram > 0 && vram < uint.MaxValue ? WmiHelper.FormatGiB(vram) : "— (reported as 4GB cap via WMI)";

        foreach (var g in gpus)
        {
            string name = WmiHelper.S(g, "Name");
            var child = new HardwareNode { Name = name.Length > 0 ? name : "Display adapter", Type = "GPU", Icon = "\uEB9F", Summary = WmiHelper.S(g, "AdapterCompatibility") };
            child.Properties.Add(new HardwareProperty { Name = "Name", Value = name });
            child.Properties.Add(new HardwareProperty { Name = "Vendor", Value = WmiHelper.S(g, "AdapterCompatibility") });
            child.Properties.Add(new HardwareProperty { Name = "PNP ID", Value = WmiHelper.S(g, "PNPDeviceID") });
            child.Properties.Add(new HardwareProperty { Name = "Driver version", Value = WmiHelper.S(g, "DriverVersion") });
            var dd = WmiHelper.Dtm(g, "DriverDate");
            child.Properties.Add(new HardwareProperty { Name = "Driver date", Value = WmiHelper.ToIso(dd) });
            child.Properties.Add(new HardwareProperty { Name = "Resolution", Value = $"{WmiHelper.UI(g, "CurrentHorizontalResolution")} × {WmiHelper.UI(g, "CurrentVerticalResolution")} @ {WmiHelper.UI(g, "CurrentRefreshRate")} Hz" });
            child.Properties.Add(new HardwareProperty { Name = "Bits per pixel", Value = WmiHelper.UI(g, "CurrentBitsPerPixel").ToString() });
            child.Properties.Add(new HardwareProperty { Name = "Video memory", Value = WmiHelper.FormatGiB(WmiHelper.U(g, "AdapterRAM")) });
            child.Properties.Add(new HardwareProperty { Name = "Status", Value = WmiHelper.S(g, "Status", "OK") });
            child.Properties.Add(new HardwareProperty { Name = "Availability", Value = WmiHelper.UI(g, "Availability").ToString() });
            node.Children.Add(child);
        }
        // Displays
        try
        {
            var displaysNode = new HardwareNode { Name = "Displays", Type = "Monitor", Icon = "\uE7F4" };
            int cnt = 0;
            var adapterDevice = new NativeMethods.DISPLAY_DEVICEW { DeviceName = "", DeviceString = "", DeviceID = "", DeviceKey = "" };
            adapterDevice.cb = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.DISPLAY_DEVICEW>();
            for (uint ai = 0; ; ai++)
            {
                var adapter = adapterDevice;
                if (!NativeMethods.EnumDisplayDevicesW(null, ai, ref adapter, 0)) break;
                if ((adapter.StateFlags & 0x1) == 0 && (adapter.StateFlags & 0x8) == 0) continue;
                for (uint mi = 0; ; mi++)
                {
                    var mon = new NativeMethods.DISPLAY_DEVICEW { DeviceName = "", DeviceString = "", DeviceID = "", DeviceKey = "" };
                    mon.cb = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.DISPLAY_DEVICEW>();
                    if (!NativeMethods.EnumDisplayDevicesW(adapter.DeviceName, mi, ref mon, 1)) break;
                    if ((mon.StateFlags & 0x1) == 0) continue;
                    cnt++;
                    var child = new HardwareNode { Name = mon.DeviceString.Length > 0 ? mon.DeviceString : mon.DeviceID, Type = "Monitor", Icon = "\uE7F4" };
                    child.Properties.Add(new HardwareProperty { Name = "Device", Value = mon.DeviceString });
                    child.Properties.Add(new HardwareProperty { Name = "Device ID", Value = mon.DeviceID });
                    var mode = new NativeMethods.DEVMODEW { dmDeviceName = "", dmFormName = "" };
                    mode.dmSize = (short)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.DEVMODEW>();
                    if (NativeMethods.EnumDisplaySettingsExW(mon.DeviceName, NativeMethods.ENUM_CURRENT_SETTINGS, ref mode, 0))
                        child.Properties.Add(new HardwareProperty { Name = "Current mode", Value = $"{mode.dmPelsWidth} × {mode.dmPelsHeight} @ {mode.dmDisplayFrequency} Hz {mode.dmBitsPerPel} bpp" });
                    displaysNode.Children.Add(child);
                }
            }
            displaysNode.Summary = $"{cnt} display(s)";
            if (cnt > 0) node.Children.Add(displaysNode);
        }
        catch { }
        tree.Add(node);
    }

    private void CollectStorage(OverviewData ov, List<HardwareNode> tree)
    {
        var disks = WmiHelper.Query("SELECT * FROM Win32_DiskDrive").OrderBy(d => WmiHelper.UI(d, "Index")).ToList();
        var logical = WmiHelper.Query("SELECT * FROM Win32_LogicalDisk");
        ov.DriveCount = disks.Count;
        string root = Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\') ?? "C:";
        ov.SystemDriveFree = logical.FirstOrDefault(l => WmiHelper.S(l, "DeviceID").Equals(root, StringComparison.OrdinalIgnoreCase)) is var sys && sys != null ? WmiHelper.FormatDecimalGB(WmiHelper.U(sys, "FreeSpace")) + " free" : "—";

        var node = new HardwareNode { Name = "Storage", Type = "Disk", Icon = "\uEDA2", Summary = $"{disks.Count} physical • {logical.Count} volumes" };
        foreach (var d in disks)
        {
            int idx = WmiHelper.I(d, "Index");
            string model = WmiHelper.S(d, "Model");
            ulong size = WmiHelper.U(d, "Size");
            var child = new HardwareNode { Name = $"Drive {idx}: {model}", Type = "DiskDrive", Icon = "\uEDA2", Summary = size > 0 ? $"{WmiHelper.FormatDecimalGB(size)} ({WmiHelper.FormatGiB(size)})" : "Unknown size" };
            child.Properties.Add(new HardwareProperty { Name = "Model", Value = model });
            child.Properties.Add(new HardwareProperty { Name = "Size", Value = size > 0 ? $"{WmiHelper.FormatDecimalGB(size)} / {WmiHelper.FormatGiB(size)}" : "—" });
            child.Properties.Add(new HardwareProperty { Name = "Interface", Value = WmiHelper.S(d, "InterfaceType") });
            child.Properties.Add(new HardwareProperty { Name = "Media type", Value = WmiHelper.S(d, "MediaType") });
            child.Properties.Add(new HardwareProperty { Name = "Serial number", Value = WmiHelper.S(d, "SerialNumber").Replace("_", "").Trim() });
            child.Properties.Add(new HardwareProperty { Name = "Firmware", Value = WmiHelper.S(d, "FirmwareRevision") });
            child.Properties.Add(new HardwareProperty { Name = "Partitions", Value = WmiHelper.UI(d, "Partitions").ToString() });
            child.Properties.Add(new HardwareProperty { Name = "PNP device ID", Value = WmiHelper.S(d, "PNPDeviceID") });
            child.Properties.Add(new HardwareProperty { Name = "Bytes per sector", Value = WmiHelper.UI(d, "BytesPerSector").ToString() });
            child.Properties.Add(new HardwareProperty { Name = "Total sectors", Value = WmiHelper.U(d, "TotalSectors").ToString() });
            child.Properties.Add(new HardwareProperty { Name = "Status", Value = WmiHelper.S(d, "Status", "Unknown") });
            node.Children.Add(child);
        }
        // Volumes group
        var volNode = new HardwareNode { Name = "Volumes", Type = "LogicalDisk", Icon = "\uEDA2" };
        foreach (var ld in logical)
        {
            string drive = WmiHelper.S(ld, "DeviceID");
            ulong size = WmiHelper.U(ld, "Size");
            ulong free = WmiHelper.U(ld, "FreeSpace");
            string pct = size > 0 ? $"{free * 100.0 / size:0}% free" : "—";
            var child = new HardwareNode { Name = $"{drive} {WmiHelper.S(ld, "VolumeName")}".Trim(), Type = "Volume", Icon = "\uEDA2", Summary = size > 0 ? $"{WmiHelper.FormatDecimalGB(size)} ({pct}) – {WmiHelper.S(ld, "FileSystem")}" : WmiHelper.S(ld, "Description") };
            child.Properties.Add(new HardwareProperty { Name = "Label", Value = WmiHelper.S(ld, "VolumeName") });
            child.Properties.Add(new HardwareProperty { Name = "File system", Value = WmiHelper.S(ld, "FileSystem") });
            child.Properties.Add(new HardwareProperty { Name = "Size", Value = size > 0 ? WmiHelper.FormatDecimalGB(size) : "—" });
            child.Properties.Add(new HardwareProperty { Name = "Free space", Value = size > 0 ? $"{WmiHelper.FormatDecimalGB(free)} ({pct})" : "—" });
            child.Properties.Add(new HardwareProperty { Name = "Drive type", Value = DescribeDriveType(WmiHelper.UI(ld, "DriveType")) });
            child.Properties.Add(new HardwareProperty { Name = "Volume serial", Value = WmiHelper.S(ld, "VolumeSerialNumber") });
            volNode.Children.Add(child);
        }
        if (volNode.Children.Count > 0) node.Children.Add(volNode);
        // Optical
        var optical = WmiHelper.Query("SELECT * FROM Win32_CDROMDrive");
        if (optical.Count > 0)
        {
            var optNode = new HardwareNode { Name = "Optical drives", Type = "CDROM", Icon = "\uE958" };
            foreach (var o in optical)
            {
                var child = new HardwareNode { Name = WmiHelper.S(o, "Caption"), Type = "CDROM", Icon = "\uE958" };
                child.Properties.Add(new HardwareProperty { Name = "Drive", Value = WmiHelper.S(o, "Drive") });
                child.Properties.Add(new HardwareProperty { Name = "Media loaded", Value = WmiHelper.S(o, "MediaLoaded") });
                optNode.Children.Add(child);
            }
            node.Children.Add(optNode);
        }
        tree.Add(node);
    }

    private void CollectOs(OverviewData ov, List<HardwareNode> tree)
    {
        var os = WmiHelper.Query("SELECT * FROM Win32_OperatingSystem").FirstOrDefault();
        var cs = WmiHelper.Query("SELECT * FROM Win32_ComputerSystem").FirstOrDefault();
        var node = new HardwareNode { Name = "Operating System", Type = "OS", Icon = "\uEC7A", Summary = os != null ? WmiHelper.S(os, "Caption") : "Not available" };
        if (os != null)
        {
            string caption = WmiHelper.S(os, "Caption");
            string version = WmiHelper.S(os, "Version");
            string build = WmiHelper.S(os, "BuildNumber");
            string arch = WmiHelper.S(os, "OSArchitecture");
            string displayVersion = ReadRegistry(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "DisplayVersion");
            string ubr = ReadRegistry(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "UBR");
            string fullBuild = build + (ubr.Length > 0 ? "." + ubr : "");
            ov.OsName = caption;
            ov.OsVersion = displayVersion.Length > 0 ? $"{version} ({displayVersion})" : version;
            ov.OsBuild = fullBuild;
            ov.OsArch = arch;
            node.Properties.Add(new HardwareProperty { Name = "Name", Value = caption });
            node.Properties.Add(new HardwareProperty { Name = "Version", Value = ov.OsVersion });
            node.Properties.Add(new HardwareProperty { Name = "Build", Value = fullBuild });
            node.Properties.Add(new HardwareProperty { Name = "Architecture", Value = arch });
            node.Properties.Add(new HardwareProperty { Name = "Install date", Value = WmiHelper.ToIso(WmiHelper.Dtm(os, "InstallDate")) });
            node.Properties.Add(new HardwareProperty { Name = "Last boot", Value = WmiHelper.ToIso(WmiHelper.Dtm(os, "LastBootUpTime")) });
            node.Properties.Add(new HardwareProperty { Name = "Registered user", Value = WmiHelper.S(os, "RegisteredUser") });
            node.Properties.Add(new HardwareProperty { Name = "Serial number", Value = WmiHelper.S(os, "SerialNumber") });
            node.Properties.Add(new HardwareProperty { Name = "System directory", Value = WmiHelper.S(os, "SystemDirectory") });
            var mui = WmiHelper.Arr(os, "MUILanguages");
            if (mui.Length > 0) node.Properties.Add(new HardwareProperty { Name = "Display languages", Value = string.Join(", ", mui) });
        }
        if (cs != null)
        {
            var comp = new HardwareNode { Name = "Computer", Type = "ComputerSystem", Icon = "\uE977" };
            comp.Properties.Add(new HardwareProperty { Name = "Computer name", Value = Environment.MachineName });
            comp.Properties.Add(new HardwareProperty { Name = "Domain", Value = WmiHelper.S(cs, "Domain") });
            comp.Properties.Add(new HardwareProperty { Name = "Manufacturer", Value = WmiHelper.S(cs, "Manufacturer") });
            comp.Properties.Add(new HardwareProperty { Name = "Model", Value = WmiHelper.S(cs, "Model") });
            comp.Properties.Add(new HardwareProperty { Name = "System type", Value = WmiHelper.S(cs, "SystemType") });
            comp.Properties.Add(new HardwareProperty { Name = "Total physical memory", Value = WmiHelper.FormatGiB(WmiHelper.U(cs, "TotalPhysicalMemory")) });
            node.Children.Add(comp);
        }
        // Time zone
        var tz = TimeZoneInfo.Local;
        node.Properties.Add(new HardwareProperty { Name = "Time zone", Value = $"{tz.Id} (UTC{tz.GetUtcOffset(DateTime.Now):hh\\:mm})" });
        tree.Add(node);
    }

    private void CollectFirmwareSecurity(OverviewData ov, List<HardwareNode> tree)
    {
        var node = new HardwareNode { Name = "Firmware & Security", Type = "Security", Icon = "\uE72E" };
        // Firmware mode
        string mode = "Unknown";
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State");
            mode = key != null ? "UEFI" : "Legacy BIOS";
            if (mode == "UEFI")
            {
                using var sbKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State");
                var enabled = sbKey?.GetValue("UEFISecureBootEnabled");
                if (enabled is int i) ov.SecureBoot = i == 1 ? "Enabled" : "Disabled";
                else if (enabled is uint u) ov.SecureBoot = u == 1 ? "Enabled" : "Disabled";
                else ov.SecureBoot = "Unknown";
            }
            else ov.SecureBoot = "Not applicable (Legacy)";
        }
        catch { ov.SecureBoot = "Unknown"; }
        ov.FirmwareMode = mode;
        node.Properties.Add(new HardwareProperty { Name = "Firmware mode", Value = mode });
        node.Properties.Add(new HardwareProperty { Name = "Secure Boot", Value = ov.SecureBoot });

        // TPM
        try
        {
            var tpm = WmiHelper.Query("SELECT * FROM Win32_Tpm", @"root\CIMV2\Security\MicrosoftTpm").FirstOrDefault();
            if (tpm != null)
            {
                string spec = WmiHelper.S(tpm, "SpecVersion");
                ov.TpmText = spec.Length > 0 ? $"TPM {spec}" : "TPM present";
                node.Properties.Add(new HardwareProperty { Name = "TPM", Value = ov.TpmText });
                node.Properties.Add(new HardwareProperty { Name = "TPM manufacturer", Value = WmiHelper.S(tpm, "ManufacturerVersion") });
                node.Properties.Add(new HardwareProperty { Name = "Is enabled", Value = WmiHelper.UI(tpm, "IsEnabled_InitialValue") == 1 ? "Yes" : "No" });
                node.Properties.Add(new HardwareProperty { Name = "Is activated", Value = WmiHelper.UI(tpm, "IsActivated_InitialValue") == 1 ? "Yes" : "No" });
            }
            else
            {
                ov.TpmText = "Not found / Not enabled";
                node.Properties.Add(new HardwareProperty { Name = "TPM", Value = ov.TpmText });
            }
        }
        catch
        {
            ov.TpmText = "Not available (access denied or not present)";
            node.Properties.Add(new HardwareProperty { Name = "TPM", Value = ov.TpmText });
        }

        // Virtualization
        try
        {
            var proc = WmiHelper.Query("SELECT VirtualizationFirmwareEnabled FROM Win32_Processor").FirstOrDefault();
            bool virt = proc != null && WmiHelper.UI(proc, "VirtualizationFirmwareEnabled") == 1;
            var cs = WmiHelper.Query("SELECT HypervisorPresent FROM Win32_ComputerSystem").FirstOrDefault();
            bool hyper = cs != null && WmiHelper.UI(cs, "HypervisorPresent") == 1;
            ov.Virtualization = virt ? (hyper ? "Enabled (Hypervisor present)" : "Enabled") : "Disabled";
            node.Properties.Add(new HardwareProperty { Name = "Virtualization firmware", Value = virt ? "Enabled" : "Disabled" });
            node.Properties.Add(new HardwareProperty { Name = "Hypervisor present", Value = hyper ? "Yes" : "No" });
        }
        catch { }

        // BitLocker via WMI?
        try
        {
            var vols = WmiHelper.Query("SELECT * FROM Win32_EncryptableVolume", @"root\CIMV2\Security\MicrosoftVolumeEncryption");
            if (vols.Count > 0)
            {
                var bl = new HardwareNode { Name = "BitLocker", Type = "Encryption", Icon = "\uE72E" };
                foreach (var v in vols)
                {
                    string id = WmiHelper.S(v, "DeviceID");
                    uint prot = WmiHelper.UI(v, "ProtectionStatus");
                    bl.Properties.Add(new HardwareProperty { Name = id, Value = prot == 1 ? "Protection On" : prot == 0 ? "Off" : prot.ToString() });
                }
                node.Children.Add(bl);
            }
        }
        catch { }

        tree.Add(node);
    }

    private void CollectNetworkAudio(OverviewData ov, List<HardwareNode> tree)
    {
        // Network
        var netNode = new HardwareNode { Name = "Network", Type = "Network", Icon = "\uE701" };
        var adapters = WmiHelper.Query("SELECT * FROM Win32_NetworkAdapter WHERE NetConnectionID IS NOT NULL");
        foreach (var a in adapters)
        {
            var child = new HardwareNode { Name = WmiHelper.S(a, "Name"), Type = "NetAdapter", Icon = "\uE701" };
            child.Properties.Add(new HardwareProperty { Name = "Connection", Value = WmiHelper.S(a, "NetConnectionID") });
            child.Properties.Add(new HardwareProperty { Name = "Adapter type", Value = WmiHelper.S(a, "AdapterType") });
            bool en = WmiHelper.UI(a, "NetEnabled") == 1;
            child.Properties.Add(new HardwareProperty { Name = "Enabled", Value = en ? "Yes" : "No" });
            child.Properties.Add(new HardwareProperty { Name = "MAC address", Value = WmiHelper.S(a, "MACAddress") });
            ulong speed = WmiHelper.U(a, "Speed");
            child.Properties.Add(new HardwareProperty { Name = "Speed", Value = speed > 0 ? $"{speed / 1_000_000} Mbps" : "—" });
            child.Properties.Add(new HardwareProperty { Name = "PNP ID", Value = WmiHelper.S(a, "PNPDeviceID") });
            netNode.Children.Add(child);
        }
        if (netNode.Children.Count > 0) tree.Add(netNode);

        // Audio
        var audio = WmiHelper.Query("SELECT * FROM Win32_SoundDevice");
        if (audio.Count > 0)
        {
            var audioNode = new HardwareNode { Name = "Audio", Type = "Sound", Icon = "\uE8D6" };
            foreach (var d in audio)
            {
                var child = new HardwareNode { Name = WmiHelper.S(d, "Name"), Type = "AudioDevice", Icon = "\uE8D6" };
                child.Properties.Add(new HardwareProperty { Name = "Manufacturer", Value = WmiHelper.S(d, "Manufacturer") });
                child.Properties.Add(new HardwareProperty { Name = "Status", Value = WmiHelper.S(d, "Status") });
                child.Properties.Add(new HardwareProperty { Name = "PNP ID", Value = WmiHelper.S(d, "PNPDeviceID") });
                audioNode.Children.Add(child);
            }
            tree.Add(audioNode);
        }
    }

    private static string DescribeCpuArch(uint v) => v switch { 0 => "x86", 1 => "MIPS", 2 => "Alpha", 3 => "PowerPC", 5 => "ARM", 6 => "ia64", 9 => "x64", 12 => "ARM64", _ => v.ToString() };
    private static string DescribeMemoryType(uint smbios, uint legacy) { if (smbios != 0) return smbios switch { 20 => "DDR", 21 => "DDR2", 22 => "DDR2 FB-DIMM", 24 => "DDR3", 26 => "DDR4", 30 => "DDR4", 34 => "DDR5", _ => $"Type {smbios}" }; return legacy switch { 1 => "Other", 2 => "Unknown", 3 => "DRAM", 4 => "EDRAM", 5 => "VRAM", 6 => "SRAM", 7 => "RAM", _ => legacy.ToString() }; }
    private static string DescribeFormFactor(uint v) => v switch { 0 => "Unknown", 1 => "Other", 2 => "SIP", 3 => "DIP", 4 => "ZIP", 5 => "SOJ", 6 => "Proprietary", 7 => "SIMM", 8 => "DIMM", 9 => "TSOP", 10 => "PGA", 11 => "RIMM", 12 => "SODIMM", 13 => "SRIMM", 14 => "SMD", 15 => "SSMP", 16 => "QFP", 17 => "TQFP", 18 => "SOIC", 19 => "LCC", 20 => "PLCC", 21 => "BGA", 22 => "FPBGA", 23 => "LGA", _ => v.ToString() };
    private static string DescribeChassis(int v) => v switch { 1 => "Other", 2 => "Unknown", 3 => "Desktop", 4 => "Low Profile Desktop", 5 => "Pizza Box", 6 => "Mini Tower", 7 => "Tower", 8 => "Portable", 9 => "Laptop", 10 => "Notebook", 12 => "Docking Station", 15 => "Space-saving", 17 => "Main System Chassis", 30 => "Tablet", 32 => "Convertible", _ => v.ToString() };
    private static string DescribeDriveType(uint v) => v switch { 0 => "Unknown", 1 => "No Root Directory", 2 => "Removable", 3 => "Fixed", 4 => "Network", 5 => "Compact Disc", 6 => "RAM Disk", _ => v.ToString() };
    private static string ReadRegistry(string path, string value) { try { using var k = Registry.LocalMachine.OpenSubKey(path); return k?.GetValue(value)?.ToString()?.Trim() ?? string.Empty; } catch { return string.Empty; } }
}
