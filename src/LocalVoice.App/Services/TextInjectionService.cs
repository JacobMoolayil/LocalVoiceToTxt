using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;

namespace LocalVoice.App.Services
{
    public class TextInjectionService
    {
        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        private const byte VK_CONTROL = 0x11;
        private const byte VK_V = 0x56;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        public void InjectText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            string textToInject = text + " ";

            try
            {
                // 1. Backup user's clipboard
                string originalText = "";
                bool hadText = false;
                try
                {
                    if (System.Windows.Clipboard.ContainsText())
                    {
                        originalText = System.Windows.Clipboard.GetText();
                        hadText = true;
                    }
                }
                catch { }

                // 2. Set transcription text
                try
                {
                    System.Windows.Clipboard.SetText(textToInject);
                }
                catch
                {
                    System.Windows.Forms.Clipboard.SetText(textToInject);
                }

                // 3. Synthesize Ctrl + V
                Thread.Sleep(40);
                keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
                keybd_event(VK_V, 0, 0, UIntPtr.Zero);
                Thread.Sleep(25);
                keybd_event(VK_V, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);

                Console.WriteLine($"[TextInjection] Injected successfully via Safe Clipboard (Ctrl+V): '{textToInject.Trim()}'");

                // 4. Restore original clipboard content
                if (hadText)
                {
                    Task.Run(async () =>
                    {
                        await Task.Delay(250);
                        try
                        {
                            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                            {
                                System.Windows.Clipboard.SetText(originalText);
                            });
                        }
                        catch { }
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TextInjection Error] {ex.Message}");
            }
        }
    }
}
