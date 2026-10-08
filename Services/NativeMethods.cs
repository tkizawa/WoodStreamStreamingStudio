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

    [DllImport("gdi32.dll", CharSet = CharSet.Auto)]
    public static extern nint CreateDC(string? lpszDriver, string? lpszDevice, string? lpszOutput, nint lpInitData);

    [DllImport("gdi32.dll")]
    public static extern bool GdiFlush();

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

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public short biPlanes;
        public short biBitCount;
        public int biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
    }

    [DllImport("gdi32.dll")]
    public static extern int GetDIBits(nint hdc, nint hbmp, uint uStartScan, uint cScanLines, nint lpvBits, ref BITMAPINFOHEADER lpbi, uint uUsage);

    public const uint PW_CLIENTONLY = 0x00000001;
    public const uint PW_RENDERFULLCONTENT = 0x00000002;

    [DllImport("user32.dll")]
    public static extern bool PrintWindow(nint hwnd, nint hdcBlt, uint nFlags);

    [DllImport("user32.dll")]
    public static extern bool IsIconic(nint hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsWindow(nint hWnd);

    public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(nint hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

    /// <summary>
    /// ウィンドウの正確な可視領域（不可視のマージンや影を除いた実際の境界）を取得します
    /// </summary>
    public static bool GetVisibleWindowRect(nint hWnd, out RECT rect)
    {
        int hr = DwmGetWindowAttribute(hWnd, DWMWA_EXTENDED_FRAME_BOUNDS, out rect, Marshal.SizeOf<RECT>());
        if (hr == 0 && rect.Width > 0 && rect.Height > 0)
        {
            return true;
        }
        return GetWindowRect(hWnd, out rect);
    }

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

    public const int ENUM_CURRENT_SETTINGS = -1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmDeviceName;
        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public int dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
    }

    [DllImport("user32.dll", CharSet = CharSet.Ansi)]
    public static extern bool EnumDisplaySettings(string? lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);

    /// <summary>
    /// 全てのモニタ（ディスプレイ）を取得します（DPI仮想化の影響を受けない物理ピクセル解像度・座標を取得）
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

                // DPI仮想化の影響を回避するため、EnumDisplaySettingsから物理ピクセル解像度および座標を取得
                var dm = new DEVMODE();
                dm.dmSize = (short)Marshal.SizeOf<DEVMODE>();
                Rectangle bounds;

                if (EnumDisplaySettings(mi.szDevice, ENUM_CURRENT_SETTINGS, ref dm) && dm.dmPelsWidth > 0 && dm.dmPelsHeight > 0)
                {
                    bounds = new Rectangle(dm.dmPositionX, dm.dmPositionY, dm.dmPelsWidth, dm.dmPelsHeight);
                }
                else
                {
                    bounds = mi.rcMonitor.ToRectangle();
                }

                var title = $"Display {displayIndex} ({bounds.Width}x{bounds.Height}){(isPrimary ? " [Primary]" : "")}";

                list.Add(new CaptureSourceInfo
                {
                    SourceType = CaptureSourceType.Display,
                    Title = title,
                    Handle = hMonitor,
                    DeviceName = mi.szDevice,
                    Bounds = bounds
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
                title == "Windows Shell Experience Host")
            {
                return true;
            }

            if (GetVisibleWindowRect(hWnd, out var rect))
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
