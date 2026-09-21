using System;
using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using LocalVoice.App.Services;

namespace LocalVoice.App
{
    public partial class App : System.Windows.Application
    {
        private EngineProcessService _engine;
        private GlobalHotkeyService _hotkey;
        private TextInjectionService _injector;
        
        private TranscriptionWindow _transcriptionWindow;
        private NotifyIcon _trayIcon;
        private Window _hiddenHotkeyWindow;

        private bool _isRecording = false;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 1. Create a hidden WPF window just to host the global hotkey message loop
            _hiddenHotkeyWindow = new Window { Width = 0, Height = 0, WindowStyle = WindowStyle.None, ShowInTaskbar = false, Visibility = Visibility.Hidden };
            _hiddenHotkeyWindow.Show();
            var hwnd = new WindowInteropHelper(_hiddenHotkeyWindow).Handle;

            // 2. Initialize Services
            _engine = new EngineProcessService();
            _hotkey = new GlobalHotkeyService();
            _injector = new TextInjectionService();

            _transcriptionWindow = new TranscriptionWindow();
            _transcriptionWindow.OnStopRequested += StopRecording;

            // Wire up IPC events
            _engine.OnInterimText += (text) => _transcriptionWindow.SetInterimText(text);
            _engine.OnCommittedText += HandleCommittedText;
            _engine.OnVadStart += () => _transcriptionWindow.SetStatus(true);
            _engine.OnVadEnd += () => _transcriptionWindow.SetStatus(false);

            // Start Python Engine (this will boot the model in the background)
            _engine.StartEngine();

            // Register Hotkey (Ctrl + Shift + Space)
            _hotkey.OnHotKeyPressed += ToggleRecording;
            _hotkey.Register(hwnd);

            // 3. Setup System Tray
            SetupSystemTray();
        }

        private void SetupSystemTray()
        {
            _trayIcon = new NotifyIcon
            {
                Icon = SystemIcons.Application,
                Visible = true,
                Text = "LocalVoice (Ctrl+Shift+Space to Dictate)"
            };

            var menu = new ContextMenuStrip();
            menu.Items.Add("Show Transcription Window", null, (s, e) => _transcriptionWindow.Show());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, (s, e) => Shutdown());

            _trayIcon.ContextMenuStrip = menu;
            _trayIcon.DoubleClick += (s, e) => _transcriptionWindow.Show();
        }

        private void ToggleRecording()
        {
            if (_isRecording)
            {
                StopRecording();
            }
            else
            {
                StartRecording();
            }
        }

        private void StartRecording()
        {
            _isRecording = true;
            _transcriptionWindow.SetStatus(true);
            
            // Check if we should show the floating window
            // In a real robust implementation, we'd check GetForegroundWindow() and UIA here
            // For now, if the transcription window is visible, we route there. If not, we just inject.
            // Let's show the transcription window briefly to indicate listening if it isn't visible,
            // or we could just trust the user is focused on a text box.
            
            _engine.SendCommand("start");
        }

        private void StopRecording()
        {
            _isRecording = false;
            _transcriptionWindow.SetStatus(false);
            _engine.SendCommand("stop");
        }

        private void HandleCommittedText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;

            // If the floating window is visible and active, or if we determined no text field is focused,
            // we append to the floating window.
            // For now, as a solid V1, we will inject via keyboard AND show it on the floating window if open.
            
            Dispatcher.Invoke(() =>
            {
                if (_transcriptionWindow.IsVisible)
                {
                    _transcriptionWindow.AppendCommittedText(text);
                }
                
                // Inject via keyboard simulation (SendInput)
                // We use SendInput so it goes into whatever the user's caret is focused on!
                _injector.InjectText(text);
            });
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _hotkey?.Dispose();
            _engine?.Dispose();
            
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
            }
            
            base.OnExit(e);
        }
    }
}
