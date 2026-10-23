namespace Snapline.Core;

/// <summary>One monotonic leave deadline, independent of focus and card hitboxes.</summary>
internal sealed class AutoHideGate
{
    private double? _awayAt;
    public void Cancel() => _awayAt = null;
    public bool ShouldHide(double now, bool inside, bool interacting, double delay)
    {
        if (inside || interacting) { Cancel(); return false; }
        _awayAt ??= now;
        return now - _awayAt >= (double.IsFinite(delay) ? Math.Clamp(delay, .5, 30) : 2.5);
    }
}
