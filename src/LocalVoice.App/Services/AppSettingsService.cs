using System;
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

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetConsoleWindow();

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_HIDE = 0;
        private const int SW_SHOW = 5;

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
                    Console.WriteLine($"[Settings] Windows startup enabled for: {exePath}");
                }
                else
                {
                    key.DeleteValue(APP_NAME, false);
                    Console.WriteLine("[Settings] Windows startup disabled.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Settings Error] Failed to update startup registry: {ex.Message}");
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
            return true; // Default to visible
        }

        public static void SetTerminalSetting(bool show)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(APP_KEY);
                key?.SetValue("ShowTerminal", show ? 1 : 0, RegistryValueKind.DWord);
            }
            catch { }

            SetConsoleVisible(show);
        }

        public static void SetConsoleVisible(bool visible)
        {
            try
            {
                IntPtr hWnd = GetConsoleWindow();
                if (hWnd != IntPtr.Zero)
                {
                    ShowWindow(hWnd, visible ? SW_SHOW : SW_HIDE);
                    Console.WriteLine($"[Settings] Console visibility set to: {visible}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Settings Error] ShowWindow error: {ex.Message}");
            }
        }
    }
}
