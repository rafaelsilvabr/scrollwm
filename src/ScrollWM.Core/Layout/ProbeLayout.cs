using ScrollWM.Core.Windows;

namespace ScrollWM.Core.Layout;

public enum ProbePosition
{
    Center,
    LeftPartial,
    LeftFull,
    RightPartial,
    RightFull,
}

public static class ProbeLayout
{
    // A diagnostic target, not the eventual scrolling-strip layout engine.
    public static PixelRect Target(PixelRect workArea, int width, ProbePosition position)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(workArea.Width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(workArea.Height);

        var left = position switch
        {
            ProbePosition.Center => workArea.Left + (workArea.Width - width) / 2,
            ProbePosition.LeftPartial => workArea.Left - width / 2,
            ProbePosition.LeftFull => workArea.Left - width - 32,
            ProbePosition.RightPartial => workArea.Right - width / 2,
            ProbePosition.RightFull => workArea.Right + 32,
            _ => throw new ArgumentOutOfRangeException(nameof(position)),
        };

        return new(left, workArea.Top, width, workArea.Height);
    }
}
