using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using WoodStreamStreamingStudio.Models;

namespace WoodStreamStreamingStudio.Services;

/// <summary>
/// ディスプレイやウィンドウのキャプチャに必要な Win32 API 宣言
/// </summary>
public static class NativeMethods
{
    public const int SRCCOPY = 0x00CC0020;
    public const int CAPTUREBLT = 0x40000000;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public int Width => Right - Left;
        public int Height => Bottom - Top;

        public Rectangle ToRectangle() => new(Left, Top, Width, Height);
    }

    [DllImport("user32.dll")]
    public static extern nint GetDesktopWindow();

    [DllImport("user32.dll")]
    public static extern nint GetDC(nint hWnd);

    [DllImport("user32.dll")]
    public static extern int ReleaseDC(nint hWnd, nint hDC);

    [DllImport("gdi32.dll")]
    public static extern nint CreateCompatibleDC(nint hDC);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteDC(nint hDC);

    [DllImport("gdi32.dll")]
    public static extern nint CreateCompatibleBitmap(nint hDC, int nWidth, int nHeight);

    [DllImport("gdi32.dll")]
    public static extern nint SelectObject(nint hDC, nint hObject);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteObject(nint hObject);

    [DllImport("gdi32.dll")]
    public static extern bool BitBlt(nint hdcDest, int nXDest, int nYDest, int nWidth, int nHeight, nint hdcSrc, int nXSrc, int nYSrc, int dwRop);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(nint hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(nint hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    public static extern int GetWindowTextLength(nint hWnd);

    public delegate bool EnumWindowsProc(nint hWnd, nint lParam);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

    public delegate bool MonitorEnumProc(nint hMonitor, nint hdcMonitor, ref RECT lprcMonitor, nint dwData);

    [DllImport("user32.dll")]
    public static extern bool EnumDisplayMonitors(nint hdc, nint lprcClip, MonitorEnumProc lpfnEnum, nint dwData);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public int dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern bool GetMonitorInfo(nint hMonitor, ref MONITORINFOEX lpmi);

    /// <summary>
    /// 全てのモニタ（ディスプレイ）を取得します
    /// </summary>
    public static List<CaptureSourceInfo> GetDisplaySources()
    {
        var list = new List<CaptureSourceInfo>();
        int displayIndex = 1;

        EnumDisplayMonitors(nint.Zero, nint.Zero, (nint hMonitor, nint hdcMonitor, ref RECT lprcMonitor, nint dwData) =>
        {
            var mi = new MONITORINFOEX();
            mi.cbSize = Marshal.SizeOf(typeof(MONITORINFOEX));
            if (GetMonitorInfo(hMonitor, ref mi))
            {
                var isPrimary = (mi.dwFlags & 1) != 0;
                var rect = mi.rcMonitor.ToRectangle();
                var title = $"Display {displayIndex} ({rect.Width}x{rect.Height}){(isPrimary ? " [Primary]" : "")}";

                list.Add(new CaptureSourceInfo
                {
                    SourceType = CaptureSourceType.Display,
                    Title = title,
                    Handle = hMonitor,
                    Bounds = rect
                });
                displayIndex++;
            }
            return true;
        }, nint.Zero);

        return list;
    }

    /// <summary>
    /// キャプチャ可能な表示中ウィンドウを取得します
    /// </summary>
    public static List<CaptureSourceInfo> GetWindowSources(nint currentAppHwnd)
    {
        var list = new List<CaptureSourceInfo>();

        EnumWindows((hWnd, lParam) =>
        {
            if (hWnd == currentAppHwnd) return true;
            if (!IsWindowVisible(hWnd)) return true;

            int length = GetWindowTextLength(hWnd);
            if (length == 0) return true;

            var sb = new StringBuilder(length + 1);
            GetWindowText(hWnd, sb, sb.Capacity);
            var title = sb.ToString();

            // 特殊なシェルウィンドウや空のタイトルを除外
            if (string.IsNullOrWhiteSpace(title) ||
                title == "Program Manager" ||
                title == "Windows Shell Experience Host" ||
                title == "Settings")
            {
                return true;
            }

            if (GetWindowRect(hWnd, out var rect))
            {
                if (rect.Width > 100 && rect.Height > 100)
                {
                    list.Add(new CaptureSourceInfo
                    {
                        SourceType = CaptureSourceType.Window,
                        Title = $"Window: {title}",
                        Handle = hWnd,
                        Bounds = rect.ToRectangle()
                    });
                }
            }

            return true;
        }, nint.Zero);

        return list;
    }
}
