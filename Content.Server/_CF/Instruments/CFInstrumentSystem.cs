using Content.Server.Instruments;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Instruments;
using Content.Shared.Instruments.UI;
using Content.Shared.Interaction;
using Content.Shared.Item;
using Content.Shared.Mobs.Systems;
using Robust.Server.GameObjects;

namespace Content.Server.CF.Instruments;

/// <summary>
/// On a held instrument whose player closed its window while the music played. Stock knows who plays an instrument only
/// through its open window, so this remembers them.
/// </summary>
[RegisterComponent]
public sealed partial class CFKeepPlayingComponent : Component
{
    [DataField]
    public EntityUid Player;
}

/// <summary>
/// Lets a held instrument go on playing after its window closes. Stock stops the music the moment the window shuts.
/// Here it plays on, from the song the player chose, for as long as they hold it in either hand (or stay within reach of a
/// placed one, such as a piano) and are conscious. The renderer that plays a song lives on the instrument on the player's
/// client, not on the window. The stock window stopped the song itself as it closed; a marked edit in
/// <c>InstrumentMenu.ExitedTree</c> leaves that to this system. Hooked into the stock <see cref="InstrumentSystem"/> with
/// marked edits.
/// </summary>
public sealed partial class CFInstrumentSystem : EntitySystem
{
    [Dependency] private InstrumentSystem _instruments = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;

    private readonly List<(EntityUid Uid, bool Clean)> _stopping = new();

    /// <summary>
    /// Called as the window closes. True when the music carries on, so stock must leave the instrument alone.
    /// </summary>
    public bool TryKeepPlaying(EntityUid uid, InstrumentComponent instrument, EntityUid player)
    {
        if (!instrument.Playing || !_mobState.IsAlive(player) || !AtInstrument(player, uid))
            return false;

        EnsureComp<CFKeepPlayingComponent>(uid).Player = player;
        return true;
    }

    /// <summary>
    /// Called as the window opens. True when the same player opens it on music they kept playing, so stock must not
    /// stop it. Either way the window now says who plays.
    /// </summary>
    public bool Reopened(EntityUid uid, InstrumentComponent instrument, EntityUid player)
    {
        if (!TryComp(uid, out CFKeepPlayingComponent? keep))
            return false;

        RemComp<CFKeepPlayingComponent>(uid);
        return keep.Player == player && instrument.Playing;
    }

    /// <summary>
    /// The player can go on playing: they hold it in either hand, or it is a placed instrument such as a piano and they
    /// are still within reach of it.
    /// </summary>
    public bool AtInstrument(EntityUid player, EntityUid uid)
    {
        if (_hands.IsHolding(player, uid))
            return true;

        return !HasComp<ItemComponent>(uid) && _interaction.InRangeUnobstructed(player, uid);
    }

    /// <summary>Who plays the instrument with its window shut, if anyone.</summary>
    public EntityUid? Player(EntityUid uid)
    {
        return CompOrNull<CFKeepPlayingComponent>(uid)?.Player;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _stopping.Clear();
        var query = EntityQueryEnumerator<CFKeepPlayingComponent, InstrumentComponent>();
        while (query.MoveNext(out var uid, out var keep, out var instrument))
        {
            // The song ended, or the player stopped it.
            if (!instrument.Playing)
                _stopping.Add((uid, false));
            else if (TerminatingOrDeleted(keep.Player)
                     || !_mobState.IsAlive(keep.Player)
                     || !AtInstrument(keep.Player, uid))
                _stopping.Add((uid, true));
        }

        foreach (var (uid, clean) in _stopping)
        {
            Stop(uid, clean);
        }
    }

    private void Stop(EntityUid uid, bool clean)
    {
        RemComp<CFKeepPlayingComponent>(uid);
        if (clean)
            _instruments.Clean(uid);

        if (!_ui.IsUiOpen(uid, InstrumentUiKey.Key))
            RemComp<ActiveInstrumentComponent>(uid);
    }
}
