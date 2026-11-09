namespace Content.Client.CF.Hud;

/// <summary>
/// How the deck's columns are shared between its sections. A divider gives columns from one neighbour to the other,
/// and no section can go below <see cref="MinColumns"/>. Whole columns, because slots are a fixed size.
/// </summary>
public sealed class CFDeckWidths
{
    public const int SectionCount = 4;
    public const int MinColumns = 1;

    private static readonly int[] Defaults = { 3, 4, 1, 2 };

    private readonly int[] _columns;

    public CFDeckWidths()
        : this(Defaults)
    {
    }

    private CFDeckWidths(int[] columns)
    {
        _columns = (int[]) columns.Clone();
    }

    public int this[int section] => _columns[section];

    /// <summary>Drags a divider, positive to the right.</summary>
    /// <param name="divider">0 is between the first two sections.</param>
    /// <returns>How many columns it really moved, which is fewer when a section is at its minimum.</returns>
    public int Move(int divider, int steps)
    {
        var moved = steps > 0
            ? Math.Min(steps, _columns[divider + 1] - MinColumns)
            : Math.Max(steps, MinColumns - _columns[divider]);

        _columns[divider] += moved;
        _columns[divider + 1] -= moved;
        return moved;
    }

    public string Save()
    {
        return string.Join(',', _columns);
    }

    /// <summary>Reads what <see cref="Save"/> wrote. Anything that does not fill the deck exactly gives the defaults.</summary>
    public static CFDeckWidths Parse(string text)
    {
        var parts = text.Split(',');
        if (parts.Length != SectionCount)
            return new CFDeckWidths();

        var columns = new int[SectionCount];
        var total = 0;
        for (var i = 0; i < SectionCount; i++)
        {
            if (!int.TryParse(parts[i], out columns[i]) || columns[i] < MinColumns)
                return new CFDeckWidths();

            total += columns[i];
        }

        return total == CFDeckLayout.TotalColumns ? new CFDeckWidths(columns) : new CFDeckWidths();
    }
}
