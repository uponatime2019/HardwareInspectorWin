using System;
using System.Diagnostics;
using System.IO;

namespace HardwareInspectorWin.Services;

public static class AppLogger
{
    private static readonly object Gate = new();
    private static string? _sessionFile;

    public static string LogDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HardwareInspectorWin", "logs");

    private static string SessionFile()
    {
        if (_sessionFile == null)
        {
            var dir = LogDirectory;
            Directory.CreateDirectory(dir);
            _sessionFile = Path.Combine(dir, $"app_session_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
        }
        return _sessionFile;
    }

    public static void Log(string message)
    {
        try
        {
            lock (Gate)
            {
                File.AppendAllText(SessionFile(), $"{DateTime.Now:HH:mm:ss.fff}  {message}{Environment.NewLine}");
            }
        }
        catch { }
    }

    public static void LogException(Exception ex, string context)
    {
        Log($"[EXCEPTION:{context}] {ex.GetType().Name}: {ex.Message}");
        Log($"[EXCEPTION:{context}] stack: {ex.StackTrace}");
        Debug.WriteLine($"[HardwareInspectorWin:{context}] {ex}");
    }
}
