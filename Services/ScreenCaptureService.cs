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
    /// バックグラウンドでの画面キャプチャループ (BitBlt)
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
                nint srcHwnd;

                if (source.SourceType == CaptureSourceType.Window)
                {
                    srcHwnd = source.Handle;
                    if (!NativeMethods.IsWindowVisible(srcHwnd))
                    {
                        Thread.Sleep(100);
                        continue;
                    }
                    if (!NativeMethods.GetWindowRect(srcHwnd, out var r))
                    {
                        Thread.Sleep(100);
                        continue;
                    }
                    targetRect = r.ToRectangle();
                }
                else
                {
                    // ディスプレイキャプチャ
                    srcHwnd = NativeMethods.GetDesktopWindow();
                    targetRect = source.Bounds;
                }

                int width = Math.Max(1, targetRect.Width);
                int height = Math.Max(1, targetRect.Height);
                CaptureWidth = width;
                CaptureHeight = height;

                // Win32 GDI による画面取り込み
                nint hSrcDC = NativeMethods.GetDC(srcHwnd);
                nint hDestDC = NativeMethods.CreateCompatibleDC(hSrcDC);
                nint hBitmap = NativeMethods.CreateCompatibleBitmap(hSrcDC, width, height);
                nint hOldBitmap = NativeMethods.SelectObject(hDestDC, hBitmap);

                int srcX = source.SourceType == CaptureSourceType.Window ? 0 : targetRect.Left;
                int srcY = source.SourceType == CaptureSourceType.Window ? 0 : targetRect.Top;

                NativeMethods.BitBlt(
                    hDestDC, 0, 0, width, height,
                    hSrcDC, srcX, srcY,
                    NativeMethods.SRCCOPY | NativeMethods.CAPTUREBLT);

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

                // GDIリソースの安全な解放
                NativeMethods.SelectObject(hDestDC, hOldBitmap);
                NativeMethods.DeleteObject(hBitmap);
                NativeMethods.DeleteDC(hDestDC);
                NativeMethods.ReleaseDC(srcHwnd, hSrcDC);

                // 合成サービス用に最新フレームを保管
                lock (_frameLock)
                {
                    frameMat.CopyTo(_latestFrame);
                }

                // プレビュー表示用 BitmapSource を生成
                var bmpSource = frameMat.ToWriteableBitmap();
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
