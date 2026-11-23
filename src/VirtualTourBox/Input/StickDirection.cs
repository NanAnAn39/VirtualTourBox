namespace VirtualTourBox.Input;

public sealed class StickDirection
{
    string? current;
    public string? Update(double x, double y, bool active)
    {
        if (!active) { current = null; return null; }
        // Keep the selected axis near a diagonal, avoiding alternating shortcuts.
        bool horizontal = current is "Left" or "Right";
        if (current is null || Math.Abs(Math.Abs(x)-Math.Abs(y)) > 0.12)
            horizontal = Math.Abs(x) > Math.Abs(y);
        current = horizontal ? (x >= 0 ? "Right" : "Left") : (y >= 0 ? "Down" : "Up");
        return current;
    }
}

