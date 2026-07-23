namespace Snaploom.Core;

public sealed class ScreenshotActivationGate
{
    private int _isActive;

    public bool IsActive => Volatile.Read(ref _isActive) == 1;

    public bool TryBegin() => Interlocked.CompareExchange(ref _isActive, 1, 0) == 0;

    public void End() => Volatile.Write(ref _isActive, 0);
}
