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
    public partial class TranscriptionWindow : Window
    {
        public event Action? OnToggleListeningRequested;
        public event Action<int>? OnDeviceSelected;
        public event Action? OnRefreshDevicesRequested;

        private static readonly BrushConverter _brushConverter = new();
        private static Brush HexBrush(string hex) => (Brush)_brushConverter.ConvertFromString(hex)!;

        private readonly ObservableCollection<AudioDeviceInfo> _deviceCollection = new();
        private bool _isPopulating = false;
        private bool _isListening = false;
        private bool _isLoading = true;

        public TranscriptionWindow()
        {
            InitializeComponent();
            MicComboBox.ItemsSource = _deviceCollection;
            MicComboBox.DropDownOpened += MicComboBox_DropDownOpened;
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
            // Initialize checkbox states from registry/settings
            try
            {
                ChkStartup.IsChecked = AppSettingsService.IsStartupEnabled();
                ChkTerminal.IsChecked = AppSettingsService.GetTerminalSetting();
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

                    if (string.IsNullOrWhiteSpace(CommittedTextBlock.Text) && string.IsNullOrWhiteSpace(InterimTextBlock.Text))
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

                    if (string.IsNullOrWhiteSpace(CommittedTextBlock.Text) && string.IsNullOrWhiteSpace(InterimTextBlock.Text))
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

                    if (string.IsNullOrWhiteSpace(CommittedTextBlock.Text) && string.IsNullOrWhiteSpace(InterimTextBlock.Text))
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
                CommittedTextBlock.Text += text + " ";
                InterimTextBlock.Text = "";
                EmptyHintTextBlock.Visibility = Visibility.Collapsed;
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
                }
                else if (string.IsNullOrWhiteSpace(CommittedTextBlock.Text))
                {
                    EmptyHintTextBlock.Visibility = Visibility.Visible;
                }
            });
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            CommittedTextBlock.Text = "";
            InterimTextBlock.Text = "";
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
                if (!string.IsNullOrWhiteSpace(CommittedTextBlock.Text))
                {
                    System.Windows.Clipboard.SetText(CommittedTextBlock.Text.Trim());
                }
            }
            catch { }
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            this.Hide();
        }
    }
}
