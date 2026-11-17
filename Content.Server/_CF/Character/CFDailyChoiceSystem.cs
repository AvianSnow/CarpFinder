using Content.Shared.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Preferences;
using Robust.Shared.Prototypes;

namespace Content.Server.CF.Character;

/// <summary>
/// Picks that change each day, such as Flexible Studies' skill or a Fey-Touched Gnome's cantrip. The player plans the next
/// one on the character sheet, and a long rest swaps it in: what the old pick gave is taken back and the new one is given.
/// The plan comes from a client, so it is checked here.
/// </summary>
public sealed partial class CFDailyChoiceSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private CFCharacterFeatureSystem _features = default!;
    [Dependency] private SharedActionsSystem _actions = default!;

    [SubscribeNetworkEvent]
    private void OnPlan(CFPlanDailyChoicesEvent ev, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is { } mob)
            TrySetPlan(mob, ev.Picks);
    }

    public bool TrySetPlan(EntityUid mob, List<CFOptionPick> plan)
    {
        if (!TryComp(mob, out CFExperienceComponent? xp)
            || xp.Build == null
            || !CFOptionChoices.IsValidDailyPlan(_prototypes, xp.Build, xp.AncestryId, Level(mob), plan))
        {
            return false;
        }

        xp.DailyPlan = plan.ConvertAll(pick => pick.Copy());
        Dirty(mob, xp);
        return true;
    }

    private int Level(EntityUid mob)
    {
        return CompOrNull<CFCombatantComponent>(mob)?.Level ?? 1;
    }

    /// <summary>A long rest: every daily pick becomes what was planned for it. One with no plan stays as it is.</summary>
    public void Apply(EntityUid mob)
    {
        if (!TryComp(mob, out CFExperienceComponent? xp) || xp.Build == null || xp.DailyPlan.Count == 0)
            return;

        var changed = new HashSet<string>();
        foreach (var slot in CFOptionChoices.DailySlots(_prototypes, xp.Build, xp.AncestryId, Level(mob)))
        {
            var planned = xp.DailyPlan.Find(pick => pick.Source == slot.Source && pick.Slot == slot.Slot);
            if (planned == null || !CFOptionChoices.Allowed(_prototypes, slot.Choice, xp.Build, xp.AncestryId).Contains(planned.Value))
                continue;

            var current = xp.OptionPicks.Find(pick => pick.Source == slot.Source && pick.Slot == slot.Slot);
            if (current != null && current.Value == planned.Value)
                continue;

            xp.OptionPicks.RemoveAll(pick => pick.Source == slot.Source && pick.Slot == slot.Slot);
            xp.Build.OptionPicks.RemoveAll(pick => pick.Source == slot.Source && pick.Slot == slot.Slot);
            if (current != null)
                TakeBack(mob, xp, current.Value);

            xp.OptionPicks.Add(planned.Copy());
            xp.Build.OptionPicks.Add(planned.Copy());
            changed.Add(slot.Source);
        }

        foreach (var source in changed)
        {
            _features.ApplyOptionPicks(mob, source);
        }

        if (changed.Count > 0)
            Dirty(mob, xp);
    }

    /// <summary>
    /// Undoes what a daily pick gave: a trained skill, a cantrip or a signature spell. Something the character also has
    /// from elsewhere is left alone.
    /// </summary>
    private void TakeBack(EntityUid mob, CFExperienceComponent xp, string value)
    {
        // Another pick still gives the same thing.
        if (xp.OptionPicks.Exists(pick => pick.Value == value))
            return;

        if (value.StartsWith(CFOptionChoices.SignaturePrefix, StringComparison.Ordinal))
        {
            if (TryComp(mob, out CFSpellbookComponent? book)
                && book.SignatureSpells.Remove(value[CFOptionChoices.SignaturePrefix.Length..]))
            {
                Dirty(mob, book);
            }

            return;
        }

        if (_prototypes.HasIndex<CFSpellPrototype>(value))
        {
            var action = "CF" + value;
            foreach (var known in _actions.GetActions(mob))
            {
                if (MetaData(known).EntityPrototype?.ID != action)
                    continue;

                _actions.RemoveAction(mob, known.AsNullable());
                break;
            }

            return;
        }

        // A skill the class or the background also trains stays trained.
        if (xp.Build is { } build
            && (build.ClassTrainedSkills.Exists(skill => skill.Id == value) || build.BackgroundTrainedSkills.Exists(skill => skill.Id == value)))
        {
            return;
        }

        if (TryComp(mob, out CFCombatantComponent? combat) && combat.TrainedSkills.Remove(value))
            Dirty(mob, combat);
    }
}
