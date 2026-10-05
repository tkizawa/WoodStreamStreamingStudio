using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using WoodStreamStreamingStudio.Models;

namespace WoodStreamStreamingStudio.Services;

/// <summary>
/// 画面/ウィンドウキャプチャを背景とし、Webカメラ映像を右下に縮小（PiP）して
/// 1つのフレームに合成する映像コンポジターサービス
/// </summary>
public class StreamCompositorService : IDisposable
{
    private readonly CameraCaptureService _cameraService;
    private readonly ScreenCaptureService _screenService;

    private CancellationTokenSource? _cts;
    private Task? _compositingTask;
    private readonly object _lockObj = new();

    public bool IsRunning { get; private set; }
    public int CompositingFps { get; private set; }
    public int OutputWidth { get; private set; }
    public int OutputHeight { get; private set; }

    /// <summary>配信映像モード（PiP合成、画面のみ、カメラのみ）</summary>
    public BroadcastMode CurrentMode { get; set; } = BroadcastMode.PictureInPicture;

    /// <summary>WebカメラPiPの縮小スケール (背景幅に対する割合: 例 0.25 = 25%)</summary>
    public double PipScale { get; set; } = 0.25;

    /// <summary>WebカメラPiPの右下マージン (ピクセル)</summary>
    public int PipMargin { get; set; } = 24;

    /// <summary>合成後の最終フレーム到着イベント</summary>
    public event Action<BitmapSource>? CompositeFrameArrived;

    /// <summary>FFmpeg配信用の生映像フレーム (bgr24) 到着イベント (byte[] bgrData, int width, int height)</summary>
    public event Action<byte[], int, int>? RawFrameAvailable;

    /// <summary>合成ステータス更新イベント (fps, width, height)</summary>
    public event Action<int, int, int>? StatusChanged;

    public StreamCompositorService(CameraCaptureService cameraService, ScreenCaptureService screenService)
    {
        _cameraService = cameraService ?? throw new ArgumentNullException(nameof(cameraService));
        _screenService = screenService ?? throw new ArgumentNullException(nameof(screenService));
    }

    /// <summary>
    /// リアルタイム映像合成を開始します (目標 60 FPS)
    /// </summary>
    public void Start()
    {
        Stop();

        lock (_lockObj)
        {
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            IsRunning = true;
            _compositingTask = Task.Run(() => CompositingLoop(token), token);
        }
    }

    /// <summary>
    /// バックグラウンドでのリアルタイム合成ループ
    /// メモリリークを防ぐため、ループ内のすべての Mat オブジェクトは using で確実に解放します。
    /// </summary>
    private void CompositingLoop(CancellationToken token)
    {
        var fpsStopwatch = Stopwatch.StartNew();
        int frameCount = 0;

        // 一時作業用のMatバッファ
        using var backgroundMat = new Mat();
        using var cameraMat = new Mat();

        while (!token.IsCancellationRequested)
        {
            try
            {
                var loopStart = Stopwatch.GetTimestamp();

                // 1. 背景映像（画面/ウィンドウキャプチャ）を取得
                bool hasScreen = _screenService.CopyLatestFrame(backgroundMat);

                // 2. カメラ映像を取得
                bool hasCamera = _cameraService.CopyLatestFrame(cameraMat);

                // 最終合成用フレーム
                using var compositeMat = new Mat();

                switch (CurrentMode)
                {
                    case BroadcastMode.ScreenOnly:
                        // 画面キャプチャのみを配信
                        if (hasScreen && !backgroundMat.Empty())
                        {
                            backgroundMat.CopyTo(compositeMat);
                        }
                        else
                        {
                            DrawWaitingScreen(compositeMat, "SCREEN CAPTURE ONLY", "WAITING FOR SCREEN INPUT...");
                        }
                        break;

                    case BroadcastMode.CameraOnly:
                        // Webカメラ映像のみを全画面配信
                        if (hasCamera && !cameraMat.Empty())
                        {
                            DrawCameraFull(compositeMat, cameraMat);
                        }
                        else
                        {
                            DrawWaitingScreen(compositeMat, "WEBCAM ONLY", "WAITING FOR WEBCAM INPUT...");
                        }
                        break;

                    case BroadcastMode.PictureInPicture:
                    default:
                        // 画面キャプチャ + WebカメラのPiP合成
                        if (hasScreen && !backgroundMat.Empty())
                        {
                            backgroundMat.CopyTo(compositeMat);
                            if (hasCamera && !cameraMat.Empty())
                            {
                                ComposePip(compositeMat, cameraMat);
                            }
                        }
                        else if (hasCamera && !cameraMat.Empty())
                        {
                            DrawCameraFull(compositeMat, cameraMat);
                        }
                        else
                        {
                            DrawWaitingScreen(compositeMat, "WOODSTREAM STREAMING STUDIO", "WAITING FOR INPUT...");
                        }
                        break;
                }

                OutputWidth = compositeMat.Width;
                OutputHeight = compositeMat.Height;

                // 4. 合成結果を WPF の BitmapSource に変換して通知
                var bitmap = compositeMat.ToWriteableBitmap();
                bitmap.Freeze(); // UIスレッドへの安全な受け渡し

                CompositeFrameArrived?.Invoke(bitmap);

                // 4-2. 配信サービスリスナーが存在する場合、raw BGR24 フレームデータを送出
                if (RawFrameAvailable != null && !compositeMat.Empty())
                {
                    using var bgrMat = new Mat();
                    if (compositeMat.Type() == MatType.CV_8UC4)
                    {
                        Cv2.CvtColor(compositeMat, bgrMat, ColorConversionCodes.BGRA2BGR);
                    }
                    else
                    {
                        compositeMat.CopyTo(bgrMat);
                    }

                    int dataSize = bgrMat.Width * bgrMat.Height * 3;
                    var rawBytes = new byte[dataSize];
                    System.Runtime.InteropServices.Marshal.Copy(bgrMat.Data, rawBytes, 0, dataSize);
                    RawFrameAvailable.Invoke(rawBytes, bgrMat.Width, bgrMat.Height);
                }

                // 5. FPS計算
                frameCount++;
                if (fpsStopwatch.ElapsedMilliseconds >= 1000)
                {
                    CompositingFps = (int)(frameCount * 1000.0 / fpsStopwatch.ElapsedMilliseconds);
                    StatusChanged?.Invoke(CompositingFps, OutputWidth, OutputHeight);
                    frameCount = 0;
                    fpsStopwatch.Restart();
                }

                // 目標約 60 FPS (約16.6ms間隔)
                var elapsedMs = (int)((Stopwatch.GetTimestamp() - loopStart) * 1000.0 / Stopwatch.Frequency);
                int waitMs = Math.Clamp(16 - elapsedMs, 1, 16);
                Thread.Sleep(waitMs);
            }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                {
                    Debug.WriteLine($"合成ループエラー: {ex.Message}");
                }
                Thread.Sleep(50);
            }
        }

        IsRunning = false;
    }

    /// <summary>
    /// 背景フレームの右下にカメラ映像を縮小してピクチャーインピクチャー描画します。
    /// すべての中間 Mat オブジェクトは using で確実に破棄します。
    /// </summary>
    private void ComposePip(Mat background, Mat camera)
    {
        // 縮小サイズの計算（背景幅に対する割合、アスペクト比維持）
        int pipWidth = Math.Max(160, (int)(background.Width * PipScale));
        int pipHeight = (int)(pipWidth * ((double)camera.Height / camera.Width));

        // 画面外にはみ出さないよう制限
        pipWidth = Math.Min(pipWidth, background.Width - 30);
        pipHeight = Math.Min(pipHeight, background.Height - 30);

        int x = background.Width - pipWidth - PipMargin;
        int y = background.Height - pipHeight - PipMargin;

        if (x < 0 || y < 0) return;

        // 縮小処理
        using var resizedCam = new Mat();
        Cv2.Resize(camera, resizedCam, new OpenCvSharp.Size(pipWidth, pipHeight), 0, 0, InterpolationFlags.Linear);

        // チャンネル数の整合（BGRA vs BGR）
        using var convertedCam = new Mat();
        if (background.Type() == MatType.CV_8UC4 && resizedCam.Type() == MatType.CV_8UC3)
        {
            Cv2.CvtColor(resizedCam, convertedCam, ColorConversionCodes.BGR2BGRA);
        }
        else if (background.Type() == MatType.CV_8UC3 && resizedCam.Type() == MatType.CV_8UC4)
        {
            Cv2.CvtColor(resizedCam, convertedCam, ColorConversionCodes.BGRA2BGR);
        }
        else
        {
            resizedCam.CopyTo(convertedCam);
        }

        // 背景の右下領域 (ROI) にカメラ映像をコピー
        using var roi = new Mat(background, new Rect(x, y, pipWidth, pipHeight));
        convertedCam.CopyTo(roi);

        // スタイリッシュな境界線（アクセントシアン #00D2FF / BGR: 255, 210, 0）を描画
        Cv2.Rectangle(background, new Rect(x - 2, y - 2, pipWidth + 4, pipHeight + 4),
            new Scalar(255, 210, 0, 255), 2, LineTypes.AntiAlias);
    }

    /// <summary>
    /// カメラ映像を 1920x1080 キャンバスにアスペクト比を維持して全画面描画します
    /// </summary>
    private static void DrawCameraFull(Mat compositeMat, Mat cameraMat)
    {
        compositeMat.Create(1080, 1920, MatType.CV_8UC4);
        compositeMat.SetTo(new Scalar(26, 19, 18, 255)); // #12131A ダークスタジオ背景

        if (cameraMat.Empty() || cameraMat.Width <= 0 || cameraMat.Height <= 0) return;

        double scale = Math.Min(1920.0 / cameraMat.Width, 1080.0 / cameraMat.Height);
        int drawW = Math.Max(1, (int)(cameraMat.Width * scale));
        int drawH = Math.Max(1, (int)(cameraMat.Height * scale));
        int drawX = (1920 - drawW) / 2;
        int drawY = (1080 - drawH) / 2;

        using var resizedCam = new Mat();
        Cv2.Resize(cameraMat, resizedCam, new OpenCvSharp.Size(drawW, drawH), 0, 0, InterpolationFlags.Linear);

        using var convertedCam = new Mat();
        if (resizedCam.Type() == MatType.CV_8UC3)
        {
            Cv2.CvtColor(resizedCam, convertedCam, ColorConversionCodes.BGR2BGRA);
        }
        else
        {
            resizedCam.CopyTo(convertedCam);
        }

        using var roi = new Mat(compositeMat, new Rect(drawX, drawY, drawW, drawH));
        convertedCam.CopyTo(roi);
    }

    /// <summary>
    /// スタジオ待機画面を描画します
    /// </summary>
    private static void DrawWaitingScreen(Mat compositeMat, string title, string subtitle)
    {
        compositeMat.Create(1080, 1920, MatType.CV_8UC4);
        compositeMat.SetTo(new Scalar(26, 19, 18, 255)); // #12131A

        // グリッド線描画
        for (int x = 0; x < 1920; x += 120)
        {
            Cv2.Line(compositeMat, new OpenCvSharp.Point(x, 0), new OpenCvSharp.Point(x, 1080), new Scalar(48, 42, 35, 255), 1);
        }
        for (int y = 0; y < 1080; y += 120)
        {
            Cv2.Line(compositeMat, new OpenCvSharp.Point(0, y), new OpenCvSharp.Point(1920, y), new Scalar(48, 42, 35, 255), 1);
        }

        // 待機テキスト
        Cv2.PutText(compositeMat, title, new OpenCvSharp.Point(520, 520),
            HersheyFonts.HersheyComplex, 1.4, new Scalar(255, 210, 0, 255), 2, LineTypes.AntiAlias);
        Cv2.PutText(compositeMat, subtitle, new OpenCvSharp.Point(680, 580),
            HersheyFonts.HersheySimplex, 1.0, new Scalar(160, 160, 160, 255), 2, LineTypes.AntiAlias);
    }

    /// <summary>
    /// 合成処理を停止します
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
                    _compositingTask?.Wait(500);
                }
                catch
                {
                    // キャンセル例外を無視
                }
                _cts.Dispose();
                _cts = null;
                _compositingTask = null;
            }
            IsRunning = false;
            CompositingFps = 0;
            StatusChanged?.Invoke(0, OutputWidth, OutputHeight);
        }
    }

    public void Dispose()
    {
        Stop();
    }
}
