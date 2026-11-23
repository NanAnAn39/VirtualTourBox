namespace VirtualTourBox.Services;

internal sealed class StickGuard
{
    public bool Blocked { get; private set; }
    bool active;
    long? candidateSince;

    public bool Update(double magnitude, double threshold, bool pressed)
    {
        double release = Math.Max(0, threshold - 0.04);
        if (pressed)
        {
            Blocked = true;
            active = false;
            candidateSince = null;
            return false;
        }
        if (Blocked)
        {
            if (magnitude <= release) Blocked = false;
            return false;
        }
        if (active)
        {
            if (magnitude <= release) Reset();
            return active;
        }
        if (magnitude < threshold)
        {
            candidateSince = null;
            return false;
        }
        candidateSince ??= Environment.TickCount64;
        active = Environment.TickCount64 - candidateSince.Value >= 150;
        return active;
    }

    public void Reset()
    {
        Blocked = false;
        active = false;
        candidateSince = null;
    }
}
