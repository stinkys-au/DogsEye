namespace DogsEye.Core;

public enum InputCommand { Toggle, Recenter, Disable }

// Keyboard, UI, and a future HOTAS adapter all issue the same commands.
public sealed class InputCommandService(int holdDurationMs)
{
    private bool held, triggered, waitForRelease;
    private double downAt;
    public event Action<InputCommand>? Command;
    public bool IsHeld => held;
    public bool LongPressTriggered => triggered;
    public double HeldMilliseconds(double now) => held ? Math.Clamp((now - downAt) * 1000, 0, holdDurationMs) : 0;
    public void Reset(bool requireRelease = true)
    {
        held = triggered = false;
        waitForRelease = requireRelease;
    }
    public void Send(InputCommand command) => Command?.Invoke(command);
    public void Update(bool down, double now)
    {
        if (waitForRelease) { if (!down) waitForRelease = false; return; }
        if (down && !held) { held = true; downAt = now; triggered = false; }
        // Also evaluate on release so a late polling tick never misclassifies a long press.
        if (held && !triggered && (now - downAt) * 1000 >= holdDurationMs)
        {
            triggered = true;
            Send(InputCommand.Toggle);
        }
        if (!down && held)
        {
            held = false;
            if (!triggered) Send(InputCommand.Recenter);
        }
    }
}
