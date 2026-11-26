using Content.Shared.CF.Combat;
using Content.Shared.Weapons.Melee;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Network;

namespace Content.Shared.CF.Gathering;

/// <summary>
/// Chopping and mining: an axe on a tree or a pick on rock needs no attack roll, only a few swings.
/// <see cref="CFStrikeSystem"/> asks here before it resolves a melee Strike.
/// </summary>
public sealed partial class CFToolWorkSystem : EntitySystem
{
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private CFObjectBreakSystem _break = default!;
    [Dependency] private INetManager _net = default!;

    /// <summary>Whether this tool does the kind of work the target takes.</summary>
    public bool CanWork(EntityUid tool, EntityUid target)
    {
        return TryComp(target, out CFWorkableComponent? workable) && Does(tool, workable.Kind);
    }

    /// <summary>Whether the tool does that kind of work at all.</summary>
    public bool Does(EntityUid tool, CFToolWork kind)
    {
        return TryComp(tool, out CFWorkToolComponent? work) && work.Kinds.Contains(kind);
    }

    /// <summary>
    /// One swing of the tool at the target. Returns false when this is not tool work, and the swing is a Strike.
    /// The last swing breaks the target the way its own damage would, so it drops what it drops.
    /// </summary>
    public bool TryWork(EntityUid user, EntityUid tool, EntityUid target)
    {
        if (!TryComp(target, out CFWorkableComponent? workable) || !Does(tool, workable.Kind))
            return false;

        if (!_net.IsServer)
            return true;

        var sound = workable.Sound ?? CompOrNull<MeleeWeaponComponent>(tool)?.HitSound;
        if (sound != null)
            _audio.PlayPvs(sound, Transform(target).Coordinates, sound.Params.WithVariation(0.05f));

        workable.Progress++;
        if (workable.Progress >= workable.Hits)
            _break.Break(user, target);

        return true;
    }
}
