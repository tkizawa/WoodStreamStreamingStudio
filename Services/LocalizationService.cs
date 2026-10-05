using System;
using System.Globalization;
using System.Linq;
using System.Windows;

namespace WoodStreamStreamingStudio.Services;

/// <summary>
/// 多言語化（日本語・英語）を管理するサービス
/// Windowsの表示言語設定に応じて自動判定または手動切り替えを行います。
/// </summary>
public class LocalizationService
{
    private static LocalizationService? _instance;
    public static LocalizationService Instance => _instance ??= new LocalizationService();

    public string CurrentLanguageCode { get; private set; } = "ja";

    /// <summary>
    /// アプリケーション起動時に言語を適用します。
    /// 指定がない場合は Windows の表示言語 (CultureInfo.CurrentUICulture) に基づいて判定します。
    /// </summary>
    /// <param name="preference">"auto", "ja", "en"</param>
    public void ApplyLanguage(string preference = "auto")
    {
        string targetLang;
        if (preference == "ja" || preference == "en")
        {
            targetLang = preference;
        }
        else
        {
            // Windowsの表示言語判定
            var uiCulture = CultureInfo.CurrentUICulture;
            targetLang = uiCulture.TwoLetterISOLanguageName.Equals("ja", StringComparison.OrdinalIgnoreCase) ? "ja" : "en";
        }

        SetLanguage(targetLang);
    }

    /// <summary>
    /// 指定された言語（ja または en）のリソースディクショナリを読み込んで適用します。
    /// </summary>
    public void SetLanguage(string langCode)
    {
        CurrentLanguageCode = langCode;

        var dictUriString = langCode == "ja"
            ? "pack://application:,,,/Resources/Strings.ja-JP.xaml"
            : "pack://application:,,,/Resources/Strings.en-US.xaml";

        var newDict = new ResourceDictionary { Source = new Uri(dictUriString, UriKind.Absolute) };

        // 既存の言語リソースを検索して差し替える
        var appResources = Application.Current.Resources;
        var existingDict = appResources.MergedDictionaries
            .FirstOrDefault(d => d.Source != null && d.Source.OriginalString.Contains("/Resources/Strings."));

        if (existingDict != null)
        {
            var index = appResources.MergedDictionaries.IndexOf(existingDict);
            appResources.MergedDictionaries[index] = newDict;
        }
        else
        {
            appResources.MergedDictionaries.Add(newDict);
        }
    }

    /// <summary>
    /// キーに対応するローカライズ文字列を取得します。
    /// </summary>
    public string GetString(string key)
    {
        if (Application.Current.TryFindResource(key) is string val)
        {
            return val;
        }
        return key;
    }
}
