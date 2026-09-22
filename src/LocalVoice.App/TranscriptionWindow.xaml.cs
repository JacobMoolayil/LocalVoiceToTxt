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
using LocalVoice.App.Services;

namespace LocalVoice.App
{
    public partial class TranscriptionWindow : Window
    {
        public event Action? OnStopRequested;
        public event Action<int>? OnDeviceSelected;
        public event Action? OnRefreshDevicesRequested;

        private readonly ObservableCollection<AudioDeviceInfo> _deviceCollection = new();
        private bool _isPopulating = false;

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
                    // by cycling SelectedValue through null while _isPopulating is true
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

        public void SetStatus(bool isRecording)
        {
            Dispatcher.Invoke(() =>
            {
                StatusText.Text = isRecording ? "Listening..." : "Idle";
                var hexColor = isRecording ? "#EF4444" : "#10B981";
                var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hexColor);
                StatusDot.Fill = new SolidColorBrush(color);
            });
        }

        public void AppendCommittedText(string text)
        {
            Dispatcher.Invoke(() =>
            {
                CommittedTextBlock.Text += text + " ";
                InterimTextBlock.Text = "";
            });
        }

        public void SetInterimText(string text)
        {
            Dispatcher.Invoke(() =>
            {
                InterimTextBlock.Text = text;
            });
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            CommittedTextBlock.Text = "";
            InterimTextBlock.Text = "";
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
            OnStopRequested?.Invoke();
            this.Hide();
        }
    }
}
