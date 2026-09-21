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
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        [DllImport("user32.dll")]
        public static extern IntPtr SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        public event Action OnStopRequested;

        public TranscriptionWindow()
        {
            InitializeComponent();
            this.SourceInitialized += TranscriptionWindow_SourceInitialized;
            this.MouseLeftButtonDown += (s, e) => { this.DragMove(); };
        }

        private void TranscriptionWindow_SourceInitialized(object? sender, EventArgs e)
        {
            // Apply WS_EX_NOACTIVATE so the window doesn't steal focus from active applications
            var helper = new WindowInteropHelper(this);
            int exStyle = GetWindowLong(helper.Handle, GWL_EXSTYLE);
            SetWindowLong(helper.Handle, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        }

        public void SetStatus(bool isRecording)
        {
            Dispatcher.Invoke(() =>
            {
                StatusText.Text = isRecording ? "Listening..." : "Idle";
                StatusDot.Fill = isRecording ? new SolidColorBrush(Colors.Red) : new SolidColorBrush(Colors.Gray);
            });
        }

        public void AppendCommittedText(string text)
        {
            Dispatcher.Invoke(() =>
            {
                CommittedTextBlock.Text += text + " ";
                InterimTextBlock.Text = ""; // Clear interim when committed
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
                System.Windows.Clipboard.SetText(CommittedTextBlock.Text.Trim());
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
