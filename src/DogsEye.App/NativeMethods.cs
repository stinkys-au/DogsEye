using System.Runtime.InteropServices;
using DogsEye.Core;

namespace DogsEye.App;

internal static class NativeMethods
{
    [DllImport("user32.dll")] internal static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [StructLayout(LayoutKind.Sequential)] internal struct INPUT { public uint Type; public MOUSEINPUT Mouse; }
    [StructLayout(LayoutKind.Sequential)] internal struct MOUSEINPUT
    {
        public int X, Y;
        public uint MouseData, Flags, Time;
        public nuint ExtraInfo;
    }
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] internal static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] internal static extern nint SetWindowLongPtr(nint window, int index, nint value);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetWindowRect(nint hwnd, out RECT rect);
    [DllImport("user32.dll")] internal static extern int GetSystemMetrics(int index);
    [StructLayout(LayoutKind.Sequential)] internal struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] internal struct MONITORINFO { public int Size; public RECT Monitor, Work; public uint Flags; }
    internal delegate bool MonitorCallback(nint monitor, nint dc, ref RECT rect, nint data);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool EnumDisplayMonitors(nint dc, nint clip, MonitorCallback callback, nint data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetMonitorInfo(nint monitor, ref MONITORINFO info);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool Beep(uint frequency, uint milliseconds);

    internal static List<MONITORINFO> Monitors()
    {
        List<MONITORINFO> result = [];
        EnumDisplayMonitors(0, 0, (nint handle, nint dc, ref RECT rect, nint data) =>
        {
            var info = new MONITORINFO { Size = Marshal.SizeOf<MONITORINFO>() };
            if (GetMonitorInfo(handle, ref info)) result.Add(info);
            return true;
        }, 0);
        return result;
    }
}

internal sealed class WindowsSendInputMouseOutput : IMouseOutput
{
    public bool Move(int deltaX, int deltaY)
    {
        if (deltaX == 0 && deltaY == 0) return true;
        var input = new NativeMethods.INPUT { Type = 0, Mouse = new() { X = deltaX, Y = deltaY, Flags = 0x0001 } };
        return NativeMethods.SendInput(1, [input], Marshal.SizeOf<NativeMethods.INPUT>()) == 1;
    }
}
