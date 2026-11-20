using Content.Shared.CF.Effects;

namespace Content.Shared.CF.Alchemy;

/// <summary>
/// What a potion or elixir does to whoever drinks it, as a list of effects: a timed bonus (Cheetah's Elixir), a
/// condition (Darkvision Elixir, Invisibility Potion), and so on. Goes beside <see cref="CFAlchemicalItemComponent"/>,
/// which handles the drinking. Healing stays on <see cref="CFHealConsumableComponent"/>, so a healing puddle keeps it.
/// </summary>
[RegisterComponent]
public sealed partial class CFDrinkEffectsComponent : Component
{
    [DataField(required: true)]
    public List<CFEffect> Effects = new();
}
