using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace LocalVoice.App.Services
{
    public enum EDataFlow
    {
        eRender = 0,
        eCapture = 1,
        eAll = 2
    }

    public enum ERole
    {
        eConsole = 0,
        eMultimedia = 1,
        eCommunications = 2
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PropertyKey
    {
        public Guid fmtid;
        public uint pid;
    }

    [Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMDevice
    {
        int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, out IntPtr ppInterface);
        int OpenPropertyStore(int stgmAccess, out IntPtr ppProperties);
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);
        int GetState(out int pdwState);
    }

    [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMDeviceCollection
    {
        int GetCount(out int pcDevices);
        int Item(int nDevice, out IMMDevice ppDevice);
    }

    [Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMNotificationClient
    {
        [PreserveSig]
        int OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, uint newState);
        
        [PreserveSig]
        int OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId);
        
        [PreserveSig]
        int OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId);
        
        [PreserveSig]
        int OnDefaultDeviceChanged(EDataFlow flow, ERole role, [MarshalAs(UnmanagedType.LPWStr)] string pwstrDefaultDeviceId);
        
        [PreserveSig]
        int OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId, PropertyKey key);
    }

    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(EDataFlow dataFlow, int stateMask, out IMMDeviceCollection ppDevices);
        int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice ppEndpoint);
        int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string pwstrId, out IMMDevice ppDevice);
        int RegisterEndpointNotificationCallback(IMMNotificationClient pClient);
        int UnregisterEndpointNotificationCallback(IMMNotificationClient pClient);
    }

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    public class MMDeviceEnumeratorComObject
    {
    }

    public class AudioDeviceWatcher : IMMNotificationClient, IDisposable
    {
        public event Action? AudioDevicesChanged;

        private IMMDeviceEnumerator? _enumerator;
        private readonly System.Threading.Timer _debounceTimer;
        private readonly System.Threading.Timer _pollTimer;
        private bool _disposed = false;

        private string _lastDefaultId = "";
        private int _lastActiveCount = -1;

        public AudioDeviceWatcher()
        {
            _debounceTimer = new System.Threading.Timer(OnDebounceTimerElapsed, null, Timeout.Infinite, Timeout.Infinite);

            try
            {
                _enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
                
                // Read initial default capture device and count
                CheckCurrentAudioState(out _lastDefaultId, out _lastActiveCount);

                int hr = _enumerator.RegisterEndpointNotificationCallback(this);
                if (hr == 0)
                {
                    AppSettingsService.Log("[AudioWatcher] Registered Windows CoreAudio endpoint notifications.");
                }
                else
                {
                    AppSettingsService.Log($"[AudioWatcher] RegisterEndpointNotificationCallback returned HRESULT {hr}");
                }
            }
            catch (Exception ex)
            {
                AppSettingsService.Log($"[AudioWatcher] Exception initializing IMMDeviceEnumerator: {ex.Message}");
            }

            // Polling safety net: Checks every 750ms if default capture device or active device count changed.
            // This guarantees detection even if Windows fails to dispatch a COM notification.
            _pollTimer = new System.Threading.Timer(PollAudioState, null, 500, 750);
        }

        private void CheckCurrentAudioState(out string defaultId, out int activeCount)
        {
            defaultId = "";
            activeCount = -1;

            if (_enumerator == null) return;

            try
            {
                int hr = _enumerator.GetDefaultAudioEndpoint(EDataFlow.eCapture, ERole.eConsole, out IMMDevice dev);
                if (hr == 0 && dev != null)
                {
                    dev.GetId(out defaultId);
                    Marshal.ReleaseComObject(dev);
                }
            }
            catch { }

            try
            {
                const int DEVICE_STATE_ACTIVE = 1;
                int hr = _enumerator.EnumAudioEndpoints(EDataFlow.eCapture, DEVICE_STATE_ACTIVE, out IMMDeviceCollection col);
                if (hr == 0 && col != null)
                {
                    col.GetCount(out activeCount);
                    Marshal.ReleaseComObject(col);
                }
            }
            catch { }
        }

        private void PollAudioState(object? state)
        {
            if (_disposed || _enumerator == null) return;

            try
            {
                CheckCurrentAudioState(out string curDefaultId, out int curActiveCount);

                bool defaultChanged = !string.IsNullOrEmpty(curDefaultId) && curDefaultId != _lastDefaultId;
                bool countChanged = curActiveCount != -1 && _lastActiveCount != -1 && curActiveCount != _lastActiveCount;

                if (defaultChanged || countChanged)
                {
                    AppSettingsService.Log($"[AudioWatcher Poll] Change detected! Default: '{_lastDefaultId}' -> '{curDefaultId}', Count: {_lastActiveCount} -> {curActiveCount}");
                    _lastDefaultId = curDefaultId;
                    _lastActiveCount = curActiveCount;
                    NotifyDeviceChanged();
                }
            }
            catch { }
        }

        public void NotifyDeviceChanged()
        {
            if (_disposed) return;
            try
            {
                _debounceTimer.Change(100, Timeout.Infinite);
            }
            catch { }
        }

        private void OnDebounceTimerElapsed(object? state)
        {
            if (_disposed) return;
            AppSettingsService.Log("[AudioWatcher] Debounce timer expired. Invoking AudioDevicesChanged.");
            try
            {
                AudioDevicesChanged?.Invoke();
            }
            catch (Exception ex)
            {
                AppSettingsService.Log($"[AudioWatcher] Error in AudioDevicesChanged handler: {ex.Message}");
            }
        }

        public int OnDefaultDeviceChanged(EDataFlow flow, ERole role, string pwstrDefaultDeviceId)
        {
            if (flow == EDataFlow.eCapture || flow == EDataFlow.eAll)
            {
                AppSettingsService.Log($"[AudioWatcher Event] OnDefaultDeviceChanged: flow={flow}, role={role}, id={pwstrDefaultDeviceId}");
                _lastDefaultId = pwstrDefaultDeviceId ?? "";
                NotifyDeviceChanged();
            }
            return 0; // S_OK
        }

        public int OnDeviceAdded(string pwstrDeviceId)
        {
            AppSettingsService.Log($"[AudioWatcher Event] OnDeviceAdded: {pwstrDeviceId}");
            NotifyDeviceChanged();
            return 0; // S_OK
        }

        public int OnDeviceRemoved(string pwstrDeviceId)
        {
            AppSettingsService.Log($"[AudioWatcher Event] OnDeviceRemoved: {pwstrDeviceId}");
            NotifyDeviceChanged();
            return 0; // S_OK
        }

        public int OnDeviceStateChanged(string deviceId, uint newState)
        {
            AppSettingsService.Log($"[AudioWatcher Event] OnDeviceStateChanged: id={deviceId}, state={newState}");
            NotifyDeviceChanged();
            return 0; // S_OK
        }

        public int OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key)
        {
            return 0; // S_OK
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try { _debounceTimer.Dispose(); } catch { }
            try { _pollTimer.Dispose(); } catch { }

            if (_enumerator != null)
            {
                try
                {
                    _enumerator.UnregisterEndpointNotificationCallback(this);
                }
                catch { }

                try
                {
                    Marshal.ReleaseComObject(_enumerator);
                }
                catch { }

                _enumerator = null;
            }
        }
    }
}
