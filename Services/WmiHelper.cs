using System;
using System.Collections.Generic;
using System.Management;

namespace HardwareInspectorWin.Services;

internal static class WmiHelper
{
    public static List<Dictionary<string, object>> Query(string query, string scope = @"root\CIMV2")
    {
        var rows = new List<Dictionary<string, object>>();
        using var searcher = new ManagementObjectSearcher(scope, query);
        using var collection = searcher.Get();
        foreach (ManagementObject obj in collection)
        {
            var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in obj.Properties)
            {
                if (prop.Value != null) row[prop.Name] = prop.Value;
            }
            rows.Add(row);
        }
        return rows;
    }

    public static string S(IReadOnlyDictionary<string, object> row, string prop, string fallback = "")
    {
        if (row.TryGetValue(prop, out var value) && value != null)
        {
            var text = value.ToString() ?? string.Empty;
            return text.Trim();
        }
        return fallback;
    }

    public static string? SN(IReadOnlyDictionary<string, object> row, string prop)
    {
        var text = S(row, prop);
        return text.Length == 0 ? null : text;
    }

    public static ulong U(IReadOnlyDictionary<string, object> row, string prop, ulong fallback = 0)
    {
        if (row.TryGetValue(prop, out var value) && value != null)
        {
            if (value is ulong u) return u;
            if (value is uint ui) return ui;
            if (value is int i && i >= 0) return (ulong)i;
            if (value is long l && l >= 0) return (ulong)l;
            if (value is ushort us) return us;
            if (ulong.TryParse(value.ToString(), out var parsed)) return parsed;
        }
        return fallback;
    }

    public static int I(IReadOnlyDictionary<string, object> row, string prop, int fallback = 0)
    {
        if (row.TryGetValue(prop, out var value) && value != null)
        {
            if (value is int i) return i;
            if (value is uint ui) return (int)ui;
            if (int.TryParse(value.ToString(), out var parsed)) return parsed;
        }
        return fallback;
    }

    public static uint UI(IReadOnlyDictionary<string, object> row, string prop, uint fallback = 0)
    {
        if (row.TryGetValue(prop, out var value) && value != null)
        {
            if (value is uint ui) return ui;
            if (value is int i && i >= 0) return (uint)i;
            if (uint.TryParse(value.ToString(), out var parsed)) return parsed;
        }
        return fallback;
    }

    public static DateTime? Dtm(IReadOnlyDictionary<string, object> row, string prop)
    {
        if (!row.TryGetValue(prop, out var value) || value == null) return null;
        var text = value.ToString();
        if (string.IsNullOrEmpty(text)) return null;
        if (value is DateTime already) return already;
        try { return ManagementDateTimeConverter.ToDateTime(text); } catch { return null; }
    }

    public static string[] Arr(IReadOnlyDictionary<string, object> row, string prop)
    {
        if (row.TryGetValue(prop, out var value))
        {
            if (value is string[] arr) return arr;
            if (value is IEnumerable<object> seq)
            {
                var list = new List<string>();
                foreach (var item in seq) list.Add(item?.ToString() ?? string.Empty);
                return list.ToArray();
            }
        }
        return Array.Empty<string>();
    }

    public static string ToIso(DateTime? time) => time.HasValue ? time.Value.ToString("yyyy-MM-dd HH:mm:ss") : string.Empty;

    public static string FormatBytes(ulong bytes)
    {
        if (bytes == 0) return "—";
        double gib = bytes / (1024.0 * 1024 * 1024);
        if (gib >= 1) return $"{gib:0.##} GiB ({bytes:N0} bytes)";
        double mib = bytes / (1024.0 * 1024);
        return $"{mib:0.##} MiB";
    }

    public static string FormatGiB(ulong bytes)
    {
        if (bytes == 0) return "—";
        double gib = bytes / (1024.0 * 1024 * 1024);
        return $"{gib:0.0} GiB";
    }
    public static string FormatDecimalGB(ulong bytes)
    {
        if (bytes == 0) return "—";
        return $"{bytes / 1000000000.0:0.0} GB";
    }
}
