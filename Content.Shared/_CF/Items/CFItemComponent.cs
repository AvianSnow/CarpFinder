using Content.Shared.CF.Character;
using Robust.Shared.GameStates;

namespace Content.Shared.CF.Items;

/// <summary>
/// Pathfinder item data: item level and rarity. Price stays on <c>CFItemPrice</c>, Bulk on <see cref="CFBulkComponent"/>.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CFItemComponent : Component
{
    [DataField, AutoNetworkedField]
    public int Level;

    [DataField, AutoNetworkedField]
    public CFRarity Rarity = CFRarity.Common;
}

/// <summary>
/// How much an item weighs and how awkward it is to carry, in remaster Bulk.
/// 0 is negligible, 0.1 is light (L), then 1, 2, and so on. Ten light items make 1 Bulk.
/// </summary>
/// <remarks>
/// SS14 item size still decides what fits in a bag's storage grid. Items without this component
/// get a Bulk from their size (see <see cref="CFBulk.FromItemSize"/>).
/// </remarks>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CFBulkComponent : Component
{
    [DataField, AutoNetworkedField]
    public float Bulk = 0.1f;
}

/// <summary>
/// What a creature carries. Encumbered above 5 + Strength Bulk (clumsy 1, −10 ft Speed).
/// It cannot pick up anything that takes it above 10 + Strength.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, Access(typeof(CFEncumbranceSystem))]
public sealed partial class CFEncumbranceComponent : Component
{
    /// <summary>Carried Bulk in tenths, so light items add up exactly.</summary>
    [DataField, AutoNetworkedField]
    public int CarriedTenths;

    [DataField, AutoNetworkedField]
    public bool Encumbered;

    [DataField]
    public TimeSpan NextCheck;
}

public static class CFBulk
{
    public const int Light = 1;
    public const int One = 10;

    public const int EncumberedBase = 5;
    public const int MaximumBase = 10;

    public static int ToTenths(float bulk)
    {
        return Math.Max(0, (int) MathF.Round(bulk * One));
    }

    /// <summary>Whole Bulk. Light items below 10 do not round up.</summary>
    public static int Whole(int tenths)
    {
        return tenths / One;
    }

    public static bool IsEncumbered(int tenths, int strength)
    {
        return Whole(tenths) > EncumberedBase + strength;
    }

    public static bool IsOverMaximum(int tenths, int strength)
    {
        return Whole(tenths) > MaximumBase + strength;
    }

    /// <summary>
    /// Bulk for an item with no <see cref="CFBulkComponent"/>, from its SS14 size:
    /// tiny is negligible, small and normal are light, large is 1, huge is 2, larger is 3.
    /// </summary>
    public static int FromItemSize(string size)
    {
        return size switch
        {
            "Tiny" => 0,
            "Small" or "Normal" => Light,
            "Large" => One,
            "Huge" => 2 * One,
            _ => 3 * One,
        };
    }

    /// <summary>"L", "—" (negligible), or the whole number, as printed in item stat blocks.</summary>
    public static string Format(int tenths)
    {
        if (tenths <= 0)
            return "—";

        if (tenths < One)
            return tenths == Light ? "L" : $"{tenths}L";

        return tenths % One == 0 ? Whole(tenths).ToString() : $"{Whole(tenths)}+{tenths % One}L";
    }
}
