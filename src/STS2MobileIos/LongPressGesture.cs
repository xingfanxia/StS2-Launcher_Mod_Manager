namespace STS2MobileIos;

public sealed class LongPressGesture
{
    private ulong _start;
    private float _x, _y;
    private bool _active;
    public const ulong HoldMicroseconds = 500_000;
    public const float MovementTolerance = 18;

    public void Begin(ulong now, float x, float y)
    {
        _start = now;
        _x = x;
        _y = y;
        _active = true;
    }

    public void Cancel() => _active = false;

    public bool Update(ulong now, float x, float y, bool pressed)
    {
        float dx = x - _x, dy = y - _y;
        if (!pressed || dx * dx + dy * dy > MovementTolerance * MovementTolerance || now < _start)
            Cancel();
        if (!_active || now - _start < HoldMicroseconds)
            return false;
        Cancel();
        return true;
    }
}
