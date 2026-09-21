using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace LocalVoice.App.Services
{
    public class EngineProcessService : IDisposable
    {
        private Process _process;
        private StreamWriter _stdin;

        public event Action<string> OnInterimText;
        public event Action<string> OnCommittedText;
        public event Action OnVadStart;
        public event Action OnVadEnd;

        public void StartEngine()
        {
            var enginePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "engine", "engine_host.py");
            var pythonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "engine", "venv", "Scripts", "python.exe");

            _process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = pythonPath,
                    Arguments = $"\"{enginePath}\"",
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            _process.OutputDataReceived += HandleEngineOutput;
            _process.ErrorDataReceived += (s, e) => { if (e.Data != null) Console.WriteLine("ENGINE ERROR: " + e.Data); };

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
                    string eventType = ev.GetString();
                    if (eventType == "vad_start") OnVadStart?.Invoke();
                    else if (eventType == "vad_end") OnVadEnd?.Invoke();
                    else if (eventType == "interim")
                    {
                        var text = doc.RootElement.GetProperty("text").GetString();
                        OnInterimText?.Invoke(text);
                    }
                    else if (eventType == "commit")
                    {
                        var text = doc.RootElement.GetProperty("text").GetString();
                        OnCommittedText?.Invoke(text);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Parse Error: " + ex.Message + " | Raw: " + e.Data);
            }
        }

        public void SendCommand(string command)
        {
            if (_stdin != null)
            {
                var cmdJson = JsonSerializer.Serialize(new { cmd = command });
                _stdin.WriteLine(cmdJson);
                _stdin.Flush();
            }
        }

        public void Dispose()
        {
            if (_process != null && !_process.HasExited)
            {
                SendCommand("exit");
                _process.WaitForExit(1000);
                if (!_process.HasExited) _process.Kill();
                _process.Dispose();
            }
        }
    }
}
