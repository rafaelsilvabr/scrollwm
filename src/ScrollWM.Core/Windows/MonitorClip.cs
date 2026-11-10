namespace ScrollWM.Core.Windows;

public static class MonitorClip
{
    // The permitted clip is rectangular. Any app-owned shape whose bounding
    // box fits inside it is safe, including rounded frames and smaller masks.
    // An empty region is valid only when the column is completely hidden.
    public static bool Accepts(PixelRect permitted, PixelRect drawing)
    {
        if (permitted.Width < 0 || permitted.Height < 0 || drawing.Width < 0 || drawing.Height < 0) { return false; }
        var hidden = permitted.Width == 0 || permitted.Height == 0;
        var empty = drawing.Width == 0 || drawing.Height == 0;
        if (hidden || empty) { return hidden && empty; }
        return drawing.Left >= permitted.Left && drawing.Top >= permitted.Top &&
            drawing.Right <= permitted.Right && drawing.Bottom <= permitted.Bottom;
    }
}
