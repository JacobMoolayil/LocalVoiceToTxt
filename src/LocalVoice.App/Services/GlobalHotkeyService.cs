using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace LocalVoice.App.Services
{
    public class GlobalHotkeyService : IDisposable
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        public const int HOTKEY_ID = 9000;
        
        public const uint MOD_NONE = 0x0000;
        public const uint MOD_ALT = 0x0001;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_SHIFT = 0x0004;
        public const uint MOD_WIN = 0x0008;
        
        public const uint VK_SPACE = 0x20;

        private IntPtr _hWnd;
        private HwndSource? _source;
        public event Action? OnHotKeyPressed;

        public bool Register(IntPtr windowHandle)
        {
            _hWnd = windowHandle;
            _source = HwndSource.FromHwnd(_hWnd);
            if (_source != null)
            {
                _source.AddHook(HwndHook);
            }
            else
            {
                Console.WriteLine("ERROR: HwndSource.FromHwnd returned null!");
            }

            // Register Ctrl + Shift + Space
            bool success = RegisterHotKey(_hWnd, HOTKEY_ID, MOD_CONTROL | MOD_SHIFT, VK_SPACE);
            if (!success)
            {
                int err = Marshal.GetLastWin32Error();
                Console.WriteLine($"RegisterHotKey failed with error code: {err}");
            }
            else
            {
                Console.WriteLine("Global hotkey Ctrl+Shift+Space registered successfully!");
            }

            return success;
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_HOTKEY = 0x0312;
            if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
            {
                Console.WriteLine("HOTKEY PRESSED: Ctrl+Shift+Space triggered!");
                OnHotKeyPressed?.Invoke();
                handled = true;
            }
            return IntPtr.Zero;
        }

        public void Dispose()
        {
            if (_hWnd != IntPtr.Zero)
            {
                UnregisterHotKey(_hWnd, HOTKEY_ID);
                _source?.RemoveHook(HwndHook);
                _hWnd = IntPtr.Zero;
            }
        }
    }
}
