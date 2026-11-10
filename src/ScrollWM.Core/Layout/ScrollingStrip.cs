using ScrollWM.Core.Windows;

namespace ScrollWM.Core.Layout;

public sealed record StripColumn(WindowIdentity Identity, int Width, ColumnWidth? Preset, int MinimumWidth = 1);
public sealed record WindowTarget(WindowIdentity Identity, PixelRect Bounds);

// Mutable only through commands; no UI, desktop access, or persistence.
public sealed class ScrollingStrip
{
    private readonly List<StripColumn> columns = [];
    public IReadOnlyList<StripColumn> Columns => columns.AsReadOnly();
    public WindowIdentity? Focused { get; private set; }
    public int Offset { get; private set; }
    public PixelRect Viewport { get; }
    public int Gap { get; }

    public ScrollingStrip(PixelRect workArea, int gap = 12, int margin = 6)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(gap);
        ArgumentOutOfRangeException.ThrowIfNegative(margin);
        if (workArea.Width <= (long)margin * 2 || workArea.Height <= (long)margin * 2)
        {
            throw new ArgumentException("The work area must contain the requested margins.", nameof(workArea));
        }

        Gap = gap;
        Viewport = new(checked(workArea.Left + margin), checked(workArea.Top + margin),
            workArea.Width - margin * 2, workArea.Height - margin * 2);
    }

    public void InsertAfterFocus(WindowIdentity identity)
    {
        if (columns.Any(item => item.Identity.Handle == identity.Handle))
        {
            throw new ArgumentException("The window is already in this strip.", nameof(identity));
        }

        var index = Focused is null ? columns.Count : IndexOf(Focused) + 1;
        Reinsert(new(identity, ColumnWidths.ResolvePixels(ColumnWidth.Half, Viewport.Width, Gap), ColumnWidth.Half), index, focus: true);
    }

    public void Reinsert(StripColumn column, int index, bool focus)
    {
        if (columns.Any(item => item.Identity.Handle == column.Identity.Handle))
        {
            throw new ArgumentException("The window is already in this strip.", nameof(column));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(column.Width);
        columns.Insert(Math.Clamp(index, 0, columns.Count), column);
        if (focus || Focused is null) { Focused = column.Identity; }
        RevealFocus();
    }

    public bool Remove(WindowIdentity identity)
    {
        var index = columns.FindIndex(item => item.Identity == identity);
        if (index < 0)
        {
            return false;
        }

        columns.RemoveAt(index);
        if (Focused == identity)
        {
            Focused = columns.Count == 0 ? null : columns[Math.Min(index, columns.Count - 1)].Identity;
        }

        RevealFocus();
        return true;
    }

    public void Focus(WindowIdentity identity)
    {
        _ = IndexOf(identity);
        Focused = identity;
        RevealFocus();
    }

    public bool FocusAdjacent(int direction)
    {
        ValidateDirection(direction);
        if (Focused is null)
        {
            return false;
        }

        var index = IndexOf(Focused);
        var target = Math.Clamp(index + direction, 0, columns.Count - 1);
        Focus(columns[target].Identity);
        return target != index;
    }

    public bool ReorderFocused(int direction)
    {
        ValidateDirection(direction);
        if (Focused is null)
        {
            return false;
        }

        var index = IndexOf(Focused);
        var target = Math.Clamp(index + direction, 0, columns.Count - 1);
        (columns[index], columns[target]) = (columns[target], columns[index]);
        RevealFocus();
        return target != index;
    }

    public void CycleWidth()
    {
        if (Focused is null)
        {
            return;
        }

        var index = IndexOf(Focused);
        var column = columns[index];
        var next = column.Preset switch
        {
            ColumnWidth.Third => ColumnWidth.Half,
            ColumnWidth.Half => ColumnWidth.TwoThirds,
            ColumnWidth.TwoThirds => ColumnWidth.Full,
            ColumnWidth.Full => ColumnWidth.Third,
            _ => ColumnWidth.Half,
        };
        columns[index] = column with { Preset = next, Width = Math.Max(column.MinimumWidth, ColumnWidths.ResolvePixels(next, Viewport.Width, Gap)) };
        RevealFocus();
    }

    public void SetCustomWidth(WindowIdentity identity, int width)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        var index = IndexOf(identity);
        columns[index] = columns[index] with { Width = Math.Max(width, columns[index].MinimumWidth), Preset = null };
        RevealFocus();
    }

    public void SetMinimumWidth(WindowIdentity identity, int minimum)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minimum);
        var index = IndexOf(identity);
        var column = columns[index];
        columns[index] = column with { MinimumWidth = minimum, Width = Math.Max(column.Width, minimum) };
        RevealFocus();
    }

    public WindowTarget[] Layout()
    {
        var left = 0;
        var result = new List<WindowTarget>();
        foreach (var column in columns)
        {
            result.Add(new(column.Identity, new(checked(Viewport.Left + left - Offset), Viewport.Top, column.Width, Viewport.Height)));
            left = checked(left + column.Width + Gap);
        }

        return result.ToArray();
    }

    private void RevealFocus()
    {
        var total = columns.Aggregate(0, (sum, column) => checked(sum + column.Width + Gap));
        total = Math.Max(0, total - (columns.Count > 0 ? Gap : 0));
        Offset = Math.Clamp(Offset, 0, Math.Max(0, total - Viewport.Width));
        if (Focused is null)
        {
            return;
        }

        var start = 0;
        foreach (var column in columns)
        {
            if (column.Identity == Focused)
            {
                if (column.Width > Viewport.Width || start < Offset)
                {
                    Offset = start;
                }
                else if (start + column.Width > Offset + Viewport.Width)
                {
                    Offset = start + column.Width - Viewport.Width;
                }

                return;
            }

            start = checked(start + column.Width + Gap);
        }
    }

    private int IndexOf(WindowIdentity identity)
    {
        var index = columns.FindIndex(item => item.Identity == identity);
        return index >= 0 ? index : throw new ArgumentException("The window is not in this strip.", nameof(identity));
    }

    private static void ValidateDirection(int direction)
    {
        if (direction is not (-1 or 1))
        {
            throw new ArgumentOutOfRangeException(nameof(direction), "Use -1 or 1.");
        }
    }
}
