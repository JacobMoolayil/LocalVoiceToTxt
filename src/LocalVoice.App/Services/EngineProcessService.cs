using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LocalVoice.App.Services
{
    public class AudioDeviceInfo : INotifyPropertyChanged
    {
        private int _id;
        private string _name = "";
        private bool _isDefault;

        public int Id
        {
            get => _id;
            set
            {
                if (_id != value)
                {
                    _id = value;
                    OnPropertyChanged();
                }
            }
        }

        public string Name
        {
            get => _name;
            set
            {
                if (_name != value)
                {
                    _name = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsDefault
        {
            get => _isDefault;
            set
            {
                if (_isDefault != value)
                {
                    _isDefault = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
        }
    }

    public class EngineProcessService : IDisposable
    {
        private Process? _process;
        private StreamWriter? _stdin;
        private readonly object _stdinLock = new();

        public event Action<string>? OnInterimText;
        public event Action<string>? OnCommittedText;
        public event Action? OnVadStart;
        public event Action? OnVadEnd;
        public event Action<List<AudioDeviceInfo>, int>? OnDeviceListReceived;
        public event Action<string, string>? OnModelInfoReceived;
        public event Action? OnEngineReady;
        public event Action<string>? OnEngineLoading;

        public void StartEngine()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string[] engineCandidates = new[]
            {
                Path.Combine(baseDir, "engine", "engine_host.py"),
                Path.Combine(baseDir, "..", "engine", "engine_host.py"),
                Path.Combine(baseDir, "..", "..", "engine", "engine_host.py"),
                Path.Combine(baseDir, "..", "..", "..", "..", "..", "engine", "engine_host.py")
            };

            string[] pythonCandidates = new[]
            {
                Path.Combine(baseDir, "engine", "venv", "Scripts", "python.exe"),
                Path.Combine(baseDir, "..", "engine", "venv", "Scripts", "python.exe"),
                Path.Combine(baseDir, "..", "..", "engine", "venv", "Scripts", "python.exe"),
                Path.Combine(baseDir, "..", "..", "..", "..", "..", "engine", "venv", "Scripts", "python.exe")
            };

            string enginePath = "";
            foreach (var c in engineCandidates)
            {
                var full = Path.GetFullPath(c);
                if (File.Exists(full)) { enginePath = full; break; }
            }

            string pythonPath = "";
            foreach (var c in pythonCandidates)
            {
                var full = Path.GetFullPath(c);
                if (File.Exists(full)) { pythonPath = full; break; }
            }

            if (string.IsNullOrEmpty(pythonPath)) pythonPath = "python.exe";
            if (string.IsNullOrEmpty(enginePath)) enginePath = "engine_host.py";

            AppSettingsService.Log($"[IPC] Launching Engine: {pythonPath}");
            AppSettingsService.Log($"[IPC] Script Path: {enginePath}");

            _process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = pythonPath,
                    Arguments = $"\"{enginePath}\"",
                    WorkingDirectory = Path.GetDirectoryName(enginePath) ?? baseDir,
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            _process.OutputDataReceived += HandleEngineOutput;
            _process.ErrorDataReceived += (s, e) => { if (e.Data != null) AppSettingsService.Log(e.Data); };

            _process.Start();
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            _stdin = _process.StandardInput;
        }

        private void HandleEngineOutput(object sender, DataReceivedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(e.Data)) return;

            try
            {
                AppSettingsService.Log($"[IPC Message] {e.Data}");
                using var doc = JsonDocument.Parse(e.Data);
                if (doc.RootElement.TryGetProperty("event", out var ev))
                {
                    string? eventType = ev.GetString();
                    if (eventType == "vad_start") OnVadStart?.Invoke();
                    else if (eventType == "vad_end") OnVadEnd?.Invoke();
                    else if (eventType == "interim")
                    {
                        var text = doc.RootElement.GetProperty("text").GetString();
                        if (!string.IsNullOrEmpty(text)) OnInterimText?.Invoke(text);
                    }
                    else if (eventType == "commit")
                    {
                        var text = doc.RootElement.GetProperty("text").GetString();
                        if (!string.IsNullOrEmpty(text)) OnCommittedText?.Invoke(text);
                    }
                    else if (eventType == "devices")
                    {
                        var list = new List<AudioDeviceInfo>();
                        int selectedId = -1;
                        if (doc.RootElement.TryGetProperty("selected_id", out var selElem))
                        {
                            selectedId = selElem.GetInt32();
                        }
                        if (doc.RootElement.TryGetProperty("devices", out var devsElem) && devsElem.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var item in devsElem.EnumerateArray())
                            {
                                int id = item.GetProperty("id").GetInt32();
                                string name = item.GetProperty("name").GetString() ?? "";
                                bool isDef = item.GetProperty("is_default").GetBoolean();
                                list.Add(new AudioDeviceInfo { Id = id, Name = name, IsDefault = isDef });
                            }
                        }
                        OnDeviceListReceived?.Invoke(list, selectedId);
                    }
                    else if (eventType == "loading")
                    {
                        string msg = "Loading AI Engine...";
                        if (doc.RootElement.TryGetProperty("message", out var msgElem))
                        {
                            msg = msgElem.GetString() ?? msg;
                        }
                        AppSettingsService.Log($"[Engine Loading] {msg}");
                        OnEngineLoading?.Invoke(msg);
                    }
                    else if (eventType == "ready" || eventType == "model_info")
                    {
                        if (eventType == "ready")
                        {
                            OnEngineReady?.Invoke();
                        }

                        string model = "small";
                        string hardwareLabel = "GPU: RTX 3050 (CUDA FP16)";
                        if (doc.RootElement.TryGetProperty("model", out var mElem))
                        {
                            model = mElem.GetString() ?? "small";
                        }
                        if (doc.RootElement.TryGetProperty("hardware_label", out var hElem))
                        {
                            hardwareLabel = hElem.GetString() ?? "GPU: RTX 3050 (CUDA FP16)";
                        }
                        else if (doc.RootElement.TryGetProperty("label", out var lElem))
                        {
                            hardwareLabel = lElem.GetString() ?? "GPU: RTX 3050 (CUDA FP16)";
                        }
                        AppSettingsService.Log($"[Engine] Model & Hardware info received: Model={model}, Hardware={hardwareLabel}");
                        OnModelInfoReceived?.Invoke(model, hardwareLabel);
                    }
                }
            }
            catch (Exception ex)
            {
                AppSettingsService.Log($"[IPC Parse Error] {ex.Message} | Line: {e.Data}");
            }
        }

        public void SetDevice(int deviceId)
        {
            SendCommandWithPayload("set_device", new { device_id = deviceId });
        }

        public void RequestDeviceList()
        {
            SendCommand("list_devices");
        }

        public void RequestModelInfo()
        {
            SendCommand("get_model_info");
        }

        public void SendCommand(string command)
        {
            lock (_stdinLock)
            {
                if (_stdin != null && _process != null && !_process.HasExited)
                {
                    try
                    {
                        var cmdJson = JsonSerializer.Serialize(new { cmd = command });
                        _stdin.WriteLine(cmdJson);
                        _stdin.Flush();
                    }
                    catch (Exception ex)
                    {
                        AppSettingsService.Log($"[IPC Send Error] {ex.Message}");
                    }
                }
            }
        }

        public void SendCommandWithPayload(string command, object payload)
        {
            lock (_stdinLock)
            {
                if (_stdin != null && _process != null && !_process.HasExited)
                {
                    try
                    {
                        var dict = new Dictionary<string, object>();
                        dict["cmd"] = command;
                        foreach (var prop in payload.GetType().GetProperties())
                        {
                            dict[prop.Name] = prop.GetValue(payload) ?? "";
                        }
                        var cmdJson = JsonSerializer.Serialize(dict);
                        _stdin.WriteLine(cmdJson);
                        _stdin.Flush();
                    }
                    catch (Exception ex)
                    {
                        AppSettingsService.Log($"[IPC Send Error] {ex.Message}");
                    }
                }
            }
        }

        public void Dispose()
        {
            if (_process != null && !_process.HasExited)
            {
                try
                {
                    SendCommand("exit");
                    _process.WaitForExit(1000);
                    if (!_process.HasExited) _process.Kill();
                    _process.Dispose();
                }
                catch { }
            }
        }
    }
}
