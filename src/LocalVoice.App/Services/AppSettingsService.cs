using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace LocalVoice.App.Services
{
    public static class AppSettingsService
    {
        private const string RUN_KEY = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string APP_KEY = @"Software\LocalVoice";
        private const string APP_NAME = "LocalVoice";

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AllocConsole();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeConsole();

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetConsoleWindow();

        private static readonly List<string> _logHistory = new();
        private static readonly object _lock = new();

        public static void Log(string message)
        {
            string entry = $"[{DateTime.Now:HH:mm:ss}] {message}";
            lock (_lock)
            {
                if (_logHistory.Count > 1000) _logHistory.RemoveAt(0);
                _logHistory.Add(entry);
            }

            if (GetConsoleWindow() != IntPtr.Zero)
            {
                try
                {
                    Console.WriteLine(entry);
                }
                catch { }
            }
        }

        public static bool IsStartupEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RUN_KEY, false);
                return key?.GetValue(APP_NAME) != null;
            }
            catch
            {
                return false;
            }
        }

        public static void SetStartup(bool enable)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RUN_KEY, true);
                if (key == null) return;

                if (enable)
                {
                    string exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName 
                        ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LocalVoice.App.exe");
                    key.SetValue(APP_NAME, $"\"{exePath}\"");
                    Log($"Windows startup enabled for: {exePath}");
                }
                else
                {
                    key.DeleteValue(APP_NAME, false);
                    Log("Windows startup disabled.");
                }
            }
            catch (Exception ex)
            {
                Log($"Failed to update startup registry: {ex.Message}");
            }
        }

        public static bool GetTerminalSetting()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(APP_KEY, false);
                if (key != null)
                {
                    var val = key.GetValue("ShowTerminal");
                    if (val is int intVal) return intVal == 1;
                }
            }
            catch { }
            return false; // Default: OFF (no terminal on launch)
        }

        public static void SetTerminalSetting(bool show)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(APP_KEY);
                key?.SetValue("ShowTerminal", show ? 1 : 0, RegistryValueKind.DWord);
            }
            catch { }

            ApplyConsoleState(show);
        }

        public static string GetWhisperModelSetting()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(APP_KEY, false);
                if (key != null)
                {
                    var val = key.GetValue("WhisperModel");
                    if (val is string strVal && !string.IsNullOrWhiteSpace(strVal))
                        return strVal;
                }
            }
            catch { }
            return "auto"; // Default: auto
        }

        public static void SetWhisperModelSetting(string model)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(APP_KEY);
                key?.SetValue("WhisperModel", model, RegistryValueKind.String);
                Log($"[Settings] Whisper model setting saved: {model}");
            }
            catch { }
        }

        public static void ApplyConsoleState(bool show)
        {
            IntPtr hWnd = GetConsoleWindow();

            if (show)
            {
                if (hWnd == IntPtr.Zero)
                {
                    AllocConsole();
                    try
                    {
                        Console.Title = "LocalVoice - Live Logs";
                        var stdOut = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
                        Console.SetOut(stdOut);
                        var stdErr = new StreamWriter(Console.OpenStandardError()) { AutoFlush = true };
                        Console.SetError(stdErr);
                    }
                    catch { }

                    // Replay all past logs
                    lock (_lock)
                    {
                        Console.WriteLine("=======================================================");
                        Console.WriteLine(" LocalVoice Logs (Terminal enabled by user)");
                        Console.WriteLine("=======================================================");
                        foreach (var line in _logHistory)
                        {
                            Console.WriteLine(line);
                        }
                    }
                }
            }
            else
            {
                if (hWnd != IntPtr.Zero)
                {
                    FreeConsole();
                }
            }
        }
    }
}
