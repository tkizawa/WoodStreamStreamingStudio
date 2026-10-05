using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using WoodStreamStreamingStudio.Models;
using WoodStreamStreamingStudio.Services;

namespace WoodStreamStreamingStudio.ViewModels;

/// <summary>
/// メインウィンドウ（リハーサル画面）のViewModel
/// カメラ・画面キャプチャ・マイク音声の入出力を統合管理します。
/// </summary>
public class MainViewModel : ViewModelBase, IDisposable
{
    private readonly CameraCaptureService _cameraService;
    private readonly ScreenCaptureService _screenService;
    private readonly AudioCaptureService _audioService;
    private readonly StreamCompositorService _compositorService;
    private readonly SettingsService _settingsService;
    private readonly LocalizationService _locService;

    private AppSettings _appSettings;
    private nint _windowHandle = nint.Zero;

    #region 配信用プレビュー（合成） プロパティ
    private BitmapSource? _streamPreviewImage;
    public BitmapSource? StreamPreviewImage
    {
        get => _streamPreviewImage;
        set => SetProperty(ref _streamPreviewImage, value);
    }

    private bool _isStreamRunning = true;
    public bool IsStreamRunning
    {
        get => _isStreamRunning;
        set => SetProperty(ref _isStreamRunning, value);
    }

    private string _streamStatusText = "-- x -- @ 0 FPS";
    public string StreamStatusText
    {
        get => _streamStatusText;
        set => SetProperty(ref _streamStatusText, value);
    }
    #endregion

    #region カメラ プロパティ
    private ObservableCollection<CameraDeviceInfo> _cameraDevices = new();
    public ObservableCollection<CameraDeviceInfo> CameraDevices
    {
        get => _cameraDevices;
        set => SetProperty(ref _cameraDevices, value);
    }

    private CameraDeviceInfo? _selectedCameraDevice;
    public CameraDeviceInfo? SelectedCameraDevice
    {
        get => _selectedCameraDevice;
        set
        {
            if (SetProperty(ref _selectedCameraDevice, value))
            {
                if (value != null && IsCameraRunning)
                {
                    // 選択変更時に即座に再起動
                    StartCamera();
                }
            }
        }
    }

    private BitmapSource? _cameraPreviewImage;
    public BitmapSource? CameraPreviewImage
    {
        get => _cameraPreviewImage;
        set => SetProperty(ref _cameraPreviewImage, value);
    }

    private bool _isCameraRunning;
    public bool IsCameraRunning
    {
        get => _isCameraRunning;
        set => SetProperty(ref _isCameraRunning, value);
    }

    private bool _isCameraMirror = true;
    public bool IsCameraMirror
    {
        get => _isCameraMirror;
        set
        {
            if (SetProperty(ref _isCameraMirror, value))
            {
                _cameraService.Mirror = value;
            }
        }
    }

    private string _cameraStatusText = "-- x -- @ 0 FPS";
    public string CameraStatusText
    {
        get => _cameraStatusText;
        set => SetProperty(ref _cameraStatusText, value);
    }
    #endregion

    #region 画面キャプチャ プロパティ
    private ObservableCollection<CaptureSourceInfo> _captureSources = new();
    public ObservableCollection<CaptureSourceInfo> CaptureSources
    {
        get => _captureSources;
        set => SetProperty(ref _captureSources, value);
    }

    private CaptureSourceInfo? _selectedCaptureSource;
    public CaptureSourceInfo? SelectedCaptureSource
    {
        get => _selectedCaptureSource;
        set
        {
            if (SetProperty(ref _selectedCaptureSource, value))
            {
                if (value != null && IsScreenRunning)
                {
                    StartScreenCapture();
                }
            }
        }
    }

    private BitmapSource? _screenPreviewImage;
    public BitmapSource? ScreenPreviewImage
    {
        get => _screenPreviewImage;
        set => SetProperty(ref _screenPreviewImage, value);
    }

    private bool _isScreenRunning;
    public bool IsScreenRunning
    {
        get => _isScreenRunning;
        set => SetProperty(ref _isScreenRunning, value);
    }

    private string _screenStatusText = "-- x -- @ 0 FPS";
    public string ScreenStatusText
    {
        get => _screenStatusText;
        set => SetProperty(ref _screenStatusText, value);
    }
    #endregion

    #region マイク プロパティ
    private ObservableCollection<AudioDeviceInfo> _audioDevices = new();
    public ObservableCollection<AudioDeviceInfo> AudioDevices
    {
        get => _audioDevices;
        set => SetProperty(ref _audioDevices, value);
    }

    private AudioDeviceInfo? _selectedAudioDevice;
    public AudioDeviceInfo? SelectedAudioDevice
    {
        get => _selectedAudioDevice;
        set
        {
            if (SetProperty(ref _selectedAudioDevice, value))
            {
                if (value != null && IsAudioRunning)
                {
                    StartAudioCapture();
                }
            }
        }
    }

    private double _audioLevel;
    public double AudioLevel
    {
        get => _audioLevel;
        set => SetProperty(ref _audioLevel, value);
    }

    private string _audioPeakDb = "-60.0 dB";
    public string AudioPeakDb
    {
        get => _audioPeakDb;
        set => SetProperty(ref _audioPeakDb, value);
    }

    private bool _isAudioRunning;
    public bool IsAudioRunning
    {
        get => _isAudioRunning;
        set => SetProperty(ref _isAudioRunning, value);
    }

    private bool _isAudioMuted;
    public bool IsAudioMuted
    {
        get => _isAudioMuted;
        set
        {
            if (SetProperty(ref _isAudioMuted, value))
            {
                _audioService.IsMuted = value;
            }
        }
    }
    #endregion

    #region 言語・ステータス
    private string _currentLanguage = "auto";
    public string CurrentLanguage
    {
        get => _currentLanguage;
        set => SetProperty(ref _currentLanguage, value);
    }

    private string _statusMessage = string.Empty;
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }
    #endregion

    #region コマンド
    public RelayCommand ToggleCameraCommand { get; }
    public RelayCommand ToggleCameraMirrorCommand { get; }
    public RelayCommand ToggleScreenCommand { get; }
    public RelayCommand RefreshScreenSourcesCommand { get; }
    public RelayCommand ToggleAudioCommand { get; }
    public RelayCommand ToggleMuteCommand { get; }
    public RelayCommand SetLanguageCommand { get; }
    #endregion

    public MainViewModel()
    {
        _settingsService = new SettingsService();
        _locService = LocalizationService.Instance;
        _cameraService = new CameraCaptureService();
        _screenService = new ScreenCaptureService();
        _audioService = new AudioCaptureService();
        _compositorService = new StreamCompositorService(_cameraService, _screenService);

        _appSettings = _settingsService.Load();
        CurrentLanguage = _appSettings.Language;
        IsCameraMirror = _appSettings.CameraMirror;
        _cameraService.Mirror = IsCameraMirror;
        IsAudioMuted = _appSettings.AudioMuted;
        _audioService.IsMuted = IsAudioMuted;

        // イベント購読
        _cameraService.FrameArrived += OnCameraFrameArrived;
        _cameraService.StatusChanged += OnCameraStatusChanged;
        _cameraService.ErrorOccurred += OnServiceError;

        _screenService.FrameArrived += OnScreenFrameArrived;
        _screenService.StatusChanged += OnScreenStatusChanged;
        _screenService.ErrorOccurred += OnServiceError;

        _compositorService.CompositeFrameArrived += OnCompositeFrameArrived;
        _compositorService.StatusChanged += OnCompositeStatusChanged;

        _audioService.AudioLevelChanged += OnAudioLevelChanged;
        _audioService.ErrorOccurred += OnServiceError;

        // コマンド初期化
        ToggleCameraCommand = new RelayCommand(ToggleCamera);
        ToggleCameraMirrorCommand = new RelayCommand(() => IsCameraMirror = !IsCameraMirror);
        ToggleScreenCommand = new RelayCommand(ToggleScreen);
        RefreshScreenSourcesCommand = new RelayCommand(RefreshScreenSources);
        ToggleAudioCommand = new RelayCommand(ToggleAudio);
        ToggleMuteCommand = new RelayCommand(() => IsAudioMuted = !IsAudioMuted);
        SetLanguageCommand = new RelayCommand(param =>
        {
            if (param is string lang)
            {
                CurrentLanguage = lang;
                _locService.ApplyLanguage(lang);
                _appSettings.Language = lang;
                SaveSettings();
            }
        });
    }

    /// <summary>
    /// ウィンドウのハンドル初期化時に呼び出され、デバイスの列挙を開始します
    /// </summary>
    public async Task InitializeDevicesAsync(nint windowHwnd)
    {
        _windowHandle = windowHwnd;

        // 1. カメラ一覧取得
        var cameras = await _cameraService.GetCameraDevicesAsync();
        CameraDevices.Clear();
        foreach (var cam in cameras)
        {
            CameraDevices.Add(cam);
        }

        // 前回の設定または先頭のカメラを選択
        if (CameraDevices.Count > 0)
        {
            var matched = CameraDevices.FirstOrDefault(c => c.Name == _appSettings.SelectedCameraDevice);
            SelectedCameraDevice = matched ?? CameraDevices[0];
            // 自動的にカメラプレビュー開始
            StartCamera();
        }

        // 2. 画面・ウィンドウ一覧取得
        RefreshScreenSources();
        if (CaptureSources.Count > 0)
        {
            var matched = CaptureSources.FirstOrDefault(s => s.Title == _appSettings.SelectedCaptureSource);
            SelectedCaptureSource = matched ?? CaptureSources[0];
            // 自動的に画面キャプチャプレビュー開始
            StartScreenCapture();
        }

        // 3. マイク一覧取得
        var mics = _audioService.GetAudioDevices();
        AudioDevices.Clear();
        foreach (var mic in mics)
        {
            AudioDevices.Add(mic);
        }

        if (AudioDevices.Count > 0)
        {
            var matched = AudioDevices.FirstOrDefault(a => a.Name == _appSettings.SelectedAudioDevice);
            SelectedAudioDevice = matched ?? AudioDevices[0];
            // 自動的にマイク監視開始
            StartAudioCapture();
        }

        // 4. リアルタイム映像合成（配信用プレビュー）開始
        _compositorService.Start();

        StatusMessage = _locService.GetString("ReadyStatus");
    }

    public void RefreshScreenSources()
    {
        var sources = _screenService.GetAvailableSources(_windowHandle);
        var prevSelectedTitle = SelectedCaptureSource?.Title;

        CaptureSources.Clear();
        foreach (var src in sources)
        {
            CaptureSources.Add(src);
        }

        if (!string.IsNullOrEmpty(prevSelectedTitle))
        {
            SelectedCaptureSource = CaptureSources.FirstOrDefault(s => s.Title == prevSelectedTitle);
        }

        if (SelectedCaptureSource == null && CaptureSources.Count > 0)
        {
            SelectedCaptureSource = CaptureSources[0];
        }
    }

    #region カメラ制御
    private void ToggleCamera()
    {
        if (IsCameraRunning)
        {
            StopCamera();
        }
        else
        {
            StartCamera();
        }
    }

    public void StartCamera()
    {
        if (SelectedCameraDevice == null) return;
        _cameraService.Start(SelectedCameraDevice.Index);
        IsCameraRunning = true;
    }

    public void StopCamera()
    {
        _cameraService.Stop();
        IsCameraRunning = false;
        CameraPreviewImage = null;
        CameraStatusText = "-- x -- @ 0 FPS";
    }

    private void OnCameraFrameArrived(BitmapSource bitmap)
    {
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            CameraPreviewImage = bitmap;
        }, System.Windows.Threading.DispatcherPriority.Render);
    }

    private void OnCameraStatusChanged(int fps, int width, int height)
    {
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            CameraStatusText = $"{width}x{height} @ {fps} FPS";
        });
    }
    #endregion

    #region 画面キャプチャ制御
    private void ToggleScreen()
    {
        if (IsScreenRunning)
        {
            StopScreenCapture();
        }
        else
        {
            StartScreenCapture();
        }
    }

    public void StartScreenCapture()
    {
        if (SelectedCaptureSource == null) return;
        _screenService.Start(SelectedCaptureSource);
        IsScreenRunning = true;
    }

    public void StopScreenCapture()
    {
        _screenService.Stop();
        IsScreenRunning = false;
        ScreenPreviewImage = null;
        ScreenStatusText = "-- x -- @ 0 FPS";
    }

    private void OnScreenFrameArrived(BitmapSource bitmap)
    {
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            ScreenPreviewImage = bitmap;
        }, System.Windows.Threading.DispatcherPriority.Render);
    }

    private void OnScreenStatusChanged(int fps, int width, int height)
    {
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            ScreenStatusText = $"{width}x{height} @ {fps} FPS";
        });
    }
    #endregion

    #region マイク制御
    private void ToggleAudio()
    {
        if (IsAudioRunning)
        {
            StopAudioCapture();
        }
        else
        {
            StartAudioCapture();
        }
    }

    public void StartAudioCapture()
    {
        if (SelectedAudioDevice == null) return;
        _audioService.Start(SelectedAudioDevice.DeviceNumber);
        IsAudioRunning = true;
    }

    public void StopAudioCapture()
    {
        _audioService.Stop();
        IsAudioRunning = false;
        AudioLevel = 0;
        AudioPeakDb = "-60.0 dB";
    }

    private void OnAudioLevelChanged(float level, float peakDb)
    {
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            // 0.0 ~ 1.0 を 0 ~ 100 にスケール
            AudioLevel = Math.Clamp(level * 100.0, 0, 100);
            AudioPeakDb = $"{peakDb:F1} dB";
        });
    }
    #endregion

    private void OnServiceError(string message)
    {
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            StatusMessage = message;
        });
    }

    /// <summary>
    /// 現在の状態を設定オブジェクトに反映して保存します
    /// </summary>
    public void SaveSettings(double left = -1, double top = -1, double width = -1, double height = -1, bool isMaximized = false)
    {
        if (left >= 0) _appSettings.WindowLeft = left;
        if (top >= 0) _appSettings.WindowTop = top;
        if (width > 0) _appSettings.WindowWidth = width;
        if (height > 0) _appSettings.WindowHeight = height;
        _appSettings.IsMaximized = isMaximized;

        _appSettings.SelectedCameraDevice = SelectedCameraDevice?.Name;
        _appSettings.CameraMirror = IsCameraMirror;
        _appSettings.SelectedCaptureSource = SelectedCaptureSource?.Title;
        _appSettings.SelectedAudioDevice = SelectedAudioDevice?.Name;
        _appSettings.AudioMuted = IsAudioMuted;
        _appSettings.Language = CurrentLanguage;

        _settingsService.Save(_appSettings);
    }

    public AppSettings GetCurrentSettings() => _appSettings;

    private void OnCompositeFrameArrived(BitmapSource bitmap)
    {
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            StreamPreviewImage = bitmap;
        }, System.Windows.Threading.DispatcherPriority.Render);
    }

    private void OnCompositeStatusChanged(int fps, int width, int height)
    {
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            StreamStatusText = $"{width}x{height} @ {fps} FPS";
        });
    }

    public void Dispose()
    {
        _compositorService.Dispose();
        _cameraService.Dispose();
        _screenService.Dispose();
        _audioService.Dispose();
    }
}
