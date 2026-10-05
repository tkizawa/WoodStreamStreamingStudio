using System;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using WoodStreamStreamingStudio.Models;

namespace WoodStreamStreamingStudio.Services;

/// <summary>
/// アプリケーション設定の読み込みおよび保存を行うサービス
/// 保存先: AppData\Local\WoodStreamStreamingStudio\settings.json
/// </summary>
public class SettingsService
{
    private const string AppFolderName = "WoodStreamStreamingStudio";
    private const string SettingsFileName = "settings.json";

    private readonly string _settingsFilePath;
    private readonly JsonSerializerOptions _jsonOptions;

    public SettingsService()
    {
        // 開発規約に基づき AppData\Local\WoodStreamStreamingStudio に保存
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appDirectory = Path.Combine(localAppData, AppFolderName);

        if (!Directory.Exists(appDirectory))
        {
            Directory.CreateDirectory(appDirectory);
        }

        _settingsFilePath = Path.Combine(appDirectory, SettingsFileName);

        // 日本語をUnicodeエスケープせず、そのまま可視テキスト（UTF-8）として保存
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
    }

    /// <summary>
    /// 設定ファイルを読み込みます。ファイルが存在しない場合はデフォルト設定を返します。
    /// </summary>
    public AppSettings Load()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                var json = File.ReadAllText(_settingsFilePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions);
                if (settings != null)
                {
                    return settings;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"設定ファイルの読み込みに失敗しました: {ex.Message}");
        }

        return new AppSettings();
    }

    /// <summary>
    /// 設定ファイルに設定内容を書き込みます。
    /// </summary>
    public void Save(AppSettings settings)
    {
        try
        {
            var json = JsonSerializer.Serialize(settings, _jsonOptions);
            File.WriteAllText(_settingsFilePath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"設定ファイルの保存に失敗しました: {ex.Message}");
        }
    }
}
