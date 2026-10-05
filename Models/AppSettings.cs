using System.Text.Json.Serialization;

namespace WoodStreamStreamingStudio.Models;

/// <summary>
/// アプリケーション全体の設定情報を保持するモデル
/// </summary>
public class AppSettings
{
    /// <summary>ウィンドウのX座標 (Left)</summary>
    public double WindowLeft { get; set; } = 100;

    /// <summary>ウィンドウのY座標 (Top)</summary>
    public double WindowTop { get; set; } = 100;

    /// <summary>ウィンドウの幅</summary>
    public double WindowWidth { get; set; } = 1200;

    /// <summary>ウィンドウの高さ</summary>
    public double WindowHeight { get; set; } = 800;

    /// <summary>ウィンドウが最大化されているか</summary>
    public bool IsMaximized { get; set; } = false;

    /// <summary>選択中のカメラデバイス名または識別子</summary>
    public string? SelectedCameraDevice { get; set; }

    /// <summary>カメラを左右反転 (ミラー) するか</summary>
    public bool CameraMirror { get; set; } = true;

    /// <summary>選択中の画面キャプチャ対象識別名</summary>
    public string? SelectedCaptureSource { get; set; }

    /// <summary>選択中のマイクデバイス番号またはデバイス名</summary>
    public string? SelectedAudioDevice { get; set; }

    /// <summary>マイクがミュートされているか</summary>
    public bool AudioMuted { get; set; } = false;

    /// <summary>言語設定 ("auto", "ja", "en")</summary>
    public string Language { get; set; } = "auto";
}
