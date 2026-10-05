# WoodStream Streaming Studio

WoodStream Streaming Studio は、C# / WPF（.NET 10）で構築されたライブ配信・ストリーミング支援スタジオアプリケーションです。

## 主な機能

### フェーズ 1: リハーサルモード（デバイス入力確認）
- **Webカメラ プレビュー**:
  - OpenCvSharp4 による Webカメラ映像のリアルタイム取得・表示
  - デバイスフレンドリー名（製品名）の列挙と切り替え
  - 左右反転（ミラー表示）対応
- **画面 / ウィンドウ キャプチャ プレビュー**:
  - Win32 API（BitBlt / DIBits）によるマルチディスプレイおよび各ウィンドウの高速キャプチャ
- **マイク音声レベルメーター**:
  - NAudio を用いた入力音量のリアルタイム測定
  - グリーン〜イエロー〜レッドの視覚的 VU メーター（ProgressBar）および dB 表示
  - ミュート機能

### フェーズ 2: リアルタイム映像合成（ピクチャーインピクチャー）
- **配信用プレビュー (PROGRAM OUT)**:
  - キャプチャ画面全体を背景として配置
  - 右下に Webカメラ映像を縮小してピクチャーインピクチャー（PiP）合成
  - カメラのミラー設定を合成映像に忠実に反映
  - OpenCvSharp の `using` / `Dispose()` による厳密なメモリリーク対策（30〜60 FPS 安定動作）

### UI / アプリケーション設計
- OBS Studio 風の洗練されたダークテーマ UI
- **多言語対応**: 日本語（ja-JP）および英語（en-US）対応（Windows の表示言語に応じて自動切替、手動切替も可能）
- **設定の自動保存・復元**:
  - 保存先: `%LOCALAPPDATA%\WoodStreamStreamingStudio\settings.json`
  - 日本語を Unicode エスケープせず UTF-8 可視テキストで保存
  - 終了時のウィンドウ位置・サイズ・最大化状態を記憶し、次回起動時に安全に復元

## 必要環境
- Windows 10 (19041 以降) / Windows 11
- .NET 10 SDK (`net10.0-windows10.0.19041.0`)

## 使用技術・ライブラリ
- **言語 / フレームワーク**: C# 13, WPF (.NET 10)
- **映像処理**: [OpenCvSharp4](https://github.com/shimat/opencvsharp)
- **音声処理**: [NAudio](https://github.com/naudio/NAudio)
- **設定シリアライズ**: `System.Text.Json`
