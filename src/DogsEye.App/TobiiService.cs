using System.Runtime.InteropServices;
using DogsEye.Core;

namespace DogsEye.App;

internal sealed class TobiiService : IDisposable
{
    private bool initialized;
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    internal struct Snapshot
    {
        public long Timestamp;
        public float Yaw, Pitch, Roll;
        public int Connected, Enabled, Present, HeadSupported, HasPose;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Model;
        public readonly HeadPose? Pose => HasPose != 0 ? new(Timestamp, Yaw, Pitch, Roll) : null;
        // Presence can depend on eye gaze; camera control depends only on fresh head data.
        public readonly bool Available => Connected != 0 && Enabled != 0 && HeadSupported != 0;
    }
    [DllImport("DogsEye.Tobii.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int dogseye_initialize(int left, int top, int right, int bottom);
    [DllImport("DogsEye.Tobii.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int dogseye_poll(out Snapshot snapshot);
    [DllImport("DogsEye.Tobii.dll", CallingConvention = CallingConvention.Cdecl)] private static extern void dogseye_shutdown();
    public void Initialize()
    {
        // A fixed desktop rectangle keeps head tracking alive while DogsEye is minimised.
        // It is unrelated to any game window or process.
        if (dogseye_initialize(0, 0, NativeMethods.GetSystemMetrics(0), NativeMethods.GetSystemMetrics(1)) == 0)
            throw new InvalidOperationException("Tobii SDK could not initialise.");
        initialized = true;
    }
    public Snapshot Poll() => dogseye_poll(out var snapshot) == 1 ? snapshot : throw new InvalidOperationException("Tobii SDK polling failed.");
    public void Dispose()
    {
        if (!initialized) return;
        initialized = false;
        dogseye_shutdown();
    }
}
