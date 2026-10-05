using System;

namespace WoodStreamStreamingStudio.Models;

/// <summary>
/// YouTube ライブチャットのメッセージ情報モデル
/// </summary>
public class ChatMessageItem
{
    public string Id { get; set; } = string.Empty;

    /// <summary>投稿者名</summary>
    public string AuthorName { get; set; } = string.Empty;

    /// <summary>プロフィールアイコン画像URL</summary>
    public string? AuthorProfileImageUrl { get; set; }

    /// <summary>コメント本文</summary>
    public string MessageText { get; set; } = string.Empty;

    /// <summary>投稿時刻</summary>
    public DateTime? PublishedAt { get; set; }

    /// <summary>表示用時刻フォーマット (HH:mm)</summary>
    public string TimeText => PublishedAt?.ToLocalTime().ToString("HH:mm") ?? string.Empty;

    /// <summary>モデレーターかどうか</summary>
    public bool IsModerator { get; set; }

    /// <summary>配信者本人かどうか</summary>
    public bool IsOwner { get; set; }

    /// <summary>スポンサー/メンバーかどうか</summary>
    public bool IsSponsor { get; set; }
}
