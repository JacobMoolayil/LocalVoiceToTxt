using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using LocalVoice.App.Services;

namespace LocalVoice.App
{
    public class WhisperModelOption : System.ComponentModel.INotifyPropertyChanged
    {
        private bool _isAvailable = true;
        private string _displayName = "";
        private string _resourceHint = "";
        private string _tooltipText = "";

        public string ModelKey { get; set; } = "";
        public string BaseName { get; set; } = "";
        public double RequiredVramGb { get; set; } = 0;
        public double RequiredRamGb { get; set; } = 0;

        public bool IsAvailable
        {
            get => _isAvailable;
            set
            {
                if (_isAvailable != value)
                {
                    _isAvailable = value;
                    OnPropertyChanged();
                }
            }
        }

        public string DisplayName
        {
            get => _displayName;
            set
            {
                if (_displayName != value)
                {
                    _displayName = value;
                    OnPropertyChanged();
                }
            }
        }

        public string ResourceHint
        {
            get => _resourceHint;
            set
            {
                if (_resourceHint != value)
                {
                    _resourceHint = value;
                    OnPropertyChanged();
                }
            }
        }

        public string TooltipText
        {
            get => _tooltipText;
            set
            {
                if (_tooltipText != value)
                {
                    _tooltipText = value;
                    OnPropertyChanged();
                }
            }
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
        }
    }

    public partial class TranscriptionWindow : Window
    {
        public event Action? OnToggleListeningRequested;
        public event Action<int>? OnDeviceSelected;
        public event Action<string>? OnModelSelected;
        public event Action? OnRefreshDevicesRequested;

        private static readonly BrushConverter _brushConverter = new();
        private static Brush HexBrush(string hex) => (Brush)_brushConverter.ConvertFromString(hex)!;

        private readonly ObservableCollection<AudioDeviceInfo> _deviceCollection = new();
        private readonly ObservableCollection<WhisperModelOption> _modelOptions = new()
        {
            new WhisperModelOption 
            { 
                ModelKey = "auto", 
                BaseName = "Auto (Recommended)", 
                DisplayName = "Auto (Recommended)", 
                RequiredVramGb = 0, 
                RequiredRamGb = 0, 
                ResourceHint = "Auto: Automatically picks balanced model for your hardware (~2 GB VRAM / RAM)",
                TooltipText = "Auto: Automatically selects the best supported model for your hardware."
            },
            new WhisperModelOption 
            { 
                ModelKey = "tiny", 
                BaseName = "Tiny (~1 GB RAM / VRAM)", 
                DisplayName = "Tiny (~1 GB RAM / VRAM)", 
                RequiredVramGb = 1.0, 
                RequiredRamGb = 1.5, 
                ResourceHint = "Tiny: Ultra-fast, lowest accuracy (~1 GB VRAM / RAM)",
                TooltipText = "Tiny: Ultra-fast inference with minimal memory footprint (~1 GB VRAM / RAM)."
            },
            new WhisperModelOption 
            { 
                ModelKey = "base", 
                BaseName = "Base (~1.5 GB RAM / VRAM)", 
                DisplayName = "Base (~1.5 GB RAM / VRAM)", 
                RequiredVramGb = 1.5, 
                RequiredRamGb = 2.0, 
                ResourceHint = "Base: Very fast, acceptable accuracy (~1.5 GB VRAM / RAM)",
                TooltipText = "Base: Fast inference with good dictation accuracy (~1.5 GB VRAM / RAM)."
            },
            new WhisperModelOption 
            { 
                ModelKey = "small", 
                BaseName = "Small (~2 GB RAM / VRAM)", 
                DisplayName = "Small (~2 GB RAM / VRAM)", 
                RequiredVramGb = 2.0, 
                RequiredRamGb = 3.5, 
                ResourceHint = "Small: Default balanced model for speed and accuracy (~2 GB VRAM / RAM)",
                TooltipText = "Small: Optimal balance of speed and high accuracy (~2 GB VRAM / RAM)."
            },
            new WhisperModelOption 
            { 
                ModelKey = "medium", 
                BaseName = "Medium (~5 GB RAM / VRAM)", 
                DisplayName = "Medium (~5 GB RAM / VRAM)", 
                RequiredVramGb = 5.0, 
                RequiredRamGb = 6.0, 
                ResourceHint = "Medium: High accuracy, heavier resource usage (~5 GB VRAM / RAM)",
                TooltipText = "Medium: High accuracy model (~5 GB VRAM)."
            },
            new WhisperModelOption 
            { 
                ModelKey = "turbo", 
                BaseName = "Turbo (~6 GB RAM / VRAM)", 
                DisplayName = "Turbo (~6 GB RAM / VRAM)", 
                RequiredVramGb = 6.0, 
                RequiredRamGb = 8.0, 
                ResourceHint = "Turbo: Large-v3-Turbo. High accuracy & optimized speed (~6 GB VRAM)",
                TooltipText = "Turbo: Large-v3-Turbo with accelerated speed (~6 GB VRAM)."
            },
            new WhisperModelOption 
            { 
                ModelKey = "large-v3", 
                BaseName = "Large v3 (~10 GB RAM / VRAM)", 
                DisplayName = "Large v3 (~10 GB RAM / VRAM)", 
                RequiredVramGb = 10.0, 
                RequiredRamGb = 12.0, 
                ResourceHint = "Large v3: Maximum accuracy, highest resource usage (~10 GB VRAM)",
                TooltipText = "Large v3: Highest accuracy Whisper model (~10 GB VRAM)."
            }
        };

        private bool _isPopulating = false;
        private bool _isListening = false;
        private bool _isLoading = true;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private class MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
            public MEMORYSTATUSEX() { this.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX)); }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

        private static double DetectSystemRamGb()
        {
            try
            {
                var memStatus = new MEMORYSTATUSEX();
                if (GlobalMemoryStatusEx(memStatus))
                {
                    return Math.Round(memStatus.ullTotalPhys / (1024.0 * 1024.0 * 1024.0), 1);
                }
            }
            catch { }
            return 8.0;
        }

        private static double DetectGpuVramGb()
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("nvidia-smi", "--query-gpu=memory.total --format=csv,noheader,nounits")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = System.Diagnostics.Process.Start(psi);
                if (p != null)
                {
                    string outStr = p.StandardOutput.ReadToEnd().Trim();
                    if (p.WaitForExit(800))
                    {
                        var lines = outStr.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        if (lines.Length > 0 && double.TryParse(lines[0].Trim(), out double mb))
                        {
                            return Math.Round(mb / 1024.0, 1);
                        }
                    }
                }
            }
            catch { }
            return 0.0;
        }

        private void DetectInitialHardware()
        {
            try
            {
                double vramGb = DetectGpuVramGb();
                double ramGb = DetectSystemRamGb();
                bool isGpu = vramGb > 0;
                string hwName = isGpu ? "NVIDIA GPU" : "CPU";
                UpdateModelAvailability(isGpu, vramGb, ramGb, hwName);
            }
            catch (Exception ex)
            {
                AppSettingsService.Log($"[UI] Initial hardware detection error: {ex.Message}");
            }
        }

        public void UpdateModelAvailability(bool isGpu, double vramGb, double ramGb, string hwName = "")
        {
            Dispatcher.Invoke(() =>
            {
                string hwDesc = isGpu 
                    ? (!string.IsNullOrWhiteSpace(hwName) ? $"{hwName} ({vramGb:F1} GB VRAM)" : $"{vramGb:F1} GB VRAM")
                    : (!string.IsNullOrWhiteSpace(hwName) ? $"{hwName} ({ramGb:F1} GB RAM)" : $"{ramGb:F1} GB RAM");

                foreach (var opt in _modelOptions)
                {
                    if (opt.ModelKey == "auto")
                    {
                        opt.IsAvailable = true;
                        opt.DisplayName = opt.BaseName;
                        opt.TooltipText = $"Auto: Selects optimal model for your device ({hwDesc})";
                        continue;
                    }

                    if (isGpu && vramGb > 0)
                    {
                        bool fits = vramGb >= opt.RequiredVramGb;
                        opt.IsAvailable = fits;
                        if (fits)
                        {
                            opt.DisplayName = opt.BaseName;
                            opt.TooltipText = $"{opt.BaseName}\nSupported on your GPU ({hwDesc})";
                        }
                        else
                        {
                            opt.DisplayName = opt.BaseName;
                            opt.TooltipText = $"Unsupported on this device: Requires {opt.RequiredVramGb:F1} GB+ VRAM.\nYour GPU has {vramGb:F1} GB VRAM.";
                        }
                    }
                    else
                    {
                        bool fits = ramGb >= opt.RequiredRamGb;
                        opt.IsAvailable = fits;
                        if (fits)
                        {
                            opt.DisplayName = opt.BaseName;
                            opt.TooltipText = $"{opt.BaseName}\nSupported on your CPU ({hwDesc})";
                        }
                        else
                        {
                            opt.DisplayName = opt.BaseName;
                            opt.TooltipText = $"Unsupported on this device: Requires {opt.RequiredRamGb:F1} GB+ RAM.\nYour system has {ramGb:F1} GB RAM.";
                        }
                    }
                }

                // If currently selected model is now unavailable, revert to 'auto'
                if (ModelComboBox.SelectedItem is WhisperModelOption selected && !selected.IsAvailable)
                {
                    var fallback = _modelOptions.FirstOrDefault(m => m.IsAvailable) ?? _modelOptions[0];
                    ModelComboBox.SelectedItem = fallback;
                    AppSettingsService.SetWhisperModelSetting(fallback.ModelKey);
                    AppSettingsService.Log($"[UI] Selected model '{selected.ModelKey}' is incompatible with detected hardware ({hwDesc}). Reverted to '{fallback.ModelKey}'.");
                }
            });
        }

        public TranscriptionWindow()
        {
            InitializeComponent();
            MicComboBox.ItemsSource = _deviceCollection;
            MicComboBox.DropDownOpened += MicComboBox_DropDownOpened;
            ModelComboBox.ItemsSource = _modelOptions;
            DetectInitialHardware();
            SettingsPopup.Opened += SettingsPopup_Opened;
            this.PreviewMouseDown += TranscriptionWindow_PreviewMouseDown;
            this.Deactivated += (s, e) => SettingsPopup.IsOpen = false;
            this.MouseLeftButtonDown += (s, e) => { this.DragMove(); };
            this.Loaded += TranscriptionWindow_Loaded;
            this.IsVisibleChanged += TranscriptionWindow_IsVisibleChanged;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            try
            {
                var helper = new WindowInteropHelper(this);
                var source = HwndSource.FromHwnd(helper.EnsureHandle());
                source?.AddHook(WndProc);
            }
            catch (Exception ex)
            {
                AppSettingsService.Log($"[UI] Error adding WndProc hook: {ex.Message}");
            }
        }

        private const int WM_DEVICECHANGE = 0x0219;
        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_DEVICECHANGE)
            {
                AppSettingsService.Log("[UI] WM_DEVICECHANGE received. Requesting audio device refresh.");
                OnRefreshDevicesRequested?.Invoke();
            }
            return IntPtr.Zero;
        }

        private void TranscriptionWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Initialize checkbox states and model selection from registry/settings
            try
            {
                ChkStartup.IsChecked = AppSettingsService.IsStartupEnabled();
                ChkTerminal.IsChecked = AppSettingsService.GetTerminalSetting();

                string savedModel = AppSettingsService.GetWhisperModelSetting();
                var matching = _modelOptions.FirstOrDefault(m => m.ModelKey.Equals(savedModel, StringComparison.OrdinalIgnoreCase));
                if (matching == null || !matching.IsAvailable)
                {
                    matching = _modelOptions.FirstOrDefault(m => m.IsAvailable) ?? _modelOptions[0];
                    AppSettingsService.SetWhisperModelSetting(matching.ModelKey);
                }

                ModelComboBox.SelectedItem = matching;
                ModelResourceHintText.Text = matching.ResourceHint;
            }
            catch { }
        }

        private void TranscriptionWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (this.IsVisible)
            {
                OnRefreshDevicesRequested?.Invoke();
            }
        }

        private void MicComboBox_DropDownOpened(object? sender, EventArgs e)
        {
            OnRefreshDevicesRequested?.Invoke();
        }

        private void TranscriptionWindow_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (SettingsPopup.IsOpen)
            {
                // 1. If clicking on or within the Settings toggle button, let BtnSettings_Click handle it
                if (BtnSettings.IsMouseOver)
                    return;

                System.Windows.Point ptBtn = e.GetPosition(BtnSettings);
                if (ptBtn.X >= 0 && ptBtn.X <= BtnSettings.ActualWidth &&
                    ptBtn.Y >= 0 && ptBtn.Y <= BtnSettings.ActualHeight)
                    return;

                // 2. If clicking inside the SettingsPopup panel, do not close!
                if (SettingsPopup.Child is FrameworkElement popupContent)
                {
                    if (popupContent.IsMouseOver)
                        return;

                    System.Windows.Point ptPopup = e.GetPosition(popupContent);
                    if (ptPopup.X >= 0 && ptPopup.X <= popupContent.ActualWidth &&
                        ptPopup.Y >= 0 && ptPopup.Y <= popupContent.ActualHeight)
                        return;

                    // If a ComboBox dropdown inside the popup is currently open, keep open
                    if (MicComboBox.IsDropDownOpen || ModelComboBox.IsDropDownOpen)
                        return;

                    if (e.OriginalSource is DependencyObject dep && IsDescendantOf(dep, popupContent))
                        return;
                }

                // 3. Clicked anywhere else outside the popup and button -> close popup
                SettingsPopup.IsOpen = false;
            }
        }

        private static bool IsDescendantOf(DependencyObject element, DependencyObject parent)
        {
            DependencyObject? current = element;
            while (current != null)
            {
                if (current == parent) return true;
                DependencyObject? next = null;
                if (current is Visual || current is System.Windows.Media.Media3D.Visual3D)
                {
                    try { next = VisualTreeHelper.GetParent(current); } catch { }
                }
                if (next == null)
                {
                    next = LogicalTreeHelper.GetParent(current);
                }
                current = next;
            }
            return false;
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            // Clean, deterministic toggle
            SettingsPopup.IsOpen = !SettingsPopup.IsOpen;
        }

        private void SettingsPopup_Opened(object? sender, EventArgs e)
        {
            OnRefreshDevicesRequested?.Invoke();
        }

        private void UpdateCurrentMicLabel()
        {
            if (MicComboBox.SelectedItem is AudioDeviceInfo device)
            {
                CurrentMicText.Text = device.Name;
            }
            else
            {
                CurrentMicText.Text = "No device";
            }
        }

        private void ChkStartup_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;
            bool enable = ChkStartup.IsChecked == true;
            AppSettingsService.SetStartup(enable);
        }

        private void ChkTerminal_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;
            bool show = ChkTerminal.IsChecked == true;
            AppSettingsService.SetTerminalSetting(show);
        }

        private void ModelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ModelComboBox.SelectedItem is WhisperModelOption option)
            {
                if (!option.IsAvailable)
                {
                    var fallback = _modelOptions.FirstOrDefault(m => m.IsAvailable) ?? _modelOptions[0];
                    ModelComboBox.SelectedItem = fallback;
                    return;
                }

                ModelResourceHintText.Text = option.ResourceHint;
                if (!IsLoaded) return;

                string currentSaved = AppSettingsService.GetWhisperModelSetting();
                if (!string.Equals(currentSaved, option.ModelKey, StringComparison.OrdinalIgnoreCase))
                {
                    AppSettingsService.SetWhisperModelSetting(option.ModelKey);
                    AppSettingsService.Log($"[UI] User switched Whisper model to: {option.ModelKey}");
                    OnModelSelected?.Invoke(option.ModelKey);
                }
            }
        }

        public void PopulateDevices(List<AudioDeviceInfo> devices, int selectedId)
        {
            Dispatcher.Invoke(() =>
            {
                _isPopulating = true;
                try
                {
                    // 1. Update existing or add new devices
                    foreach (var newDev in devices)
                    {
                        var existing = _deviceCollection.FirstOrDefault(d => d.Id == newDev.Id);
                        if (existing != null)
                        {
                            existing.Name = newDev.Name;
                            existing.IsDefault = newDev.IsDefault;
                        }
                        else
                        {
                            _deviceCollection.Add(new AudioDeviceInfo
                            {
                                Id = newDev.Id,
                                Name = newDev.Name,
                                IsDefault = newDev.IsDefault
                            });
                        }
                    }

                    // 2. Remove devices no longer present
                    for (int i = _deviceCollection.Count - 1; i >= 0; i--)
                    {
                        if (!devices.Any(d => d.Id == _deviceCollection[i].Id))
                        {
                            _deviceCollection.RemoveAt(i);
                        }
                    }

                    // 3. Maintain or select appropriate device
                    int targetId = selectedId;
                    if (MicComboBox.SelectedValue is int currentSelection && _deviceCollection.Any(d => d.Id == currentSelection))
                    {
                        targetId = currentSelection;
                    }

                    // Force WPF to update the displayed text on the closed ComboBox header
                    MicComboBox.SelectedValue = null;

                    if (_deviceCollection.Any(d => d.Id == targetId))
                    {
                        MicComboBox.SelectedValue = targetId;
                    }
                    else if (_deviceCollection.Any(d => d.Id == -1))
                    {
                        MicComboBox.SelectedValue = -1;
                    }
                    else if (_deviceCollection.Count > 0)
                    {
                        MicComboBox.SelectedIndex = 0;
                    }

                    if (!MicComboBox.IsDropDownOpen)
                    {
                        try { MicComboBox.Items.Refresh(); } catch { }
                    }

                    // Sync the current mic name to the main window label
                    UpdateCurrentMicLabel();
                }
                finally
                {
                    _isPopulating = false;
                }
            });
        }

        private void MicComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isPopulating) return;

            if (MicComboBox.SelectedValue is int deviceId)
            {
                AppSettingsService.Log($"[UI] Microphone changed by user to: ID {deviceId}");
                OnDeviceSelected?.Invoke(deviceId);
            }

            // Sync the current mic name to the main window label
            UpdateCurrentMicLabel();
        }

        private void BtnToggleListening_Click(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            OnToggleListeningRequested?.Invoke();
        }

        public void SetLoadingState(bool isLoading, string message = "Loading AI Engine...")
        {
            Dispatcher.Invoke(() =>
            {
                _isLoading = isLoading;

                if (isLoading)
                {
                    LoadingProgressBar.Visibility = Visibility.Visible;
                    BtnToggleListening.IsEnabled = false;
                    BtnToggleListening.Background = HexBrush("#27272A");
                    BtnToggleListening.BorderBrush = HexBrush("#D97706");
                    ToggleBtnDot.Fill = HexBrush("#F59E0B");
                    ToggleBtnText.Text = "WAIT";
                    ToggleBtnText.Foreground = HexBrush("#F59E0B");
                    BtnToggleListening.ToolTip = "LocalVoice is initializing the AI model. Please wait...";

                    StatusDot.Fill = HexBrush("#F59E0B");
                    StatusText.Text = message;
                    StatusText.Foreground = HexBrush("#F59E0B");

                    if (string.IsNullOrWhiteSpace(CommittedTextBox.Text) && string.IsNullOrWhiteSpace(InterimTextBlock.Text))
                    {
                        EmptyHintTextBlock.Text = $"{message} Please wait a moment.";
                        EmptyHintTextBlock.Foreground = HexBrush("#D97706");
                        EmptyHintTextBlock.Visibility = Visibility.Visible;
                    }
                }
                else
                {
                    LoadingProgressBar.Visibility = Visibility.Collapsed;
                    BtnToggleListening.IsEnabled = true;
                    SetListeningState(_isListening);
                }
            });
        }

        public void SetListeningState(bool isListening)
        {
            Dispatcher.Invoke(() =>
            {
                _isListening = isListening;

                if (isListening)
                {
                    // ON State: Vibrant Emerald Green
                    BtnToggleListening.Background = HexBrush("#059669");
                    BtnToggleListening.BorderBrush = HexBrush("#10B981");
                    ToggleBtnDot.Fill = HexBrush("#FFFFFF");
                    ToggleBtnText.Text = "ON";
                    ToggleBtnText.Foreground = HexBrush("#FFFFFF");
                    BtnToggleListening.ToolTip = "Listening is ON. Click or press Ctrl+Shift+Space to turn OFF.";

                    StatusDot.Fill = HexBrush("#10B981");
                    StatusText.Text = "Listening...";
                    StatusText.Foreground = HexBrush("#F3F4F6");

                    if (string.IsNullOrWhiteSpace(CommittedTextBox.Text) && string.IsNullOrWhiteSpace(InterimTextBlock.Text))
                    {
                        EmptyHintTextBlock.Text = "Listening... Speak now and text will appear here.";
                        EmptyHintTextBlock.Foreground = HexBrush("#6EE7B7");
                        EmptyHintTextBlock.Visibility = Visibility.Visible;
                    }
                }
                else
                {
                    // OFF State: Muted Dark / Gray
                    BtnToggleListening.Background = HexBrush("#27272A");
                    BtnToggleListening.BorderBrush = HexBrush("#4B5563");
                    ToggleBtnDot.Fill = HexBrush("#9CA3AF");
                    ToggleBtnText.Text = "OFF";
                    ToggleBtnText.Foreground = HexBrush("#9CA3AF");
                    BtnToggleListening.ToolTip = "Listening is OFF. Click or press Ctrl+Shift+Space to turn ON.";

                    StatusDot.Fill = HexBrush("#6B7280");
                    StatusText.Text = "OFF (Not Listening)";
                    StatusText.Foreground = HexBrush("#9CA3AF");

                    if (string.IsNullOrWhiteSpace(CommittedTextBox.Text) && string.IsNullOrWhiteSpace(InterimTextBlock.Text))
                    {
                        EmptyHintTextBlock.Text = "Dictation is OFF. Press Ctrl+Shift+Space or click ON to start.";
                        EmptyHintTextBlock.Foreground = HexBrush("#6B7280");
                        EmptyHintTextBlock.Visibility = Visibility.Visible;
                    }
                }
            });
        }

        public void SetSpeechDetected(bool isSpeaking)
        {
            Dispatcher.Invoke(() =>
            {
                if (!_isListening) return;

                if (isSpeaking)
                {
                    StatusDot.Fill = HexBrush("#22C55E");
                    StatusText.Text = "Listening (Speaking...)";
                    StatusText.Foreground = HexBrush("#6EE7B7");
                }
                else
                {
                    StatusDot.Fill = HexBrush("#10B981");
                    StatusText.Text = "Listening...";
                    StatusText.Foreground = HexBrush("#F3F4F6");
                }
            });
        }

        public void SetStatus(bool isRecording)
        {
            SetListeningState(isRecording);
        }

        public void SetModelInfo(string model, string hardwareLabel)
        {
            Dispatcher.Invoke(() =>
            {
                WhisperModelText.Text = !string.IsNullOrWhiteSpace(model) ? model : "small";
                HardwareDeviceText.Text = !string.IsNullOrWhiteSpace(hardwareLabel) ? hardwareLabel : "GPU: RTX 3050 (CUDA FP16)";
                WhisperModelBorder.ToolTip = $"Whisper Model: {model}\nHardware: {hardwareLabel}";
            });
        }

        public void AppendCommittedText(string text)
        {
            Dispatcher.Invoke(() =>
            {
                CommittedTextBox.Text += text + " ";
                InterimTextBlock.Text = "";
                EmptyHintTextBlock.Visibility = Visibility.Collapsed;
                if (CommittedTextBox.SelectionLength == 0)
                {
                    TranscriptionScrollViewer.ScrollToEnd();
                }
            });
        }

        public void SetInterimText(string text)
        {
            Dispatcher.Invoke(() =>
            {
                InterimTextBlock.Text = text;
                if (!string.IsNullOrEmpty(text))
                {
                    EmptyHintTextBlock.Visibility = Visibility.Collapsed;
                    if (CommittedTextBox.SelectionLength == 0)
                    {
                        TranscriptionScrollViewer.ScrollToEnd();
                    }
                }
                else if (string.IsNullOrWhiteSpace(CommittedTextBox.Text))
                {
                    EmptyHintTextBlock.Visibility = Visibility.Visible;
                }
            });
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            CommittedTextBox.Text = "";
            InterimTextBlock.Text = "";
            BtnCopy.Content = "Copy All";
            EmptyHintTextBlock.Visibility = Visibility.Visible;
            if (_isLoading)
            {
                EmptyHintTextBlock.Text = "LocalVoice AI is loading... Please wait a moment.";
                EmptyHintTextBlock.Foreground = HexBrush("#D97706");
            }
            else if (_isListening)
            {
                EmptyHintTextBlock.Text = "Listening... Speak now and text will appear here.";
                EmptyHintTextBlock.Foreground = HexBrush("#6EE7B7");
            }
            else
            {
                EmptyHintTextBlock.Text = "Dictation is OFF. Press Ctrl+Shift+Space or click ON to start.";
                EmptyHintTextBlock.Foreground = HexBrush("#6B7280");
            }
        }

        private void BtnCopy_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!string.IsNullOrEmpty(CommittedTextBox.SelectedText))
                {
                    System.Windows.Clipboard.SetText(CommittedTextBox.SelectedText);
                }
                else if (!string.IsNullOrWhiteSpace(CommittedTextBox.Text))
                {
                    System.Windows.Clipboard.SetText(CommittedTextBox.Text.Trim());
                }
            }
            catch { }
        }

        private void CommittedTextBox_SelectionChanged(object sender, RoutedEventArgs e)
        {
            if (BtnCopy == null) return;
            if (!string.IsNullOrEmpty(CommittedTextBox.SelectedText))
            {
                BtnCopy.Content = "Copy";
            }
            else
            {
                BtnCopy.Content = "Copy All";
            }
        }

        private void CommittedTextBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (!e.Handled && TranscriptionScrollViewer != null)
            {
                e.Handled = true;
                var eventArg = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
                {
                    RoutedEvent = UIElement.MouseWheelEvent,
                    Source = sender
                };
                TranscriptionScrollViewer.RaiseEvent(eventArg);
            }
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            this.Hide();
        }
    }
}
