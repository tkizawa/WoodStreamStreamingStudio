using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using WoodStreamStreamingStudio.Services;
using WoodStreamStreamingStudio.ViewModels;

namespace WoodStreamStreamingStudio;

/// <summary>
/// MainWindow の相互作用ロジック
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();

        _viewModel = (MainViewModel)DataContext;

        Loaded += OnLoaded;
        Closing += OnClosing;

        // 保存されたウィンドウ位置・サイズの復元
        RestoreWindowBounds();
    }

    /// <summary>
    /// 設定からウィンドウ位置・サイズを復元します
    /// </summary>
    private void RestoreWindowBounds()
    {
        var settings = _viewModel.GetCurrentSettings();

        // 仮想スクリーンの範囲内に収まっているかチェック
        var virtualLeft = SystemParameters.VirtualScreenLeft;
        var virtualTop = SystemParameters.VirtualScreenTop;
        var virtualWidth = SystemParameters.VirtualScreenWidth;
        var virtualHeight = SystemParameters.VirtualScreenHeight;

        if (settings.WindowLeft >= virtualLeft &&
            settings.WindowTop >= virtualTop &&
            settings.WindowLeft + settings.WindowWidth <= virtualLeft + virtualWidth + 50 &&
            settings.WindowTop + settings.WindowHeight <= virtualTop + virtualHeight + 50 &&
            settings.WindowWidth >= MinWidth &&
            settings.WindowHeight >= MinHeight)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = settings.WindowLeft;
            Top = settings.WindowTop;
            Width = settings.WindowWidth;
            Height = settings.WindowHeight;

            if (settings.IsMaximized)
            {
                WindowState = WindowState.Maximized;
            }
        }
        else
        {
            // 画面外や不正な値の場合は画面中央に配置
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // ウィンドウハンドルの取得
        var helper = new WindowInteropHelper(this);
        var hwnd = helper.Handle;

        // 各種デバイス（カメラ、画面/ウィンドウ、マイク）の列挙と自動開始
        await _viewModel.InitializeDevicesAsync(hwnd);
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        // 開発規約: 終了時のウィンドウ位置およびサイズを保存
        bool isMaximized = WindowState == WindowState.Maximized;
        double left = RestoreBounds.Left;
        double top = RestoreBounds.Top;
        double width = RestoreBounds.Width;
        double height = RestoreBounds.Height;

        if (WindowState == WindowState.Normal)
        {
            left = Left;
            top = Top;
            width = Width;
            height = Height;
        }

        _viewModel.SaveSettings(left, top, width, height, isMaximized);

        // 各種キャプチャのリソース解放
        _viewModel.Dispose();
    }

    private void OnLanguageSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox comboBox && comboBox.SelectedItem is ComboBoxItem item && item.Tag is string langCode)
        {
            LocalizationService.Instance.SetLanguage(langCode);
        }
    }
}