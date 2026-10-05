namespace WoodStreamStreamingStudio.Models;

/// <summary>
/// 配信・プレビューの映像出力モード
/// </summary>
public enum BroadcastMode
{
    /// <summary>画面キャプチャとWebカメラ映像の合成（PiP: Picture in Picture）</summary>
    PictureInPicture = 0,

    /// <summary>画面キャプチャ映像のみ</summary>
    ScreenOnly = 1,

    /// <summary>Webカメラ映像のみ（全画面）</summary>
    CameraOnly = 2
}
