using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using WoodStreamStreamingStudio.Models;

namespace WoodStreamStreamingStudio.Services;

/// <summary>
/// YouTube Data API v3 を使用してライブ配信のチャットメッセージを非同期ポーリングするサービス
/// </summary>
public class YouTubeChatService : IDisposable
{
    private YouTubeService? _youtubeService;
    private CancellationTokenSource? _cts;
    private Task? _pollingTask;
    private readonly object _lockObj = new();

    private string? _liveChatId;
    private string? _nextPageToken;

    public bool IsPolling { get; private set; }

    /// <summary>新しいチャットメッセージを受信したときのイベント</summary>
    public event Action<List<ChatMessageItem>>? MessagesReceived;

    /// <summary>ステータス（接続中、接続成功など）の更新イベント</summary>
    public event Action<string>? StatusChanged;

    /// <summary>エラー発生時のイベント</summary>
    public event Action<string>? ErrorOccurred;

    /// <summary>
    /// YouTubeのURLまたはVideoIDから11文字のVideoIDを抽出します
    /// </summary>
    public static string? ExtractVideoId(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;

        input = input.Trim();

        // 11文字の英数字・記号で構成された直接のVideo ID
        if (Regex.IsMatch(input, @"^[a-zA-Z0-9_-]{11}$"))
        {
            return input;
        }

        // https://www.youtube.com/watch?v=VIDEO_ID
        var matchWatch = Regex.Match(input, @"(?:v=|\/v\/|embed\/|live\/|youtu\.be\/|\/shorts\/)([a-zA-Z0-9_-]{11})");
        if (matchWatch.Success)
        {
            return matchWatch.Groups[1].Value;
        }

        return null;
    }

    /// <summary>
    /// ライブ配信チャットのポーリングを開始します
    /// </summary>
    public async Task StartPollingAsync(string apiKey, string urlOrVideoId)
    {
        StopPolling();

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            ErrorOccurred?.Invoke("APIキーを入力してください。");
            return;
        }

        var videoId = ExtractVideoId(urlOrVideoId);
        if (string.IsNullOrWhiteSpace(videoId))
        {
            ErrorOccurred?.Invoke("有効なYouTubeライブ配信URLまたはVideo IDを入力してください。");
            return;
        }

        StatusChanged?.Invoke("ライブ配信情報を確認中...");

        try
        {
            _youtubeService = new YouTubeService(new BaseClientService.Initializer
            {
                ApiKey = apiKey,
                ApplicationName = "WoodStreamStreamingStudio"
            });

            // 1. ビデオ情報から activeLiveChatId を取得
            var videoRequest = _youtubeService.Videos.List("liveStreamingDetails,snippet");
            videoRequest.Id = videoId;
            var videoResponse = await videoRequest.ExecuteAsync();

            var videoItem = videoResponse.Items?.FirstOrDefault();
            if (videoItem == null)
            {
                ErrorOccurred?.Invoke("指定された動画が見つかりませんでした。");
                return;
            }

            _liveChatId = videoItem.LiveStreamingDetails?.ActiveLiveChatId;
            if (string.IsNullOrEmpty(_liveChatId))
            {
                ErrorOccurred?.Invoke("ライブチャットが有効になっていないか、配信が開始されていません。");
                return;
            }

            var title = videoItem.Snippet?.Title ?? videoId;
            StatusChanged?.Invoke($"接続完了: {title}");

            // 2. バックグラウンドでのポーリングタスクを開始
            lock (_lockObj)
            {
                _cts = new CancellationTokenSource();
                var token = _cts.Token;
                IsPolling = true;
                _nextPageToken = null;
                _pollingTask = Task.Run(() => PollingLoop(token), token);
            }
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke($"YouTube API エラー: {ex.Message}");
            StopPolling();
        }
    }

    /// <summary>
    /// チャットメッセージの定期取得ループ
    /// </summary>
    private async Task PollingLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            int waitMillis = 4000; // デフォルトポーリング間隔 4秒

            try
            {
                if (_youtubeService == null || string.IsNullOrEmpty(_liveChatId))
                {
                    break;
                }

                var chatRequest = _youtubeService.LiveChatMessages.List(_liveChatId, "snippet,authorDetails");
                chatRequest.PageToken = _nextPageToken;

                var chatResponse = await chatRequest.ExecuteAsync(token);

                if (token.IsCancellationRequested) break;

                // APIが推奨する次回ポーリング間隔（ミリ秒）を取得
                if (chatResponse.PollingIntervalMillis.HasValue && chatResponse.PollingIntervalMillis.Value > 1000)
                {
                    waitMillis = (int)chatResponse.PollingIntervalMillis.Value;
                }

                _nextPageToken = chatResponse.NextPageToken;

                if (chatResponse.Items != null && chatResponse.Items.Count > 0)
                {
                    var newMessages = new List<ChatMessageItem>();

                    foreach (var item in chatResponse.Items)
                    {
                        var author = item.AuthorDetails;
                        var snippet = item.Snippet;

                        var messageText = snippet?.DisplayMessage ?? snippet?.TextMessageDetails?.MessageText ?? string.Empty;

                        if (!string.IsNullOrEmpty(messageText))
                        {
                            newMessages.Add(new ChatMessageItem
                            {
                                Id = item.Id,
                                AuthorName = author?.DisplayName ?? "名無し",
                                AuthorProfileImageUrl = author?.ProfileImageUrl,
                                MessageText = messageText,
                                PublishedAt = snippet?.PublishedAtDateTimeOffset?.DateTime,
                                IsModerator = author?.IsChatModerator ?? false,
                                IsOwner = author?.IsChatOwner ?? false,
                                IsSponsor = author?.IsChatSponsor ?? false
                            });
                        }
                    }

                    if (newMessages.Count > 0)
                    {
                        MessagesReceived?.Invoke(newMessages);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                {
                    Debug.WriteLine($"チャット取得エラー: {ex.Message}");
                    StatusChanged?.Invoke($"一時エラー (リトライ中): {ex.Message}");
                }
                waitMillis = 8000; // エラー時は少し間隔を空ける
            }

            try
            {
                await Task.Delay((int)waitMillis, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        IsPolling = false;
    }

    /// <summary>
    /// チャット取得のポーリングを停止します
    /// </summary>
    public void StopPolling()
    {
        lock (_lockObj)
        {
            if (_cts != null)
            {
                _cts.Cancel();
                _cts.Dispose();
                _cts = null;
                _pollingTask = null;
            }
            _youtubeService?.Dispose();
            _youtubeService = null;
            IsPolling = false;
            StatusChanged?.Invoke("停止中");
        }
    }

    public void Dispose()
    {
        StopPolling();
    }
}
