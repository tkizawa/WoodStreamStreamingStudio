using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using Windows.Devices.Enumeration;
using WoodStreamStreamingStudio.Models;

namespace WoodStreamStreamingStudio.Services;

/// <summary>
/// OpenCvSharpを使用したWebカメラのキャプチャおよびプレビュー管理サービス
/// </summary>
public class CameraCaptureService : IDisposable
{
    private VideoCapture? _capture;
    private CancellationTokenSource? _cts;
    private Task? _captureTask;
    private readonly object _lockObj = new();

    public bool IsRunning { get; private set; }
    public bool Mirror { get; set; } = true;
    public int CurrentFps { get; private set; }
    public int FrameWidth { get; private set; }
    public int FrameHeight { get; private set; }

    private readonly Mat _latestFrame = new();
    private readonly object _frameLock = new();

    /// <summary>新しいカメラフレームが到着したときに発生するイベント</summary>
    public event Action<BitmapSource>? FrameArrived;

    /// <summary>キャプチャ状態（FPSや解像度、ステータス）が変化したときに発生するイベント</summary>
    public event Action<int, int, int>? StatusChanged;

    /// <summary>エラー発生時のイベント</summary>
    public event Action<string>? ErrorOccurred;

    /// <summary>
    /// 最新のカメラフレーム（左右反転反映済み）をターゲットMatにコピーします。
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
    /// システムに接続されている利用可能なWebカメラを列挙します
    /// </summary>
    public async Task<List<CameraDeviceInfo>> GetCameraDevicesAsync()
    {
        var devices = new List<CameraDeviceInfo>();

        try
        {
            // Windows.Devices.Enumeration を使用してカメラのフレンドリー名を取得
            var winDevices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
            int index = 0;
            foreach (var dev in winDevices)
            {
                if (dev.IsEnabled)
                {
                    devices.Add(new CameraDeviceInfo
                    {
                        Index = index,
                        Name = string.IsNullOrWhiteSpace(dev.Name) ? $"Camera {index}" : dev.Name,
                        Id = dev.Id
                    });
                    index++;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Windows.Devices.Enumeration でのカメラ列挙失敗: {ex.Message}");
        }

        // カメラが1件も取得できなかった場合は OpenCV インデックスでフォールバック検出
        if (devices.Count == 0)
        {
            await Task.Run(() =>
            {
                for (int i = 0; i < 4; i++)
                {
                    try
                    {
                        using var testCap = new VideoCapture(i, VideoCaptureAPIs.DSHOW);
                        if (testCap.IsOpened())
                        {
                            devices.Add(new CameraDeviceInfo
                            {
                                Index = i,
                                Name = $"Camera {i} (DirectShow)",
                                Id = i.ToString()
                            });
                        }
                    }
                    catch
                    {
                        // 検出失敗時はスキップ
                    }
                }
            });
        }

        return devices;
    }

    /// <summary>
    /// 指定されたインデックスのカメラでキャプチャを開始します
    /// </summary>
    public void Start(int deviceIndex)
    {
        Stop();

        lock (_lockObj)
        {
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            IsRunning = true;
            _captureTask = Task.Run(() => CaptureLoop(deviceIndex, token), token);
        }
    }

    /// <summary>
    /// バックグラウンドでのカメラフレーム取得ループ
    /// </summary>
    private void CaptureLoop(int deviceIndex, CancellationToken token)
    {
        try
        {
            // DirectShowバックエンドを優先使用（Windows環境で最も安定）
            _capture = new VideoCapture(deviceIndex, VideoCaptureAPIs.DSHOW);

            if (!_capture.IsOpened())
            {
                // フォールバック
                _capture.Dispose();
                _capture = new VideoCapture(deviceIndex);
            }

            if (!_capture.IsOpened())
            {
                ErrorOccurred?.Invoke($"カメラ (Index {deviceIndex}) を開くことができませんでした。");
                IsRunning = false;
                return;
            }

            // 解像度などの初期取得
            FrameWidth = (int)_capture.Get(VideoCaptureProperties.FrameWidth);
            FrameHeight = (int)_capture.Get(VideoCaptureProperties.FrameHeight);

            using var frame = new Mat();
            var fpsStopwatch = Stopwatch.StartNew();
            int frameCount = 0;

            while (!token.IsCancellationRequested)
            {
                if (!_capture.Read(frame) || frame.Empty())
                {
                    Thread.Sleep(10);
                    continue;
                }

                // 左右反転（ミラー表示）
                if (Mirror)
                {
                    Cv2.Flip(frame, frame, FlipMode.Y);
                }

                FrameWidth = frame.Width;
                FrameHeight = frame.Height;

                // 合成サービス用に最新フレームを保管
                lock (_frameLock)
                {
                    frame.CopyTo(_latestFrame);
                }

                // OpenCvSharp の WriteableBitmapConverter を使用して WPF 用 BitmapSource に変換
                var bitmap = frame.ToWriteableBitmap();
                bitmap.Freeze(); // UIスレッドへ渡すためにFreeze

                FrameArrived?.Invoke(bitmap);

                // FPS計算
                frameCount++;
                if (fpsStopwatch.ElapsedMilliseconds >= 1000)
                {
                    CurrentFps = (int)(frameCount * 1000.0 / fpsStopwatch.ElapsedMilliseconds);
                    StatusChanged?.Invoke(CurrentFps, FrameWidth, FrameHeight);
                    frameCount = 0;
                    fpsStopwatch.Restart();
                }

                // 目標約30fps (33ms周期)
                Thread.Sleep(30);
            }
        }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested)
            {
                ErrorOccurred?.Invoke($"カメラ処理中にエラーが発生しました: {ex.Message}");
            }
        }
        finally
        {
            lock (_lockObj)
            {
                _capture?.Release();
                _capture?.Dispose();
                _capture = null;
                IsRunning = false;
            }
        }
    }

    /// <summary>
    /// カメラキャプチャを停止します
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
                    // キャンセル時の例外を無視
                }
                _cts.Dispose();
                _cts = null;
                _captureTask = null;
            }
            IsRunning = false;
            CurrentFps = 0;
            StatusChanged?.Invoke(0, FrameWidth, FrameHeight);
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
