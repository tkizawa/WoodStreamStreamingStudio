using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace WoodStreamStreamingStudio.Services;

/// <summary>
/// FFmpegプロセスを使用して、合成映像とマイク音声をYouTubeへRTMP配信するサービス
/// </summary>
public class RtmpStreamService : IDisposable
{
    private Process? _ffmpegProcess;
    private NamedPipeServerStream? _audioPipeServer;
    private CancellationTokenSource? _cts;

    private Channel<byte[]>? _videoChannel;
    private Channel<byte[]>? _audioChannel;

    private Task? _videoWriteTask;
    private Task? _audioWriteTask;

    private readonly object _lockObj = new();
    private Stopwatch? _streamingTimer;

    public bool IsStreaming { get; private set; }
    public TimeSpan StreamingDuration => _streamingTimer?.Elapsed ?? TimeSpan.Zero;

    /// <summary>配信ステータス更新イベント</summary>
    public event Action<string>? StatusChanged;

    /// <summary>エラー発生時イベント</summary>
    public event Action<string>? ErrorOccurred;

    /// <summary>
    /// ffmpeg.exe の実行可能パスを取得します
    /// アプリケーションフォルダ、プロジェクトフォルダ、PATH環境変数の順に探索します
    /// </summary>
    public static string? FindFfmpegPath()
    {
        // 1. アプリケーションディレクトリ (AppDomain.CurrentDomain.BaseDirectory)
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var pathInBase = Path.Combine(baseDir, "ffmpeg.exe");
        if (File.Exists(pathInBase)) return pathInBase;

        // 2. カレントディレクトリ
        var pathInCurrent = Path.Combine(Directory.GetCurrentDirectory(), "ffmpeg.exe");
        if (File.Exists(pathInCurrent)) return pathInCurrent;

        // 3. プロジェクトルート (開発環境)
        var projectRoot = @"D:\Dev\WoodStreamStreamingStudio";
        var pathInProject = Path.Combine(projectRoot, "ffmpeg.exe");
        if (File.Exists(pathInProject)) return pathInProject;

        // 4. PATH 環境変数
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathEnv))
        {
            var paths = pathEnv.Split(Path.PathSeparator);
            foreach (var dir in paths)
            {
                try
                {
                    var fullPath = Path.Combine(dir, "ffmpeg.exe");
                    if (File.Exists(fullPath)) return fullPath;
                }
                catch
                {
                    // 無効なパスは無視
                }
            }
        }

        return null;
    }

    /// <summary>
    /// YouTube高画質配信向け（1080p 60fps / 6000kbps）のFFmpegコマンドライン引数を生成します。
    /// 【設定内容】
    /// - エンコーダ: h264_nvenc (映像), aac (音声)
    /// - 解像度: 1920x1080
    /// - フレームレート: 60fps
    /// - 映像ビットレート: 6000k
    /// - 音声ビットレート: 128k
    /// - キーフレーム間隔（GOP）: 2秒分（120）
    /// - 出力フォーマット: flv (RTMP配信用)
    /// </summary>
    /// <param name="pipeFullPath">音声入力パイプのフルパス</param>
    /// <param name="fullRtmpTarget">配信先URL（ストリームキーを含む）</param>
    /// <param name="inputWidth">入力映像幅（既定: 1920）</param>
    /// <param name="inputHeight">入力映像高さ（既定: 1080）</param>
    /// <param name="inputFps">入力フレームレート（既定: 60）</param>
    /// <returns>FFmpegプロセスのArguments文字列</returns>
    public static string GenerateArguments(
        string pipeFullPath,
        string fullRtmpTarget,
        int inputWidth = 1920,
        int inputHeight = 1080,
        int inputFps = 60)
    {
        return $"-y -re -f rawvideo -pix_fmt bgr24 -s {inputWidth}x{inputHeight} -r {inputFps} -i - " +
               $"-f s16le -ar 44100 -ac 1 -i {pipeFullPath} " +
               $"-c:v h264_nvenc -preset p4 -b:v 6000k -maxrate 6000k -bufsize 12000k -pix_fmt yuv420p -s 1920x1080 -r 60 -g 120 " +
               $"-c:a aac -b:a 128k -ar 44100 " +
               $"-f flv \"{fullRtmpTarget}\"";
    }

    /// <summary>
    /// GenerateArguments のエイリアス
    /// </summary>
    public static string BuildArguments(
        string pipeFullPath,
        string fullRtmpTarget,
        int inputWidth = 1920,
        int inputHeight = 1080,
        int inputFps = 60)
        => GenerateArguments(pipeFullPath, fullRtmpTarget, inputWidth, inputHeight, inputFps);

    /// <summary>
    /// RTMP配信を開始します
    /// </summary>
    public async Task StartStreamingAsync(string rtmpUrl, string streamKey, int width = 1920, int height = 1080, int fps = 60)
    {
        StopStreaming();

        if (string.IsNullOrWhiteSpace(streamKey))
        {
            ErrorOccurred?.Invoke("ストリームキーを入力してください。");
            return;
        }

        var ffmpegPath = FindFfmpegPath();
        if (string.IsNullOrEmpty(ffmpegPath))
        {
            ErrorOccurred?.Invoke("ffmpeg.exe が見つかりません。アプリフォルダ（またはプロジェクトフォルダ）に ffmpeg.exe を配置してください。");
            return;
        }

        // URLの末尾スラッシュ調整
        rtmpUrl = rtmpUrl.TrimEnd('/');
        string fullRtmpTarget = $"{rtmpUrl}/{streamKey.Trim()}";

        StatusChanged?.Invoke("配信接続を準備中...");

        lock (_lockObj)
        {
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            // 映像・音声の非同期キューを準備（古いフレームで詰まらないようBoundedChannel使用）
            _videoChannel = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(4)
            {
                FullMode = BoundedChannelFullMode.DropOldest
            });

            _audioChannel = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(30)
            {
                FullMode = BoundedChannelFullMode.Wait
            });

            // 音声用名前付きパイプの一意名
            string pipeName = $"woodstream_audio_{Guid.NewGuid():N}";
            string pipeFullPath = $@"\\.\pipe\{pipeName}";

            try
            {
                // 音声用パイプサーバー作成 (Windows Named Pipe)
                _audioPipeServer = new NamedPipeServerStream(
                    pipeName,
                    PipeDirection.Out,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                // FFmpegプロセスの起動設定（YouTube高画質配信向け引数を生成）
                string arguments = GenerateArguments(pipeFullPath, fullRtmpTarget, width, height, fps);

                var startInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };

                _ffmpegProcess = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

                _ffmpegProcess.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        Debug.WriteLine($"[FFmpeg] {e.Data}");
                    }
                };

                _ffmpegProcess.Exited += (s, e) =>
                {
                    if (IsStreaming)
                    {
                        StatusChanged?.Invoke("配信プロセスが終了しました。");
                        StopStreaming();
                    }
                };

                _ffmpegProcess.Start();
                _ffmpegProcess.BeginErrorReadLine();

                // バックグラウンドで名前付きパイプの接続を待機
                _ = Task.Run(async () =>
                {
                    try
                    {
                        if (_audioPipeServer != null)
                        {
                            await _audioPipeServer.WaitForConnectionAsync(token);
                            Debug.WriteLine("[RtmpStreamService] 音声パイプ接続完了");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"音声パイプ待機エラー: {ex.Message}");
                    }
                }, token);

                // 映像書き込みタスク (stdin)
                _videoWriteTask = Task.Run(() => WriteVideoLoop(_ffmpegProcess.StandardInput.BaseStream, token), token);

                // 音声書き込みタスク (Named Pipe)
                _audioWriteTask = Task.Run(() => WriteAudioLoop(token), token);

                _streamingTimer = Stopwatch.StartNew();
                IsStreaming = true;
                StatusChanged?.Invoke("配信中 (LIVE)");
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke($"配信プロセスの起動に失敗しました: {ex.Message}");
                StopStreaming();
            }
        }
    }

    /// <summary>
    /// 映像フレームを非同期キューへ投入します (BGR24)
    /// </summary>
    public void PushVideoFrame(byte[] bgrData)
    {
        if (!IsStreaming || _videoChannel == null) return;
        _videoChannel.Writer.TryWrite(bgrData);
    }

    /// <summary>
    /// 音声サンプルデータを非同期キューへ投入します (PCM 16bit)
    /// </summary>
    public void PushAudioData(byte[] buffer, int offset, int count)
    {
        if (!IsStreaming || _audioChannel == null || count <= 0) return;

        var data = new byte[count];
        Buffer.BlockCopy(buffer, offset, data, 0, count);
        _audioChannel.Writer.TryWrite(data);
    }

    /// <summary>
    /// 映像データを FFmpeg の StandardInput に書き込むループ
    /// </summary>
    private async Task WriteVideoLoop(Stream stdinStream, CancellationToken token)
    {
        try
        {
            var reader = _videoChannel!.Reader;
            while (await reader.WaitToReadAsync(token))
            {
                while (reader.TryRead(out var frameData))
                {
                    await stdinStream.WriteAsync(frameData, 0, frameData.Length, token);
                }
                await stdinStream.FlushAsync(token);
            }
        }
        catch (OperationCanceledException)
        {
            // キャンセル時は正常終了
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"映像書き込みエラー: {ex.Message}");
        }
    }

    /// <summary>
    /// 音声データを FFmpeg の Named Pipe に書き込むループ
    /// </summary>
    private async Task WriteAudioLoop(CancellationToken token)
    {
        try
        {
            // パイプ接続待機
            while (_audioPipeServer != null && !_audioPipeServer.IsConnected && !token.IsCancellationRequested)
            {
                await Task.Delay(50, token);
            }

            if (_audioPipeServer == null || !_audioPipeServer.IsConnected) return;

            var reader = _audioChannel!.Reader;
            while (await reader.WaitToReadAsync(token))
            {
                while (reader.TryRead(out var audioData))
                {
                    if (_audioPipeServer.IsConnected)
                    {
                        await _audioPipeServer.WriteAsync(audioData, 0, audioData.Length, token);
                    }
                }
                if (_audioPipeServer.IsConnected)
                {
                    await _audioPipeServer.FlushAsync(token);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // キャンセル時
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"音声書き込みエラー: {ex.Message}");
        }
    }

    /// <summary>
    /// 配信を安全に停止し、パイプおよびプロセスを破棄します
    /// </summary>
    public void StopStreaming()
    {
        lock (_lockObj)
        {
            if (!IsStreaming && _ffmpegProcess == null) return;

            IsStreaming = false;
            _streamingTimer?.Stop();

            // 1. キューへの投入を完了
            _videoChannel?.Writer.TryComplete();
            _audioChannel?.Writer.TryComplete();

            // 2. キャンセルトークン発行
            if (_cts != null)
            {
                _cts.Cancel();
            }

            // 3. FFmpeg の stdin を閉じる（正常なストリーム終了シグナル）
            try
            {
                if (_ffmpegProcess != null && !_ffmpegProcess.HasExited)
                {
                    _ffmpegProcess.StandardInput.Close();
                }
            }
            catch
            {
                // 無視
            }

            // 4. 音声パイプを閉じる
            try
            {
                _audioPipeServer?.Dispose();
                _audioPipeServer = null;
            }
            catch
            {
                // 無視
            }

            // 5. プロセスの終了待機と安全なKill
            if (_ffmpegProcess != null)
            {
                try
                {
                    if (!_ffmpegProcess.WaitForExit(2500))
                    {
                        _ffmpegProcess.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                    // 無視
                }
                finally
                {
                    _ffmpegProcess.Dispose();
                    _ffmpegProcess = null;
                }
            }

            _cts?.Dispose();
            _cts = null;
            _videoWriteTask = null;
            _audioWriteTask = null;

            StatusChanged?.Invoke("配信停止中");
        }
    }

    public void Dispose()
    {
        StopStreaming();
    }
}
