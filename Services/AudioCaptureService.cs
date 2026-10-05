using System;
using System.Collections.Generic;
using System.Diagnostics;
using NAudio.Wave;
using WoodStreamStreamingStudio.Models;

namespace WoodStreamStreamingStudio.Services;

/// <summary>
/// NAudioを使用したマイク音声キャプチャおよびリアルタイム音量レベル測定サービス
/// </summary>
public class AudioCaptureService : IDisposable
{
    private WaveIn? _waveIn;
    private readonly object _lockObj = new();

    public bool IsRunning { get; private set; }
    public bool IsMuted { get; set; } = false;

    /// <summary>音量レベル更新イベント (level: 0.0~1.0, dB: -60~0)</summary>
    public event Action<float, float>? AudioLevelChanged;

    /// <summary>エラー発生時イベント</summary>
    public event Action<string>? ErrorOccurred;

    /// <summary>
    /// システムで利用可能なマイク（録音デバイス）の一覧を取得します
    /// </summary>
    public List<AudioDeviceInfo> GetAudioDevices()
    {
        var devices = new List<AudioDeviceInfo>();

        try
        {
            int count = WaveIn.DeviceCount;
            for (int i = 0; i < count; i++)
            {
                var caps = WaveIn.GetCapabilities(i);
                devices.Add(new AudioDeviceInfo
                {
                    DeviceNumber = i,
                    Name = caps.ProductName
                });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"マイクデバイス列挙失敗: {ex.Message}");
        }

        return devices;
    }

    /// <summary>
    /// 指定したマイク番号でキャプチャを開始します
    /// </summary>
    public void Start(int deviceNumber)
    {
        Stop();

        lock (_lockObj)
        {
            try
            {
                _waveIn = new WaveIn
                {
                    DeviceNumber = deviceNumber,
                    WaveFormat = new WaveFormat(44100, 16, 1), // 44.1kHz, 16bit, モノラル
                    BufferMilliseconds = 30 // 約30msごとの即応性の高い更新
                };

                _waveIn.DataAvailable += OnDataAvailable;
                _waveIn.RecordingStopped += OnRecordingStopped;

                _waveIn.StartRecording();
                IsRunning = true;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke($"マイクの初期化に失敗しました: {ex.Message}");
                IsRunning = false;
            }
        }
    }

    /// <summary>
    /// 音声データ受信時のピークレベル計算
    /// </summary>
    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (IsMuted)
        {
            AudioLevelChanged?.Invoke(0f, -60f);
            return;
        }

        float maxSample = 0;

        // 16bit PCM サンプルからピーク値を算出
        for (int i = 0; i < e.BytesRecorded; i += 2)
        {
            short sample = (short)((e.Buffer[i + 1] << 8) | e.Buffer[i]);
            float sample32 = Math.Abs(sample / 32768.0f);
            if (sample32 > maxSample)
            {
                maxSample = sample32;
            }
        }

        // dB 計算 (0dB が最大、無音時は -60dB)
        float db = -60f;
        if (maxSample > 0.001f)
        {
            db = (float)(20 * Math.Log10(maxSample));
            if (db < -60f) db = -60f;
            if (db > 0f) db = 0f;
        }

        AudioLevelChanged?.Invoke(maxSample, db);
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception != null)
        {
            ErrorOccurred?.Invoke($"マイク録音が停止しました: {e.Exception.Message}");
        }
        IsRunning = false;
    }

    /// <summary>
    /// マイクキャプチャを停止します
    /// </summary>
    public void Stop()
    {
        lock (_lockObj)
        {
            if (_waveIn != null)
            {
                _waveIn.DataAvailable -= OnDataAvailable;
                _waveIn.RecordingStopped -= OnRecordingStopped;
                try
                {
                    _waveIn.StopRecording();
                }
                catch
                {
                    // 停止時例外を無視
                }
                _waveIn.Dispose();
                _waveIn = null;
            }
            IsRunning = false;
            AudioLevelChanged?.Invoke(0f, -60f);
        }
    }

    public void Dispose()
    {
        Stop();
    }
}
