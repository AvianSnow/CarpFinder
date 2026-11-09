using Content.Client.CF.Hud;
using Content.Client.UserInterface.Systems.Hotbar.Widgets;
using Content.Shared.Bed.Sleep;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Detection;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Items;
using JetBrains.Annotations;
using Robust.Client.Player;
using Robust.Client.UserInterface.Controllers;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client.CF.Hud;

/// <summary>
/// Fills the hit point bars, Armor Class, spell trackers, saves and conditions from the local creature's Carpfinder components.
/// </summary>
[UsedImplicitly]
public sealed partial class CFHudUiController : UIController
{
    /// <summary>Saves and conditions only change now and then, so they are not read every frame.</summary>
    private const float StatusSeconds = 0.2f;

    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;

    private float _statusTimer;
    private int _acModifier;

    public override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (UIManager.GetActiveUIWidgetOrNull<CFGameHud>() is not { } hud)
            return;

        // The stock hotbar strip (hands, status panels, old slot row) stays hidden.
        // Storage windows still parent to that widget, above this HUD.
        if (UIManager.GetActiveUIWidgetOrNull<HotbarGui>() is { } hotbar
            && hotbar.HandContainer.Parent is { } row)
            row.Visible = false;

        if (_player.LocalEntity is not { } player
            || !EntityManager.TryGetComponent<CFCombatantComponent>(player, out var combatant))
        {
            hud.SetCombatant(false);
            hud.ActionPips.Set(null);
            hud.Deck.SetBook(null);
            hud.Status.Set(new List<CFHudStatus>());
            return;
        }

        _statusTimer -= args.DeltaSeconds;
        if (_statusTimer <= 0f)
        {
            _statusTimer = StatusSeconds;
            _acModifier = EntityManager.System<CFConditionSystem>().ArmorClassModifier(player);
            UpdateDefenses(hud, player, combatant);
            hud.Status.Set(Statuses(player, combatant));
        }

        hud.SetCombatant(true);
        UpdateBars(hud, combatant);
        hud.Armor.Set(combatant.ArmorClass, _acModifier, IsOffGuard(player));
        EntityManager.TryGetComponent<CFActionPoolComponent>(player, out var actions);
        hud.ActionPips.Set(actions);
        EntityManager.TryGetComponent<CFSpellbookComponent>(player, out var book);
        hud.Deck.SetBook(book);
    }

    private static void UpdateBars(CFGameHud hud, CFCombatantComponent combatant)
    {
        var max = Math.Max(0, combatant.MaxHp);
        var current = Math.Max(0, combatant.CurrentHp);
        var temp = Math.Max(0, combatant.TemporaryHp);
        hud.HpBar.Set(max <= 0 ? 0f : current / (float) max, Loc.GetString("cf-hud-bar-hp", ("current", current), ("max", max)));
        hud.TempBar.Set(max <= 0 ? 0f : temp / (float) max, Loc.GetString("cf-hud-bar-temp", ("amount", temp)));
        hud.HpBar.ToolTip = Loc.GetString("cf-hud-hp-tooltip", ("current", current), ("max", max));
        hud.TempBar.ToolTip = Loc.GetString("cf-hud-temp-tooltip", ("amount", temp));
    }

    private bool IsOffGuard(EntityUid player)
    {
        return EntityManager.TryGetComponent(player, out CFOffGuardComponent? flanked) && flanked.Active
               || EntityManager.TryGetComponent(player, out CFConditionComponent? cond) && cond.OffGuardAll
               || EntityManager.System<CFConditionSystem>().IsProne(player);
    }

    private void UpdateDefenses(CFGameHud hud, EntityUid player, CFCombatantComponent combat)
    {
        if (!EntityManager.TryGetComponent(player, out CFAbilityScoreComponent? abilities))
        {
            hud.Defenses.Visible = false;
            return;
        }

        var conditions = EntityManager.System<CFConditionSystem>();
        hud.Defenses.Visible = true;
        for (var i = 0; i < CFDefensePanel.Shown.Length; i++)
        {
            var defense = CFDefensePanel.Shown[i];
            var rank = defense == CFDefenseType.Perception
                ? CFDefense.PerceptionRank(_prototypes, combat)
                : CFDefense.SaveRank(_prototypes, combat, defense.ToString());

            hud.Defenses.Set(
                i,
                conditions.DefenseModifier(player, defense),
                CFDefense.SaveModifier(_prototypes, combat, abilities, defense),
                rank);
        }
    }

    private List<CFHudStatus> Statuses(EntityUid player, CFCombatantComponent combat)
    {
        var statuses = new List<CFHudStatus>();
        var conditions = EntityManager.System<CFConditionSystem>();
        EntityManager.TryGetComponent(player, out CFConditionComponent? cond);
        EntityManager.TryGetComponent(player, out CFCombatOptionsComponent? options);

        void Add(string name, bool good, int value = 0)
        {
            var text = Loc.GetString($"cf-hud-status-{name}");
            statuses.Add(new CFHudStatus(value > 0 ? $"{text} {value}" : text, good));
        }

        if (combat.Dying > 0)
            Add("dying", false, combat.Dying);
        if (combat.Wounded > 0)
            Add("wounded", false, combat.Wounded);
        if (EntityManager.HasComponent<SleepingComponent>(player))
            Add("asleep", false);
        if (conditions.IsProne(player))
            Add("prone", false);
        if (IsOffGuard(player))
            Add("off-guard", false);
        if (conditions.Frightened(player) is var frightened and > 0)
            Add("frightened", false, frightened);
        if (conditions.Clumsy(player) is var clumsy and > 0)
            Add("clumsy", false, clumsy);
        if (conditions.Stupefied(player) is var stupefied and > 0)
            Add("stupefied", false, stupefied);
        if (cond is { SickenedValue: > 0 })
            Add("sickened", false, cond.SickenedValue);
        if (cond is { Grabbed: true })
            Add("grabbed", false);
        if (cond is { Immobilized: true })
            Add("immobilized", false);
        if (cond is { Blinded: true })
            Add("blinded", false);
        if (cond is { Charmed: true })
            Add("charmed", false);
        if (cond is { Calmed: true })
            Add("calmed", false);
        if (EntityManager.System<CFEncumbranceSystem>().IsEncumbered(player))
            Add("encumbered", false);

        if (EntityManager.TryGetComponent(player, out CFActionPoolComponent? pool) && pool.MaxActions != 3)
            Add(pool.MaxActions > 3 ? "quickened" : "slowed", pool.MaxActions > 3);

        if (EntityManager.System<CFShieldSystem>().RaisedBonus(player) > 0)
            Add("shield-raised", true);
        if (cond is { ShieldHardness: > 0 })
            Add("spell-shield", true);
        if (options is { Raging: true })
            Add("raging", true);
        if (options is { Panache: true })
            Add("panache", true);
        if (EntityManager.TryGetComponent(player, out CFStealthComponent? stealth) && stealth.Active)
            Add("hidden", true);
        if (cond is { Concealed: true })
            Add("concealed", true);
        if (cond is { Sanctuary: true })
            Add("sanctuary", true);

        AddBonus(statuses, player, cond, "attack", CFBonusStat.AttackRoll);
        AddBonus(statuses, player, cond, "damage", CFBonusStat.Damage, CFBonusStat.MeleeDamage);
        AddBonus(statuses, player, cond, "saves", CFBonusStat.Saves);
        AddBonus(statuses, player, cond, "perception", CFBonusStat.Perception);
        AddBonus(statuses, player, cond, "skills", CFBonusStat.SkillChecks);
        AddBonus(statuses, player, cond, "speed", CFBonusStat.Speed);

        // Last, because it comes and goes with every Strike and the rest of the row should not shift.
        if (pool is { MapAttacks: > 0 })
        {
            var map = EntityManager.System<CFStrikeSystem>().NextStrikeMapPenalty(player);
            statuses.Add(new CFHudStatus($"{Loc.GetString("cf-hud-status-map")} {CFSkills.FormatSigned(map)}", false));
        }

        return statuses;
    }

    /// <summary>A spell's bonus or penalty. Lasting ones are on the sheet, and sickened already has its own entry.</summary>
    private void AddBonus(
        List<CFHudStatus> statuses,
        EntityUid player,
        CFConditionComponent? cond,
        string name,
        CFBonusStat stat,
        CFBonusStat? also = null)
    {
        var value = EntityManager.System<CFBonusSystem>().TimedTotal(player, stat, also);
        if (value < 0 && stat != CFBonusStat.Damage && stat != CFBonusStat.Speed)
            value = Math.Min(0, value + (cond?.SickenedValue ?? 0));

        if (value != 0)
            statuses.Add(new CFHudStatus($"{Loc.GetString($"cf-hud-status-{name}")} {CFSkills.FormatSigned(value)}", value > 0));
    }
}
