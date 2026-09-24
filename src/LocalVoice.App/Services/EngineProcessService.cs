using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

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

    internal class JobObject : IDisposable
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetInformationJobObject(IntPtr hJob, int JobObjectInfoClass, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        private const int JobObjectExtendedLimitInformation = 9;
        private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IO_COUNTERS
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryLimit;
            public UIntPtr PeakJobMemoryLimit;
        }

        private IntPtr _handle;

        public JobObject()
        {
            try
            {
                _handle = CreateJobObject(IntPtr.Zero, null);
                if (_handle != IntPtr.Zero)
                {
                    var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
                    info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;

                    int length = Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
                    IntPtr pInfo = Marshal.AllocHGlobal(length);
                    try
                    {
                        Marshal.StructureToPtr(info, pInfo, false);
                        SetInformationJobObject(_handle, JobObjectExtendedLimitInformation, pInfo, (uint)length);
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(pInfo);
                    }
                }
            }
            catch (Exception ex)
            {
                AppSettingsService.Log($"[JobObject] Error initializing job object: {ex.Message}");
            }
        }

        public void AddProcess(IntPtr processHandle)
        {
            try
            {
                if (_handle != IntPtr.Zero && processHandle != IntPtr.Zero)
                {
                    AssignProcessToJobObject(_handle, processHandle);
                }
            }
            catch { }
        }

        public void Dispose()
        {
            if (_handle != IntPtr.Zero)
            {
                CloseHandle(_handle);
                _handle = IntPtr.Zero;
            }
        }
    }

    public class EngineProcessService : IDisposable
    {
        private Process? _process;
        private StreamWriter? _stdin;
        private readonly object _stdinLock = new();
        private JobObject? _jobObject;

        public event Action<string>? OnInterimText;
        public event Action<string>? OnCommittedText;
        public event Action? OnVadStart;
        public event Action? OnVadEnd;
        public event Action<List<AudioDeviceInfo>, int>? OnDeviceListReceived;
        public event Action<string, string>? OnModelInfoReceived;
        public event Action<bool, double, double, string>? OnHardwareInfoReceived;
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

            string savedModel = AppSettingsService.GetWhisperModelSetting();
            AppSettingsService.Log($"[IPC] Launching Engine: {pythonPath} (Model: {savedModel})");
            AppSettingsService.Log($"[IPC] Script Path: {enginePath}");

            _process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = pythonPath,
                    Arguments = $"\"{enginePath}\" --model \"{savedModel}\"",
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

            _jobObject = new JobObject();
            _process.Start();
            _jobObject.AddProcess(_process.Handle);
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
                        bool isGpu = false;
                        double vramGb = 0;
                        double ramGb = 0;
                        string hardwareName = "";

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
                        if (doc.RootElement.TryGetProperty("is_gpu", out var gElem))
                        {
                            isGpu = gElem.GetBoolean();
                        }
                        if (doc.RootElement.TryGetProperty("vram_gb", out var vElem))
                        {
                            vramGb = vElem.GetDouble();
                        }
                        if (doc.RootElement.TryGetProperty("ram_gb", out var rElem))
                        {
                            ramGb = rElem.GetDouble();
                        }
                        if (doc.RootElement.TryGetProperty("hardware_name", out var hnElem))
                        {
                            hardwareName = hnElem.GetString() ?? "";
                        }

                        AppSettingsService.Log($"[Engine] Model & Hardware info received: Model={model}, Hardware={hardwareLabel}, isGpu={isGpu}, VRAM={vramGb}GB, RAM={ramGb}GB");
                        OnModelInfoReceived?.Invoke(model, hardwareLabel);
                        OnHardwareInfoReceived?.Invoke(isGpu, vramGb, ramGb, hardwareName);
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

        public void SetModel(string model)
        {
            SendCommandWithPayload("set_model", new { model = model });
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
            lock (_stdinLock)
            {
                if (_process != null && !_process.HasExited)
                {
                    try
                    {
                        AppSettingsService.Log("[Engine] Terminating engine process & offloading GPU...");
                        SendCommand("exit");
                        try { _stdin?.Close(); } catch { }

                        if (!_process.WaitForExit(1500))
                        {
                            AppSettingsService.Log("[Engine] Forcing entire process tree termination to release GPU VRAM...");
                            _process.Kill(entireProcessTree: true);
                        }
                    }
                    catch (Exception ex)
                    {
                        AppSettingsService.Log($"[Engine] Error during process disposal: {ex.Message}");
                        try { _process.Kill(entireProcessTree: true); } catch { }
                    }
                    finally
                    {
                        try { _process.Dispose(); } catch { }
                        _jobObject?.Dispose();
                        _jobObject = null;
                        _process = null;
                        _stdin = null;
                    }
                }
            }
        }
    }
}
