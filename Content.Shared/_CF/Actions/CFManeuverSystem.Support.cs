using Content.Shared.CF.Character;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Crafting;
using Content.Shared.CF.Preferences;
using Content.Shared.Examine;
using Content.Shared.Mobs.Components;
using Content.Shared.Verbs;
using Robust.Shared.Utility;

namespace Content.Shared.CF.Actions;

/// <summary>Reposition, Aid, and Recall Knowledge.</summary>
public sealed partial class CFManeuverSystem
{
    private const int AidDc = 15;

    private const int RepositionBackfireFeet = 5;

    /// <summary>Creatures have no trait data yet, so a character uses whichever of these it knows best.</summary>
    private static readonly (string Skill, CFAbility Ability)[] RecallSkills =
    {
        ("Arcana", CFAbility.Intelligence),
        ("Nature", CFAbility.Wisdom),
        ("Occultism", CFAbility.Intelligence),
        ("Religion", CFAbility.Wisdom),
        ("Society", CFAbility.Intelligence),
    };

    private static readonly CFDefenseType[] Saves =
    {
        CFDefenseType.Fortitude,
        CFDefenseType.Reflex,
        CFDefenseType.Will,
    };

    [Dependency] private ExamineSystemShared _examine = default!;

    /// <summary>
    /// One action, Athletics against Fortitude, when stock's move-pulled input is used on a grabbed creature.
    /// Stock then moves it. On a critical failure the target shoves the user 5 feet instead.
    /// </summary>
    public bool TryReposition(EntityUid user, EntityUid target)
    {
        if (!_net.IsServer || !HasStats(user) || !_actions.TrySpendActions(user, 1))
            return false;

        var degree = _conditions.RollSkill(user, target, "Athletics", CFAbility.Strength, CFDefenseType.Fortitude, attackTrait: true);
        if (degree.IsSuccess())
            return true;

        if (degree == CFDegree.CriticalFailure)
            _conditions.Push(target, user, RepositionBackfireFeet);

        _popup.PopupEntity(Loc.GetString("cf-act-reposition-fail"), user, user);
        return false;
    }

    [SubscribeLocalEvent]
    private void OnGetVerbs(Entity<CFCombatantComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        var user = args.User;
        if (!args.CanAccess || !args.CanInteract || user == ent.Owner || !HasStats(user) || _factions.CanHarm(user, ent))
            return;

        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString("cf-act-aid"),
            Act = () => TryAid(user, ent),
        });
    }

    /// <summary>
    /// One action now, and the result waits for the ally's next check. The printed Aid uses whichever check fits
    /// what the helper describes, and there is no one to ask, so this rolls Perception.
    /// </summary>
    public bool TryAid(EntityUid helper, EntityUid ally)
    {
        if (!_net.IsServer || !_actions.TrySpendActions(helper, 1))
            return false;

        var degree = CFDefense.Roll(_random, _conditions.PerceptionModifier(helper), AidDc);
        var bonus = degree switch
        {
            CFDegree.CriticalSuccess => 2,
            CFDegree.Success => 1,
            CFDegree.CriticalFailure => -1,
            _ => 0,
        };

        if (bonus != 0)
            _conditions.SetAid(ally, bonus);

        _popup.PopupEntity(Loc.GetString(bonus > 0 ? "cf-act-aid-success" : "cf-act-aid-fail"), ally, helper);
        return bonus > 0;
    }

    [SubscribeLocalEvent]
    private void OnGetExamineVerbs(Entity<CFCombatantComponent> ent, ref GetVerbsEvent<ExamineVerb> args)
    {
        var user = args.User;
        if (user == ent.Owner || !HasStats(user) || !HasComp<MobStateComponent>(ent))
            return;

        args.Verbs.Add(new ExamineVerb
        {
            Text = Loc.GetString("cf-act-recall"),
            Message = Loc.GetString("cf-act-recall-message"),
            Category = VerbCategory.Examine,
            Icon = new SpriteSpecifier.Texture(new("/Textures/Interface/VerbIcons/information.svg.192dpi.png")),
            Act = () => TryRecall(user, ent),
        });
    }

    /// <summary>
    /// One action and a secret check against the DC for the creature's level. Each character gets one try per
    /// creature. A critical failure tells it something false, and it looks just like the truth.
    /// </summary>
    public CFDegree? TryRecall(EntityUid user, EntityUid target)
    {
        if (!_net.IsServer || !_conditions.CanRecall(user, target) || !_actions.TrySpendActions(user, 1))
            return null;

        _conditions.NoteRecall(user, target);
        var level = CompOrNull<CFCombatantComponent>(target)?.Level ?? 0;
        var degree = RollRecall(user, CFCraftingRules.LevelDc(level));
        var text = RecallText(target, level, degree);
        _examine.SendExamineTooltip(user, target, FormattedMessage.FromMarkupOrThrow(text), getVerbs: false, centerAtCursor: false);
        return degree;
    }

    private CFDegree RollRecall(EntityUid user, int dc)
    {
        var best = RecallSkills[0];
        var bestModifier = int.MinValue;
        if (TryComp(user, out CFCombatantComponent? combat) && TryComp(user, out CFAbilityScoreComponent? abilities))
        {
            foreach (var option in RecallSkills)
            {
                var modifier = CFDefense.SkillModifier(_prototypes, combat, abilities, option.Skill, option.Ability, _conditions.SkillBonus(user, option.Skill));
                if (modifier <= bestModifier)
                    continue;

                best = option;
                bestModifier = modifier;
            }
        }

        return _conditions.RollSkillAgainst(user, best.Skill, best.Ability, dc, out _);
    }

    private string RecallText(EntityUid target, int level, CFDegree degree)
    {
        var weakest = WeakestSave(target);
        switch (degree)
        {
            case CFDegree.CriticalSuccess:
                var armor = CompOrNull<CFCombatantComponent>(target)?.ArmorClass ?? 10;
                return Loc.GetString("cf-act-recall-critical", ("level", level), ("save", weakest.ToString()), ("ac", armor));
            case CFDegree.Success:
                return Loc.GetString("cf-act-recall-success", ("level", level), ("save", weakest.ToString()));
            case CFDegree.CriticalFailure:
                var wrong = Saves[(Array.IndexOf(Saves, weakest) + _random.Next(1, Saves.Length)) % Saves.Length];
                return Loc.GetString("cf-act-recall-success", ("level", level + _random.Next(1, 4)), ("save", wrong.ToString()));
            default:
                return Loc.GetString("cf-act-recall-fail");
        }
    }

    private CFDefenseType WeakestSave(EntityUid target)
    {
        var weakest = Saves[0];
        var lowest = int.MaxValue;
        var combat = CompOrNull<CFCombatantComponent>(target);
        var abilities = CompOrNull<CFAbilityScoreComponent>(target);
        foreach (var save in Saves)
        {
            var modifier = CFDefense.SaveModifier(_prototypes, combat, abilities, save);
            if (modifier >= lowest)
                continue;

            weakest = save;
            lowest = modifier;
        }

        return weakest;
    }
}
