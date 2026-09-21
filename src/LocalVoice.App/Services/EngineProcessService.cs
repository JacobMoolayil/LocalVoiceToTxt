using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace LocalVoice.App.Services
{
    public class EngineProcessService : IDisposable
    {
        private Process? _process;
        private StreamWriter? _stdin;

        public event Action<string>? OnInterimText;
        public event Action<string>? OnCommittedText;
        public event Action? OnVadStart;
        public event Action? OnVadEnd;

        public void StartEngine()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var enginePath = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "engine", "engine_host.py"));
            var pythonPath = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "engine", "venv", "Scripts", "python.exe"));

            // Check if paths exist
            if (!File.Exists(pythonPath))
            {
                // Fallback for published/relative layouts
                var altPy = Path.GetFullPath(Path.Combine(baseDir, "engine", "venv", "Scripts", "python.exe"));
                if (File.Exists(altPy)) pythonPath = altPy;
            }
            if (!File.Exists(enginePath))
            {
                var altEng = Path.GetFullPath(Path.Combine(baseDir, "engine", "engine_host.py"));
                if (File.Exists(altEng)) enginePath = altEng;
            }

            Console.WriteLine($"[IPC] Launching Engine: {pythonPath}");
            Console.WriteLine($"[IPC] Script Path: {enginePath}");

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
            _process.ErrorDataReceived += (s, e) => { if (e.Data != null) Console.WriteLine(e.Data); };

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
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[IPC Parse Error] {ex.Message} | Line: {e.Data}");
            }
        }

        public void SendCommand(string command)
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
                    Console.WriteLine($"[IPC Send Error] {ex.Message}");
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
