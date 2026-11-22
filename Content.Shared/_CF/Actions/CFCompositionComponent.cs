using Content.Shared.CF.Effects;
using Content.Shared.Popups;
using Robust.Shared.Network;

namespace Content.Shared.CF.Actions;

/// <summary>
/// A Bard's composition cantrip other than Courageous Anthem: Rallying Anthem, Triple Time, Song of Strength. Using the
/// action performs it for <see cref="Seconds"/>, ending any other composition, and its <see cref="Effects"/> work in the
/// Bard's aura while it runs. Playing a held instrument afterwards keeps it going. Parent on <c>CFActivityInstant</c>.
/// </summary>
[RegisterComponent]
public sealed partial class CFCompositionComponent : Component
{
    /// <summary>Names the composition, so starting it again extends it and another one replaces it.</summary>
    [DataField(required: true)]
    public string Id = string.Empty;

    /// <summary>The rules give a round. Here a composition lasts a minute, as Courageous Anthem does.</summary>
    [DataField]
    public float Seconds = 60f;

    [DataField(required: true)]
    public List<CFAuraEffect> Effects = new();
}

public sealed partial class CFCompositionSystem : EntitySystem
{
    [Dependency] private INetManager _net = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private CFAuraSystem _auras = default!;

    [SubscribeLocalEvent]
    private void OnPerform(Entity<CFCompositionComponent> ent, ref CFActivityInstantEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        if (!_net.IsServer)
            return;

        var effects = new List<CFAuraEffect>();
        foreach (var effect in ent.Comp.Effects)
        {
            effects.Add(new CFAuraEffect
            {
                Affects = effect.Affects,
                SizeFeet = effect.SizeFeet,
                IncludeSelf = effect.IncludeSelf,
                Stat = effect.Stat,
                Type = effect.Type,
                Value = effect.Value,
                Trait = effect.Trait,
                Skill = effect.Skill,
            });
        }

        _auras.Compose(args.Performer, ent.Comp.Id, effects, TimeSpan.FromSeconds(ent.Comp.Seconds));
        _popup.PopupEntity(Loc.GetString("cf-composition-start", ("name", Name(ent))), args.Performer, args.Performer);
    }
}
