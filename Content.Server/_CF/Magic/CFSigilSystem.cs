using System.Numerics;
using Content.Server.Decals;
using Content.Shared.CF.Magic;
using Content.Shared.Decals;
using Content.Shared.Popups;
using Robust.Shared.Random;

namespace Content.Server.CF.Magic;

/// <summary>The rune a caster's Sigil last painted, so casting it again can move it.</summary>
[RegisterComponent]
public sealed partial class CFSigilCasterComponent : Component
{
    [ViewVariables]
    public EntityUid Grid;

    [ViewVariables]
    public DecalIndex Rune;
}

/// <summary>
/// Sigil: a glowing rune painted on the floor, as a crayon paints one. A caster has one at a time. It can be scrubbed off
/// like any crayon mark. PF2e lets it be hidden and put on a creature, which is not built.
/// </summary>
public sealed partial class CFSigilSystem : EntitySystem
{
    public static readonly string[] Runes = { "rune1", "rune2", "rune3", "rune4", "rune5", "rune6" };

    private static readonly Color Glow = Color.FromHex("#b48cffdd");

    [Dependency] private DecalSystem _decals = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    [SubscribeLocalEvent]
    private void OnPlace(ref CFPlaceSigilEvent args)
    {
        if (_transform.GetGrid(args.Spot) is not { } grid)
            return;

        if (TryComp(args.Caster, out CFSigilCasterComponent? old))
            _decals.RemoveDecal(old.Grid, old.Rune);

        // A decal's position is its corner, so it is moved back half a tile to sit on the spot.
        var corner = args.Spot.Offset(new Vector2(-0.5f, -0.5f));
        if (!_decals.TryAddDecal(_random.Pick(Runes), corner, out var rune, Glow, cleanable: true))
            return;

        var caster = EnsureComp<CFSigilCasterComponent>(args.Caster);
        caster.Grid = grid;
        caster.Rune = rune;
        _popup.PopupEntity(Loc.GetString("cf-spell-sigil-placed"), args.Caster, args.Caster);
    }
}
