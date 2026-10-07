using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using WoodStreamStreamingStudio.Models;

namespace WoodStreamStreamingStudio.Services;

/// <summary>
/// 指定したディスプレイまたはウィンドウの画面をキャプチャするサービス
/// </summary>
public class ScreenCaptureService : IDisposable
{
    private CancellationTokenSource? _cts;
    private Task? _captureTask;
    private readonly object _lockObj = new();

    public bool IsRunning { get; private set; }
    public int CurrentFps { get; private set; }
    public int CaptureWidth { get; private set; }
    public int CaptureHeight { get; private set; }

    private readonly Mat _latestFrame = new();
    private readonly object _frameLock = new();

    /// <summary>画面フレームが生成されたときに発生するイベント</summary>
    public event Action<BitmapSource>? FrameArrived;

    /// <summary>キャプチャ状態（FPSや解像度）の更新イベント</summary>
    public event Action<int, int, int>? StatusChanged;

    /// <summary>エラー発生時のイベント</summary>
    public event Action<string>? ErrorOccurred;

    /// <summary>
    /// 最新の画面フレーム（Mat）をターゲットMatにコピーします。
    /// </summary>
    /// <returns>フレームが存在しコピー成功した場合はtrue</returns>
    public bool CopyLatestFrame(Mat targetMat)
    {
        lock (_frameLock)
        {
            if (_latestFrame.Empty()) return false;
            _latestFrame.CopyTo(targetMat);
            return true;
        }
    }

    /// <summary>
    /// キャプチャ可能なソース（ディスプレイおよびアクティブウィンドウ）を取得します
    /// </summary>
    public List<CaptureSourceInfo> GetAvailableSources(nint currentAppHwnd)
    {
        var sources = new List<CaptureSourceInfo>();

        try
        {
            // ディスプレイ（モニタ）一覧
            var displays = NativeMethods.GetDisplaySources();
            sources.AddRange(displays);

            // 表示中ウィンドウ一覧
            var windows = NativeMethods.GetWindowSources(currentAppHwnd);
            sources.AddRange(windows);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"キャプチャ対象の列挙失敗: {ex.Message}");
        }

        return sources;
    }

    /// <summary>
    /// 指定されたソースの画面キャプチャを開始します
    /// </summary>
    public void Start(CaptureSourceInfo source)
    {
        Stop();

        lock (_lockObj)
        {
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            IsRunning = true;
            _captureTask = Task.Run(() => CaptureLoop(source, token), token);
        }
    }

    /// <summary>
    /// バックグラウンドでの画面キャプチャループ
    /// PrintWindow (PW_RENDERFULLCONTENT) と デスクトップ BitBlt (CAPTUREBLT) のハイブリッド方式により、
    /// UWPアプリ（Windows「設定」アプリ等）やGPUアクセラレーション有効ウィンドウも確実にキャプチャします。
    /// </summary>
    private void CaptureLoop(CaptureSourceInfo source, CancellationToken token)
    {
        var fpsStopwatch = Stopwatch.StartNew();
        int frameCount = 0;

        while (!token.IsCancellationRequested)
        {
            try
            {
                Rectangle targetRect;
                nint srcHwnd = nint.Zero;

                if (source.SourceType == CaptureSourceType.Window)
                {
                    srcHwnd = source.Handle;
                    if (!NativeMethods.IsWindow(srcHwnd) || !NativeMethods.IsWindowVisible(srcHwnd) || NativeMethods.IsIconic(srcHwnd))
                    {
                        Thread.Sleep(100);
                        continue;
                    }
                    if (!NativeMethods.GetVisibleWindowRect(srcHwnd, out var r))
                    {
                        Thread.Sleep(100);
                        continue;
                    }
                    targetRect = r.ToRectangle();
                }
                else
                {
                    // ディスプレイキャプチャ
                    targetRect = source.Bounds;
                }

                if (targetRect.Width <= 10 || targetRect.Height <= 10)
                {
                    Thread.Sleep(100);
                    continue;
                }

                int width = Math.Max(1, targetRect.Width);
                int height = Math.Max(1, targetRect.Height);
                CaptureWidth = width;
                CaptureHeight = height;

                // Win32 GDI による画面取り込み
                // デスクトップDCを基準に互換DCとビットマップを作成
                nint hDesktopDC = NativeMethods.GetDC(nint.Zero);
                nint hDestDC = NativeMethods.CreateCompatibleDC(hDesktopDC);
                nint hBitmap = NativeMethods.CreateCompatibleBitmap(hDesktopDC, width, height);
                nint hOldBitmap = NativeMethods.SelectObject(hDestDC, hBitmap);

                bool capturedWithPrintWindow = false;

                if (source.SourceType == CaptureSourceType.Window)
                {
                    // 方法1: PrintWindow (PW_RENDERFULLCONTENT: 0x00000002)
                    // 他のウィンドウに重なっていてもウィンドウ単体の描画を取得可能
                    try
                    {
                        capturedWithPrintWindow = NativeMethods.PrintWindow(srcHwnd, hDestDC, NativeMethods.PW_RENDERFULLCONTENT);
                    }
                    catch
                    {
                        capturedWithPrintWindow = false;
                    }
                }

                // 方法2 (またはディスプレイキャプチャ): デスクトップ画面座標から直接 BitBlt でキャプチャ
                // UWPアプリ（Windows「設定」等）やPrintWindow非対応のGPUレンダリングウィンドウも
                // 画面上に表示されているピクセルを100%確実にキャプチャ
                if (!capturedWithPrintWindow)
                {
                    NativeMethods.BitBlt(
                        hDestDC, 0, 0, width, height,
                        hDesktopDC, targetRect.Left, targetRect.Top,
                        NativeMethods.SRCCOPY | NativeMethods.CAPTUREBLT);
                }

                // DIBits 経由で OpenCvSharp の Mat (CV_8UC4) を直接生成
                var bmi = new NativeMethods.BITMAPINFOHEADER
                {
                    biSize = Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>(),
                    biWidth = width,
                    biHeight = -height, // トップダウン
                    biPlanes = 1,
                    biBitCount = 32, // BGRA
                    biCompression = 0
                };

                using var frameMat = new Mat(height, width, MatType.CV_8UC4);
                NativeMethods.GetDIBits(hDestDC, hBitmap, 0, (uint)height, frameMat.Data, ref bmi, 0);

                // もし PrintWindow で「成功」と返ってきたが中身が真っ黒だった場合は、
                // デスクトップBitBltにフォールバックして再取得
                if (capturedWithPrintWindow && IsBlackFrame(frameMat))
                {
                    NativeMethods.BitBlt(
                        hDestDC, 0, 0, width, height,
                        hDesktopDC, targetRect.Left, targetRect.Top,
                        NativeMethods.SRCCOPY | NativeMethods.CAPTUREBLT);
                    NativeMethods.GetDIBits(hDestDC, hBitmap, 0, (uint)height, frameMat.Data, ref bmi, 0);
                }

                // GDIリソースの安全な解放
                NativeMethods.SelectObject(hDestDC, hOldBitmap);
                NativeMethods.DeleteObject(hBitmap);
                NativeMethods.DeleteDC(hDestDC);
                NativeMethods.ReleaseDC(nint.Zero, hDesktopDC);

                // BGR24 (CV_8UC3) に統一変換してメモリ効率およびプレビュー・合成色空間の整合性を確保
                using var bgrMat = new Mat();
                Cv2.CvtColor(frameMat, bgrMat, ColorConversionCodes.BGRA2BGR);

                // 合成サービス用に最新フレームを保管
                lock (_frameLock)
                {
                    bgrMat.CopyTo(_latestFrame);
                }

                // プレビュー表示用 BitmapSource (Bgr24) を生成
                var bmpSource = bgrMat.ToWriteableBitmap();
                bmpSource.Freeze();

                FrameArrived?.Invoke(bmpSource);

                // FPS計算
                frameCount++;
                if (fpsStopwatch.ElapsedMilliseconds >= 1000)
                {
                    CurrentFps = (int)(frameCount * 1000.0 / fpsStopwatch.ElapsedMilliseconds);
                    StatusChanged?.Invoke(CurrentFps, CaptureWidth, CaptureHeight);
                    frameCount = 0;
                    fpsStopwatch.Restart();
                }

                // 目標約30fps (33ms周期)
                Thread.Sleep(33);
            }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                {
                    ErrorOccurred?.Invoke($"画面キャプチャ中にエラーが発生しました: {ex.Message}");
                }
                Thread.Sleep(200);
            }
        }

        IsRunning = false;
    }

    /// <summary>
    /// キャプチャしたフレームが真っ黒（全画素値が0近傍）かどうかをサンプリング判定します
    /// </summary>
    private static bool IsBlackFrame(Mat mat)
    {
        if (mat.Empty() || mat.Data == nint.Zero) return true;

        int totalPixels = mat.Width * mat.Height;
        int step = Math.Max(1, totalPixels / 100);

        for (int i = 0; i < totalPixels; i += step)
        {
            int offset = i * 4;
            byte b = Marshal.ReadByte(mat.Data, offset);
            byte g = Marshal.ReadByte(mat.Data, offset + 1);
            byte r = Marshal.ReadByte(mat.Data, offset + 2);
            if (b > 5 || g > 5 || r > 5)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 画面キャプチャを停止します
    /// </summary>
    public void Stop()
    {
        lock (_lockObj)
        {
            if (_cts != null)
            {
                _cts.Cancel();
                try
                {
                    _captureTask?.Wait(500);
                }
                catch
                {
                    // キャンセル例外を無視
                }
                _cts.Dispose();
                _cts = null;
                _captureTask = null;
            }
            IsRunning = false;
            CurrentFps = 0;
            StatusChanged?.Invoke(0, CaptureWidth, CaptureHeight);
        }
    }

    public void Dispose()
    {
        Stop();
        lock (_frameLock)
        {
            _latestFrame.Dispose();
        }
    }
}
