using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System;
using System.IO;
using System.Runtime.InteropServices;
using Windows.Graphics;
using WinRT.Interop;

namespace HardwareInspectorWin.Helpers;

public static class WindowHelper
{
    private const double DefaultDpi = 96.0;

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr windowHandle);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr LoadImage(IntPtr hInst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

    public static double GetScaleRatio(Window window)
    {
        IntPtr windowHandle = WindowNative.GetWindowHandle(window);
        uint dpi = GetDpiForWindow(windowHandle);
        return dpi > 0 ? dpi / DefaultDpi : 1.0;
    }

    public static void ResizeForScale(Window window, int logicalWidth, int logicalHeight)
    {
        double scaleRatio = GetScaleRatio(window);
        window.AppWindow.Resize(new SizeInt32(
            (int)Math.Round(logicalWidth * scaleRatio),
            (int)Math.Round(logicalHeight * scaleRatio)));
    }

    public static void SetAppIcon(Window window, string relativeIconPath = "Assets/AppIcon.ico")
    {
        try
        {
            IntPtr hwnd = WindowNative.GetWindowHandle(window);
            WindowId windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
            AppWindow appWindow = AppWindow.GetFromWindowId(windowId);

            string normPath = relativeIconPath.Replace('/', Path.DirectorySeparatorChar);
            string[] searchPaths = new[]
            {
                Path.Combine(AppContext.BaseDirectory, normPath),
                Path.Combine(AppContext.BaseDirectory, "Assets", Path.GetFileName(normPath)),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, normPath),
                Path.GetFullPath(normPath)
            };

            string? iconPath = null;
            foreach (var candidate in searchPaths)
            {
                if (File.Exists(candidate))
                {
                    iconPath = candidate;
                    break;
                }
            }

            if (iconPath != null)
            {
                if (appWindow != null)
                {
                    try { appWindow.SetIcon(iconPath); } catch { }
                }

                if (hwnd != IntPtr.Zero)
                {
                    try
                    {
                        IntPtr hIconSmall = LoadImage(IntPtr.Zero, iconPath, 1, 16, 16, 0x0010);
                        IntPtr hIconBig = LoadImage(IntPtr.Zero, iconPath, 1, 32, 32, 0x0010);
                        if (hIconSmall != IntPtr.Zero)
                            SendMessage(hwnd, 0x0080, (IntPtr)0, hIconSmall);
                        if (hIconBig != IntPtr.Zero)
                            SendMessage(hwnd, 0x0080, (IntPtr)1, hIconBig);
                    }
                    catch { }
                }
            }
        }
        catch { }
    }

    public static void Maximize(Window window)
    {
        try
        {
            if (window?.AppWindow?.Presenter is OverlappedPresenter presenter)
            {
                presenter.Maximize();
            }
        }
        catch { }
    }
}
