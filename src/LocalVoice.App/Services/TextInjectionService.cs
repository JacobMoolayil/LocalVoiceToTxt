using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Automation;

namespace LocalVoice.App.Services
{
    public class TextInjectionService
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public InputUnion u;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }

        [StructLayout(LayoutKind.Sequential)]
        private struct HARDWAREINPUT { public uint uMsg; public ushort wParamL; public ushort wParamH; }

        private const int INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_UNICODE = 0x0004;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        public void InjectText(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            
            // For safety and compatibility with all apps (Notepad, VS Code, Slack, Word), 
            // the most robust injection is synthesizing unicode keyboard events directly.
            // This avoids destroying user clipboards or failing on UIA ValuePattern locks.
            SendStringViaSendInput(text + " ");
        }

        private void SendStringViaSendInput(string text)
        {
            INPUT[] inputs = new INPUT[text.Length * 2];
            int index = 0;

            foreach (char c in text)
            {
                // Key down
                inputs[index] = new INPUT
                {
                    type = INPUT_KEYBOARD,
                    u = new InputUnion
                    {
                        ki = new KEYBDINPUT
                        {
                            wVk = 0,
                            wScan = c,
                            dwFlags = KEYEVENTF_UNICODE,
                            time = 0,
                            dwExtraInfo = IntPtr.Zero
                        }
                    }
                };
                index++;

                // Key up
                inputs[index] = new INPUT
                {
                    type = INPUT_KEYBOARD,
                    u = new InputUnion
                    {
                        ki = new KEYBDINPUT
                        {
                            wVk = 0,
                            wScan = c,
                            dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP,
                            time = 0,
                            dwExtraInfo = IntPtr.Zero
                        }
                    }
                };
                index++;
            }

            SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
        }
    }
}
