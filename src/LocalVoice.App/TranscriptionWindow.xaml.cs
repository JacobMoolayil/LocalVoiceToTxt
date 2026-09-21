using System;
using System.Collections.Generic;
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

        private bool _isPopulating = false;

        public TranscriptionWindow()
        {
            InitializeComponent();
            this.MouseLeftButtonDown += (s, e) => { this.DragMove(); };
        }

        public void PopulateDevices(List<AudioDeviceInfo> devices, int selectedId)
        {
            Dispatcher.Invoke(() =>
            {
                _isPopulating = true;
                try
                {
                    MicComboBox.ItemsSource = devices;
                    MicComboBox.SelectedValue = selectedId;
                    if (MicComboBox.SelectedIndex == -1 && devices.Count > 0)
                    {
                        MicComboBox.SelectedIndex = 0;
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
                Console.WriteLine($"[UI] Microphone changed by user to: ID {deviceId}");
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
