using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace LocalVoice.App
{
    public partial class TranscriptionWindow : Window
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        [DllImport("user32.dll")]
        public static extern IntPtr SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        public event Action? OnStopRequested;

        public TranscriptionWindow()
        {
            InitializeComponent();
            this.MouseLeftButtonDown += (s, e) => { this.DragMove(); };
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
            catch { /* Ignore clipboard locks */ }
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            OnStopRequested?.Invoke();
            this.Hide();
        }
    }
}
