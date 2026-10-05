namespace WoodStreamStreamingStudio.Models;

/// <summary>
/// Webカメラのデバイス情報
/// </summary>
public class CameraDeviceInfo
{
    /// <summary>デバイスのインデックス番号 (OpenCV VideoCapture用)</summary>
    public int Index { get; set; }

    /// <summary>表示用デバイス名</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Windows DeviceId (存在する場合)</summary>
    public string Id { get; set; } = string.Empty;

    public override string ToString() => Name;
}

/// <summary>
/// キャプチャ対象の種別
/// </summary>
public enum CaptureSourceType
{
    Display,
    Window
}

/// <summary>
/// 画面またはウィンドウのキャプチャ対象情報
/// </summary>
public class CaptureSourceInfo
{
    public CaptureSourceType SourceType { get; set; }

    /// <summary>表示名（ディスプレイ番号やウィンドウタイトル）</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>ウィンドウハンドルまたはモニタ識別子</summary>
    public nint Handle { get; set; }

    /// <summary>対象の境界矩形 (画面座標)</summary>
    public System.Drawing.Rectangle Bounds { get; set; }

    public override string ToString() => Title;
}

/// <summary>
/// 音声入力（マイク）デバイス情報
/// </summary>
public class AudioDeviceInfo
{
    /// <summary>NAudio WaveIn デバイス番号</summary>
    public int DeviceNumber { get; set; }

    /// <summary>デバイス名</summary>
    public string Name { get; set; } = string.Empty;

    public override string ToString() => Name;
}
