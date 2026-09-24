using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text;
using System.Windows;
using System.Windows.Automation;
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
        private bool _isEngineReady = false;

        private IntPtr _lastExternalHwnd = IntPtr.Zero;
        private IntPtr _winEventHook = IntPtr.Zero;
        private WinEventDelegate? _winEventDelegate;
        private uint _currentProcessId;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            this.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            try
            {
                _currentProcessId = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
                _winEventDelegate = WinEventProc;
                _winEventHook = SetWinEventHook(
                    EVENT_SYSTEM_FOREGROUND,
                    EVENT_SYSTEM_FOREGROUND,
                    IntPtr.Zero,
                    _winEventDelegate,
                    0,
                    0,
                    WINEVENT_OUTOFCONTEXT);

                IntPtr initialFg = GetForegroundWindow();
                if (initialFg != IntPtr.Zero)
                {
                    GetWindowThreadProcessId(initialFg, out uint fgPid);
                    if (fgPid != _currentProcessId)
                    {
                        _lastExternalHwnd = initialFg;
                    }
                }

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
                _engine.OnVadStart += () => _transcriptionWindow?.SetSpeechDetected(true);
                _engine.OnVadEnd += () => _transcriptionWindow?.SetSpeechDetected(false);
                _engine.OnDeviceListReceived += (devs, selId) => _transcriptionWindow.PopulateDevices(devs, selId);
                _engine.OnModelInfoReceived += (model, label) => _transcriptionWindow?.SetModelInfo(model, label);
                _engine.OnHardwareInfoReceived += (isGpu, vramGb, ramGb, hwName) => _transcriptionWindow?.UpdateModelAvailability(isGpu, vramGb, ramGb, hwName);

                _engine.OnEngineLoading += (msg) =>
                {
                    _transcriptionWindow?.SetLoadingState(true, msg);
                    if (_trayIcon != null) _trayIcon.Text = "LocalVoice - Loading AI Model...";
                };

                _engine.OnEngineReady += () =>
                {
                    _isEngineReady = true;
                    _transcriptionWindow?.SetLoadingState(false);
                    if (_trayIcon != null)
                    {
                        _trayIcon.Text = _isRecording ? "LocalVoice - ON (Listening)" : "LocalVoice - OFF (Ctrl+Shift+Space)";
                    }
                    AppSettingsService.Log("[App] Engine is READY.");
                };

                _transcriptionWindow.OnToggleListeningRequested += ToggleRecording;
                _transcriptionWindow.OnDeviceSelected += (id) => _engine.SetDevice(id);
                _transcriptionWindow.OnModelSelected += (model) => _engine.SetModel(model);
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

                // 4. Show the Transcription Window on startup in Loading state
                _transcriptionWindow.Show();
                _transcriptionWindow.SetLoadingState(true, "Loading AI Engine...");
                _engine.RequestModelInfo();

                // Ensure Python AI engine is cleanly terminated and GPU freed on process exit
                AppDomain.CurrentDomain.ProcessExit += (s, e) =>
                {
                    AppSettingsService.Log("[App] ProcessExit triggered. Ensuring engine cleanup.");
                    _engine?.Dispose();
                };

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
            menu.Items.Add("Exit", null, (s, e) =>
            {
                AppSettingsService.Log("[App] Exit requested from system tray.");
                _engine?.Dispose();
                Shutdown();
            });

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

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
        private const uint WINEVENT_OUTOFCONTEXT = 0x0000;

        private void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            if (hwnd == IntPtr.Zero) return;

            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid != 0 && pid != _currentProcessId)
            {
                _lastExternalHwnd = hwnd;
                AppSettingsService.Log($"[Focus] External target window updated: {hwnd}");
            }
        }

        private void RestoreFocusToWindow(IntPtr targetHwnd)
        {
            if (targetHwnd == IntPtr.Zero) return;

            try
            {
                IntPtr currentFg = GetForegroundWindow();
                if (currentFg == targetHwnd) return;

                uint currentThreadId = GetCurrentThreadId();
                uint targetThreadId = GetWindowThreadProcessId(targetHwnd, out _);

                bool attached = false;
                if (currentThreadId != targetThreadId && targetThreadId != 0)
                {
                    attached = AttachThreadInput(currentThreadId, targetThreadId, true);
                }

                try
                {
                    SetForegroundWindow(targetHwnd);
                    BringWindowToTop(targetHwnd);
                }
                finally
                {
                    if (attached)
                    {
                        AttachThreadInput(currentThreadId, targetThreadId, false);
                    }
                }
            }
            catch (Exception ex)
            {
                AppSettingsService.Log($"[Focus Error] {ex.Message}");
            }
        }

        private void ToggleRecording()
        {
            if (!_isEngineReady)
            {
                AppSettingsService.Log("[App] Toggle ignored: AI Engine is still loading.");
                try { System.Media.SystemSounds.Exclamation.Play(); } catch { }
                return;
            }

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
            IntPtr fg = GetForegroundWindow();
            if (fg != IntPtr.Zero)
            {
                GetWindowThreadProcessId(fg, out uint pid);
                if (pid != _currentProcessId)
                {
                    _lastExternalHwnd = fg;
                    AppSettingsService.Log($"[Focus] Initial external target window captured: {_lastExternalHwnd}");
                }
            }

            _isRecording = true;
            try { System.Media.SystemSounds.Asterisk.Play(); } catch { }

            if (_transcriptionWindow != null)
            {
                _transcriptionWindow.Show();
                _transcriptionWindow.SetListeningState(true);
            }

            if (_trayIcon != null)
            {
                _trayIcon.Text = "LocalVoice - ON (Listening)";
            }

            _engine?.SendCommand("start");
        }

        private void StopRecording()
        {
            _isRecording = false;
            try { System.Media.SystemSounds.Beep.Play(); } catch { }

            if (_transcriptionWindow != null)
            {
                _transcriptionWindow.SetListeningState(false);
            }

            if (_trayIcon != null)
            {
                _trayIcon.Text = "LocalVoice - OFF (Ctrl+Shift+Space)";
            }

            _engine?.SendCommand("stop");
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct GUITHREADINFO
        {
            public int cbSize;
            public int flags;
            public IntPtr hwndActive;
            public IntPtr hwndFocus;
            public IntPtr hwndCapture;
            public IntPtr hwndMenuOwner;
            public IntPtr hwndMoveSize;
            public IntPtr hwndCaret;
            public RECT rcCaret;
        }

        private const int GUI_CARETBLINKING = 0x00000001;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);

        private bool IsCaretOrEditableFocused(IntPtr fgHwnd)
        {
            if (fgHwnd == IntPtr.Zero) return false;

            // 1. Process check: if foreground belongs to LocalVoice, no external injection
            uint fgTid = GetWindowThreadProcessId(fgHwnd, out uint fgPid);
            if (fgPid == 0 || fgPid == _currentProcessId) return false;

            // 2. Class check: Desktop, Taskbar, and system shell surfaces never have carets
            var sbClass = new StringBuilder(256);
            GetClassName(fgHwnd, sbClass, 256);
            string cls = sbClass.ToString();
            if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd" || 
                cls == "Shell_SecondaryTrayWnd" || cls == "NotifyIconOverflowWindow")
            {
                return false;
            }

            // 3. Fast Win32 Caret check (GetGUIThreadInfo)
            try
            {
                var gui = new GUITHREADINFO();
                gui.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(gui);
                if (GetGUIThreadInfo(fgTid, ref gui))
                {
                    if (gui.hwndCaret != IntPtr.Zero) return true;
                    if ((gui.flags & GUI_CARETBLINKING) != 0) return true;
                    if (gui.rcCaret.Right > gui.rcCaret.Left && gui.rcCaret.Bottom > gui.rcCaret.Top) return true;

                    // If focused sub-window is a standard Edit/RichEdit control
                    if (gui.hwndFocus != IntPtr.Zero)
                    {
                        var sbFocusClass = new StringBuilder(128);
                        GetClassName(gui.hwndFocus, sbFocusClass, 128);
                        string fcls = sbFocusClass.ToString().ToLowerInvariant();
                        if (fcls.Contains("edit")) return true;
                    }
                }
            }
            catch { }

            // 4. UI Automation check (for modern browsers, Chrome, Electron, Slack, Discord, VS Code, WPF)
            try
            {
                var focused = AutomationElement.FocusedElement;
                if (focused != null)
                {
                    var ct = focused.Current.ControlType;
                    if (ct == ControlType.Edit)
                    {
                        // Check if readonly
                        if (focused.TryGetCurrentPattern(ValuePattern.Pattern, out var vpObj) && vpObj is ValuePattern vp)
                        {
                            if (vp.Current.IsReadOnly) return false;
                        }
                        return true;
                    }

                    if (ct == ControlType.Document)
                    {
                        // For document controls (e.g. Word, VS Code, Google Docs, rich editors)
                        if (focused.TryGetCurrentPattern(ValuePattern.Pattern, out var vpObj) && vpObj is ValuePattern vp)
                        {
                            if (!vp.Current.IsReadOnly) return true;
                        }

                        // If class or name indicates an editable text area
                        string uiaCls = focused.Current.ClassName ?? "";
                        if (uiaCls.Contains("Edit") || uiaCls.Contains("TextBox") || uiaCls.Contains("RichEdit"))
                        {
                            return true;
                        }
                    }

                    // Check if it's a combo box with editable text
                    if (ct == ControlType.ComboBox)
                    {
                        if (focused.TryGetCurrentPattern(ValuePattern.Pattern, out var vpObj) && vpObj is ValuePattern vp)
                        {
                            if (!vp.Current.IsReadOnly) return true;
                        }
                    }
                }
            }
            catch { }

            return false;
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
                
                // Determine active target window
                IntPtr currentFg = GetForegroundWindow();
                if (currentFg == IntPtr.Zero)
                {
                    AppSettingsService.Log("[Focus] No foreground window active. Skipping text injection.");
                    return;
                }

                GetWindowThreadProcessId(currentFg, out uint fgPid);
                if (fgPid == _currentProcessId)
                {
                    AppSettingsService.Log("[Focus] Focus is on LocalVoice app. Skipping text injection.");
                    return;
                }

                // Verify that the user has an active caret or editable text box focused
                if (!IsCaretOrEditableFocused(currentFg))
                {
                    AppSettingsService.Log($"[Focus] No active caret or text box in foreground window ({currentFg}). Skipping text injection.");
                    return;
                }

                AppSettingsService.Log($"[Focus] Active text insertion point confirmed in target: {currentFg}. Injecting text...");
                _lastExternalHwnd = currentFg;

                // Inject via Safe Clipboard (Ctrl+V)
                _injector?.InjectText(text);
            });
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (_winEventHook != IntPtr.Zero)
            {
                UnhookWinEvent(_winEventHook);
                _winEventHook = IntPtr.Zero;
            }

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
