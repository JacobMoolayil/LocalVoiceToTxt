using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using LocalVoice.App.Services;

namespace LocalVoice.App
{
    public partial class App : System.Windows.Application
    {
        private EngineProcessService? _engine;
        private GlobalHotkeyService? _hotkey;
        private TextInjectionService? _injector;
        private AudioDeviceWatcher? _deviceWatcher;
        
        private TranscriptionWindow? _transcriptionWindow;
        private NotifyIcon? _trayIcon;

        private bool _isRecording = false;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            this.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            try
            {
                // Ensure Console.WriteLine never throws 'handle is invalid' in GUI mode
                if (!AppSettingsService.GetTerminalSetting())
                {
                    try
                    {
                        Console.SetOut(System.IO.TextWriter.Null);
                        Console.SetError(System.IO.TextWriter.Null);
                    }
                    catch { }
                }

                AppSettingsService.Log("Initializing LocalVoice UI...");

                // 1. Initialize Main UI Window
                _transcriptionWindow = new TranscriptionWindow();
                _transcriptionWindow.OnStopRequested += StopRecording;

                // Ensure the Win32 handle is fully created
                var helper = new WindowInteropHelper(_transcriptionWindow);
                IntPtr hwnd = helper.EnsureHandle();
                AppSettingsService.Log($"Main Window Handle: {hwnd}");

                // 2. Initialize Services
                _engine = new EngineProcessService();
                _hotkey = new GlobalHotkeyService();
                _injector = new TextInjectionService();
                _deviceWatcher = new AudioDeviceWatcher();

                // Wire up Win32 WM_DEVICECHANGE hardware notification hook directly
                var hwndSource = HwndSource.FromHwnd(hwnd);
                hwndSource?.AddHook((IntPtr h, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
                {
                    const int WM_DEVICECHANGE = 0x0219;
                    if (msg == WM_DEVICECHANGE)
                    {
                        AppSettingsService.Log("[App] WM_DEVICECHANGE received via Win32 hook.");
                        _deviceWatcher?.NotifyDeviceChanged();
                    }
                    return IntPtr.Zero;
                });

                // Wire up IPC events from Python
                _engine.OnInterimText += (text) => _transcriptionWindow.SetInterimText(text);
                _engine.OnCommittedText += HandleCommittedText;
                _engine.OnVadStart += () => _transcriptionWindow.SetStatus(true);
                _engine.OnVadEnd += () => _transcriptionWindow.SetStatus(false);
                _engine.OnDeviceListReceived += (devs, selId) => _transcriptionWindow.PopulateDevices(devs, selId);

                _transcriptionWindow.OnDeviceSelected += (id) => _engine.SetDevice(id);
                _transcriptionWindow.OnRefreshDevicesRequested += () => _engine.RequestDeviceList();

                // Wire up audio device watcher (CoreAudio endpoint & default device changes)
                _deviceWatcher.AudioDevicesChanged += () =>
                {
                    AppSettingsService.Log("[App] Refreshing audio devices after system device notification.");
                    _engine.RequestDeviceList();
                };

                // Start Python AI Engine
                _engine.StartEngine();

                // Register Global Hotkey (Ctrl + Shift + Space)
                _hotkey.OnHotKeyPressed += ToggleRecording;
                _hotkey.Register(hwnd);

                // 3. Setup System Tray with custom drawn microphone icon
                SetupSystemTray();

                // 4. Show the Transcription Window on startup
                _transcriptionWindow.Show();
                _transcriptionWindow.SetStatus(false);

                // Apply saved terminal visibility setting (creates console on-demand if enabled)
                bool showTerminal = AppSettingsService.GetTerminalSetting();
                AppSettingsService.ApplyConsoleState(showTerminal);

                AppSettingsService.Log("LocalVoice started successfully!");
            }
            catch (Exception ex)
            {
                AppSettingsService.Log("FATAL ERROR: " + ex.ToString());
                try { System.IO.File.WriteAllText("crash.log", ex.ToString()); } catch { }
                System.Windows.MessageBox.Show("Fatal Error: " + ex.Message);
                Shutdown();
            }
        }

        private void SetupSystemTray()
        {
            _trayIcon = new NotifyIcon
            {
                Icon = CreateMicrophoneIcon(),
                Visible = true,
                Text = "LocalVoice (Ctrl+Shift+Space to Dictate)"
            };

            var menu = new ContextMenuStrip();
            menu.Items.Add("Show / Hide Window", null, (s, e) =>
            {
                if (_transcriptionWindow != null)
                {
                    if (_transcriptionWindow.IsVisible) _transcriptionWindow.Hide();
                    else _transcriptionWindow.Show();
                }
            });
            menu.Items.Add("Toggle Terminal", null, (s, e) =>
            {
                bool cur = AppSettingsService.GetTerminalSetting();
                AppSettingsService.SetTerminalSetting(!cur);
            });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, (s, e) => Shutdown());

            _trayIcon.ContextMenuStrip = menu;
            _trayIcon.DoubleClick += (s, e) =>
            {
                if (_transcriptionWindow != null)
                {
                    _transcriptionWindow.Show();
                    _transcriptionWindow.Activate();
                }
            };
        }

        private Icon CreateMicrophoneIcon()
        {
            var bmp = new Bitmap(32, 32);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                
                // Blue badge background
                using (var brush = new SolidBrush(Color.FromArgb(37, 99, 235)))
                {
                    g.FillEllipse(brush, 1, 1, 30, 30);
                }

                // White mic capsule
                using (var brush = new SolidBrush(Color.White))
                {
                    g.FillRoundedRectangle(brush, new Rectangle(12, 6, 8, 14), new System.Drawing.Size(4, 4));
                }

                // White mic cradle
                using (var pen = new Pen(Color.White, 2))
                {
                    g.DrawArc(pen, 9, 10, 14, 12, 0, 180);
                    g.DrawLine(pen, 16, 22, 16, 26);
                    g.DrawLine(pen, 12, 26, 20, 26);
                }
            }
            return Icon.FromHandle(bmp.GetHicon());
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private IntPtr _targetHwnd = IntPtr.Zero;

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
            _targetHwnd = GetForegroundWindow();
            AppSettingsService.Log($"[Focus] Target Window captured: {_targetHwnd}");

            _isRecording = true;
            try { System.Media.SystemSounds.Asterisk.Play(); } catch { }

            if (_transcriptionWindow != null)
            {
                _transcriptionWindow.Show();
                _transcriptionWindow.SetStatus(true);
            }

            _engine?.SendCommand("start");
        }

        private void StopRecording()
        {
            _isRecording = false;
            try { System.Media.SystemSounds.Beep.Play(); } catch { }

            if (_transcriptionWindow != null)
            {
                _transcriptionWindow.SetStatus(false);
            }

            _engine?.SendCommand("stop");
        }

        private void HandleCommittedText(string text)
        {
            AppSettingsService.Log($"[App] HandleCommittedText received: '{text}'");
            if (string.IsNullOrWhiteSpace(text)) return;

            Dispatcher.Invoke(() =>
            {
                if (_transcriptionWindow != null)
                {
                    _transcriptionWindow.AppendCommittedText(text);
                    if (!_transcriptionWindow.IsVisible) _transcriptionWindow.Show();
                }
                
                // Return focus to target application before pasting
                if (_targetHwnd != IntPtr.Zero)
                {
                    AppSettingsService.Log($"[Focus] Restoring focus to target window: {_targetHwnd}");
                    SetForegroundWindow(_targetHwnd);
                    System.Threading.Thread.Sleep(70);
                }

                // Inject via Safe Clipboard (Ctrl+V)
                _injector?.InjectText(text);
            });
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _deviceWatcher?.Dispose();
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

    public static class GraphicsExtensions
    {
        public static void FillRoundedRectangle(this Graphics g, Brush brush, Rectangle bounds, System.Drawing.Size cornerRadius)
        {
            using (var path = new GraphicsPath())
            {
                int arcWidth = cornerRadius.Width * 2;
                int arcHeight = cornerRadius.Height * 2;

                path.AddArc(bounds.X, bounds.Y, arcWidth, arcHeight, 180, 90);
                path.AddArc(bounds.Right - arcWidth, bounds.Y, arcWidth, arcHeight, 270, 90);
                path.AddArc(bounds.Right - arcWidth, bounds.Bottom - arcHeight, arcWidth, arcHeight, 0, 90);
                path.AddArc(bounds.X, bounds.Bottom - arcHeight, arcWidth, arcHeight, 90, 90);
                path.CloseFigure();

                g.FillPath(brush, path);
            }
        }
    }
}
