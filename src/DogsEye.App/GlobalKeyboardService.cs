using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace DogsEye.App;

// Standard WM_INPUT delivery to DogsEye's own window. No keyboard hook,
// input suppression, generated input, or interaction with another process.
internal sealed class GlobalKeyboardService : IDisposable
{
    [StructLayout(LayoutKind.Sequential)] private struct Device
    {
        public ushort UsagePage, Usage;
        public uint Flags;
        public nint Target;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Header
    {
        public uint Type, Size;
        public nint Device, WParam;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Keyboard
    {
        public ushort MakeCode, Flags, Reserved, VirtualKey;
        public uint Message, ExtraInformation;
    }
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterRawInputDevices(Device[] devices, uint count, uint size);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputData(nint input, uint command, nint data, ref uint size, uint headerSize);
    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint code, uint type);

    private readonly HwndSource source;
    private readonly nint buffer = Marshal.AllocHGlobal(256);
    private int binding;
    private bool held, awaitingRelease, disposed;
    public event Action<bool>? KeyChanged;
    public event Action<string>? Error;
    public bool Registered { get; private set; }
    public GlobalKeyboardService(nint window, int binding)
    {
        this.binding = binding;
        source = HwndSource.FromHwnd(window) ?? throw new InvalidOperationException("DogsEye window is not ready for keyboard input.");
        source.AddHook(WindowMessage);
        Device keyboard = new() { UsagePage = 1, Usage = 6, Flags = 0x100, Target = window }; // RIDEV_INPUTSINK
        if (!RegisterRawInputDevices([keyboard], 1, (uint)Marshal.SizeOf<Device>()))
        {
            int error = Marshal.GetLastWin32Error();
            source.RemoveHook(WindowMessage);
            Marshal.FreeHGlobal(buffer);
            throw new Win32Exception(error, "Windows could not register the global activation key.");
        }
        Registered = true;
    }
    public void Rebind(int key)
    {
        binding = key;
        held = false;
        awaitingRelease = true; // The captured press must never become an activation.
        KeyChanged?.Invoke(false);
    }
    private nint WindowMessage(nint hwnd, int message, nint wparam, nint lparam, ref bool handled)
    {
        if (message != 0x00FF || disposed) return 0; // WM_INPUT
        uint size = 256;
        uint bytes = GetRawInputData(lparam, 0x10000003, buffer, ref size, (uint)Marshal.SizeOf<Header>());
        if (bytes == uint.MaxValue)
        {
            Error?.Invoke($"Windows keyboard input error {Marshal.GetLastWin32Error()}.");
            return 0;
        }
        int headerSize = Marshal.SizeOf<Header>();
        if (bytes < headerSize + Marshal.SizeOf<Keyboard>() || Marshal.PtrToStructure<Header>(buffer).Type != 1) return 0;
        var keyboard = Marshal.PtrToStructure<Keyboard>(buffer + headerSize);
        int key = keyboard.VirtualKey;
        if (key == 255 || keyboard.MakeCode == 255) return 0;
        if (key == 0x10) key = (int)MapVirtualKey(keyboard.MakeCode, 3); // distinguish L/R Shift
        if (key == 0x11) key = (keyboard.Flags & 2) != 0 ? 0xA3 : 0xA2;
        if (key == 0x12) key = (keyboard.Flags & 2) != 0 ? 0xA5 : 0xA4;
        if (key != binding) return 0; // Unbound keys are neither retained nor logged.
        bool down = (keyboard.Flags & 1) == 0;
        if (awaitingRelease) { if (!down) awaitingRelease = false; return 0; }
        if (held != down) { held = down; KeyChanged?.Invoke(down); }
        // Leave the message unhandled so normal Windows cleanup / keyboard delivery continues.
        return 0;
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        source.RemoveHook(WindowMessage);
        if (Registered) RegisterRawInputDevices([new Device { UsagePage = 1, Usage = 6, Flags = 1 }], 1, (uint)Marshal.SizeOf<Device>());
        Registered = false;
        Marshal.FreeHGlobal(buffer);
    }
}
