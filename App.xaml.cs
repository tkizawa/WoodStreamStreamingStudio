using System.Windows;
using WoodStreamStreamingStudio.Services;

namespace WoodStreamStreamingStudio;

/// <summary>
/// アプリケーションのエントリポイント
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 設定から言語設定を読み込み、多言語リソースを適用
        var settingsService = new SettingsService();
        var settings = settingsService.Load();
        LocalizationService.Instance.ApplyLanguage(settings.Language);
    }
}
