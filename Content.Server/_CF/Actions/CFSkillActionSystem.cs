using Content.Server.CF.Scent;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Afflictions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Crafting;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Factions;
using Content.Shared.CF.Items;
using Content.Shared.CF.Medicine;
using Content.Shared.CF.Preferences;
using Content.Shared.CF.Scent;
using Content.Shared.DoAfter;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server.CF.Actions;

/// <summary>
/// Skill actions reached by right-click instead of a button. On yourself: Perform, Subsist, Track and Cover Tracks.
/// On another creature: Steal, Treat Poison and Treat Disease. On a damaged shield: Repair.
/// </summary>
public sealed partial class CFSkillActionSystem : EntitySystem
{
    /// <summary>The rules give Repair 10 minutes, Subsist a day and Treat Disease 8 hours. Here each is a short wait.</summary>
    private const float WorkSeconds = 12f;

    private const float QuickRepairSeconds = 3f;
    private const float TrackSeconds = 3f;

    /// <summary>What Subsist and Track have to beat. The rules set it by the place.</summary>
    private const int WildernessDc = 15;

    /// <summary>Taking from a pocket, which is all Steal can reach here, is at -5 without Pickpocket.</summary>
    private const int PocketPenalty = 5;

    private static readonly TimeSpan SubsistCooldown = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan TrackCooldown = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan TrackTime = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan CriticalTrackTime = TimeSpan.FromMinutes(5);

    private static readonly EntProtoId Ration = "CFRation";

    private static readonly string[] Pockets = { "pocket1", "pocket2" };

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private MovementSpeedModifierSystem _speed = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private CFActionPoolSystem _actions = default!;
    [Dependency] private CFAfflictionSystem _afflictions = default!;
    [Dependency] private CFConditionSystem _conditions = default!;
    [Dependency] private CFEffectSystem _effects = default!;
    [Dependency] private CFFactionSystem _factions = default!;
    [Dependency] private CFMedicineSystem _medicine = default!;
    [Dependency] private CFScentServerSystem _scent = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<CFSkillActionsComponent>();
        while (query.MoveNext(out var uid, out var skills))
        {
            if (skills.TrackingUntil == TimeSpan.Zero || now < skills.TrackingUntil)
                continue;

            skills.TrackingUntil = TimeSpan.Zero;
            if (!skills.BorrowedSmeller)
                continue;

            skills.BorrowedSmeller = false;
            if (TryComp(uid, out CFSmellerComponent? smeller))
            {
                _scent.SetSniffing((uid, smeller), false);
                RemComp<CFSmellerComponent>(uid);
            }
        }
    }

    [SubscribeLocalEvent]
    private void OnCreatureVerbs(Entity<CFCombatantComponent> ent, ref GetVerbsEvent<InteractionVerb> args)
    {
        var user = args.User;
        if (!args.CanInteract || !HasComp<CFCombatantComponent>(user) || !HasComp<CFAbilityScoreComponent>(user))
            return;

        if (user == ent.Owner)
        {
            AddSelfVerbs(user, ref args);
            return;
        }

        if (!args.CanAccess || !_mobState.IsAlive(ent))
            return;

        var target = ent.Owner;
        if (_factions.CanHarm(user, target))
        {
            args.Verbs.Add(new InteractionVerb
            {
                Text = Loc.GetString("cf-skill-steal"),
                Act = () => TrySteal(user, target),
            });
        }

        if (_afflictions.Has(target, CFConditionKind.Poisoned))
        {
            args.Verbs.Add(new InteractionVerb
            {
                Text = Loc.GetString("cf-skill-treat-poison"),
                Act = () => TryTreatPoison(user, target),
            });
        }

        if (_afflictions.Has(target, CFConditionKind.Diseased))
        {
            args.Verbs.Add(new InteractionVerb
            {
                Text = Loc.GetString("cf-skill-treat-disease"),
                Act = () => StartTreatDisease(user, target),
            });
        }
    }

    private void AddSelfVerbs(EntityUid user, ref GetVerbsEvent<InteractionVerb> args)
    {
        args.Verbs.Add(new InteractionVerb
        {
            Text = Loc.GetString("cf-skill-perform"),
            Act = () => TryPerform(user),
        });
        args.Verbs.Add(new InteractionVerb
        {
            Text = Loc.GetString("cf-skill-subsist"),
            Act = () => StartSubsist(user),
        });
        args.Verbs.Add(new InteractionVerb
        {
            Text = Loc.GetString("cf-skill-track"),
            Act = () => StartTrack(user),
        });
        args.Verbs.Add(new InteractionVerb
        {
            Text = Loc.GetString(CompOrNull<CFSkillActionsComponent>(user)?.CoveringTracks == true ? "cf-skill-cover-tracks-stop" : "cf-skill-cover-tracks"),
            Act = () => ToggleCoverTracks(user),
        });

        // A patient can tend its own poison or disease.
        if (_afflictions.Has(user, CFConditionKind.Poisoned))
        {
            args.Verbs.Add(new InteractionVerb
            {
                Text = Loc.GetString("cf-skill-treat-poison"),
                Act = () => TryTreatPoison(user, user),
            });
        }

        if (_afflictions.Has(user, CFConditionKind.Diseased))
        {
            args.Verbs.Add(new InteractionVerb
            {
                Text = Loc.GetString("cf-skill-treat-disease"),
                Act = () => StartTreatDisease(user, user),
            });
        }
    }

    private CFProficiencyRank Rank(EntityUid user, string skill)
    {
        return TryComp(user, out CFCombatantComponent? combat)
            ? CFDefense.SkillRank(_prototypes, combat, skill)
            : CFProficiencyRank.Untrained;
    }

    private bool NeedsTrained(EntityUid user, string skill)
    {
        if (Rank(user, skill) >= CFProficiencyRank.Trained)
            return true;

        _popup.PopupEntity(Loc.GetString("cf-skill-untrained", ("skill", skill)), user, user);
        return false;
    }

    private CFOptions Options(EntityUid user)
    {
        return new CFOptions(CompOrNull<CFCombatOptionsComponent>(user));
    }

    private readonly record struct CFOptions(CFCombatOptionsComponent? Comp)
    {
        public bool Pickpocket => Comp?.Pickpocket == true;
        public bool Forager => Comp?.Forager == true;
        public bool QuickRepair => Comp?.QuickRepair == true;
        public bool AcrobaticPerformer => Comp?.AcrobaticPerformer == true;
        public bool RobustRecovery => Comp?.RobustRecovery == true;
    }

    #region Steal

    /// <summary>
    /// One action: Thievery against the target's Perception DC, at -5 because a pocket is closely guarded. A success
    /// takes something from a pocket, and the target does not know. A failure is noticed by the target, and a
    /// critical failure by everyone.
    /// </summary>
    public bool TrySteal(EntityUid user, EntityUid target)
    {
        if (!_factions.CanHarm(user, target) || !_mobState.IsAlive(target) || !_actions.TrySpendActions(user, 1, stealthy: true))
            return false;

        var penalty = Options(user).Pickpocket ? 0 : PocketPenalty;
        var degree = _conditions.RollSkillAgainst(user, "Thievery", CFAbility.Dexterity, _conditions.PerceptionDc(target) + penalty, out _);
        if (!degree.IsSuccess())
        {
            if (degree == CFDegree.CriticalFailure)
                _popup.PopupEntity(Loc.GetString("cf-skill-steal-caught"), user, PopupType.MediumCaution);
            else
                _popup.PopupEntity(Loc.GetString("cf-skill-steal-noticed"), user, target, PopupType.MediumCaution);

            _popup.PopupEntity(Loc.GetString("cf-skill-steal-fail"), user, user);
            return false;
        }

        var full = new List<string>();
        foreach (var slot in Pockets)
        {
            if (_inventory.TryGetSlotEntity(target, slot, out _))
                full.Add(slot);
        }

        if (full.Count == 0)
        {
            _popup.PopupEntity(Loc.GetString("cf-skill-steal-nothing"), user, user);
            return false;
        }

        var from = full[_random.Next(full.Count)];
        if (!_inventory.TryGetSlotEntity(target, from, out var item)
            || !_inventory.TryUnequip(target, from, silent: true, force: true))
        {
            _popup.PopupEntity(Loc.GetString("cf-skill-steal-nothing"), user, user);
            return false;
        }

        _hands.PickupOrDrop(user, item.Value, checkActionBlocker: false);
        _popup.PopupEntity(Loc.GetString("cf-skill-steal-done", ("item", item.Value)), user, user);
        return true;
    }

    #endregion

    #region Perform

    /// <summary>One action: Performance against the DC for the performer's level. Everyone near sees how it went.</summary>
    public CFDegree? TryPerform(EntityUid user)
    {
        if (!_actions.TrySpendActions(user, 1))
            return null;

        var level = CompOrNull<CFCombatantComponent>(user)?.Level ?? 0;
        var skill = "Performance";
        var ability = CFAbility.Charisma;

        // Acrobatic Performer: Acrobatics, when the performer is better at it.
        if (Options(user).AcrobaticPerformer
            && TryComp(user, out CFCombatantComponent? combat)
            && TryComp(user, out CFAbilityScoreComponent? abilities)
            && CFDefense.SkillModifier(_prototypes, combat, abilities, "Acrobatics", CFAbility.Dexterity, 0)
            > CFDefense.SkillModifier(_prototypes, combat, abilities, skill, ability, 0))
        {
            skill = "Acrobatics";
            ability = CFAbility.Dexterity;
        }

        var degree = _conditions.RollSkillAgainst(user, skill, ability, CFCraftingRules.LevelDc(level), out _);
        _popup.PopupEntity(Loc.GetString($"cf-skill-perform-{Key(degree)}"), user);
        return degree;
    }

    private static string Key(CFDegree degree)
    {
        return degree switch
        {
            CFDegree.CriticalSuccess => "critical",
            CFDegree.Success => "success",
            CFDegree.Failure => "failure",
            _ => "critical-failure",
        };
    }

    #endregion

    #region Subsist

    private void StartSubsist(EntityUid user)
    {
        var skills = EnsureComp<CFSkillActionsComponent>(user);
        if (_timing.CurTime < skills.NextSubsist)
        {
            _popup.PopupEntity(Loc.GetString("cf-skill-subsist-wait"), user, user);
            return;
        }

        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, user, WorkSeconds, new CFSubsistDoAfterEvent(), eventTarget: user)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
        });
    }

    [SubscribeLocalEvent]
    private void OnSubsisted(Entity<CFSkillActionsComponent> ent, ref CFSubsistDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled)
            return;

        args.Handled = true;
        Subsist(ent);
    }

    /// <summary>
    /// Survival against DC 15. A success finds one ration and a critical success two. With Forager a failure is a
    /// success, and the forager finds twice as much. A critical failure leaves the forager fatigued.
    /// </summary>
    public CFDegree Subsist(EntityUid user)
    {
        EnsureComp<CFSkillActionsComponent>(user).NextSubsist = _timing.CurTime + SubsistCooldown;
        var degree = _conditions.RollSkillAgainst(user, "Survival", CFAbility.Wisdom, WildernessDc, out _);
        var forager = Options(user).Forager;
        if (forager && !degree.IsSuccess())
            degree = CFDegree.Success;

        var rations = degree switch
        {
            CFDegree.CriticalSuccess => 2,
            CFDegree.Success => 1,
            _ => 0,
        };

        if (forager)
            rations *= 2;

        for (var i = 0; i < rations; i++)
        {
            _hands.PickupOrDrop(user, Spawn(Ration, Transform(user).Coordinates), checkActionBlocker: false);
        }

        if (degree == CFDegree.CriticalFailure)
            _conditions.ApplyCondition(user, CFConditionKind.Fatigued, 1, 60f, user, 0);

        _popup.PopupEntity(Loc.GetString(rations > 0 ? "cf-skill-subsist-found" : "cf-skill-subsist-nothing"), user, user);
        return degree;
    }

    #endregion

    #region Track and Cover Tracks

    private void StartTrack(EntityUid user)
    {
        if (!NeedsTrained(user, "Survival"))
            return;

        var skills = EnsureComp<CFSkillActionsComponent>(user);
        if (_timing.CurTime < skills.NextTrack)
        {
            _popup.PopupEntity(Loc.GetString("cf-skill-track-wait"), user, user);
            return;
        }

        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, user, TrackSeconds, new CFTrackDoAfterEvent(), eventTarget: user)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
        });
    }

    [SubscribeLocalEvent]
    private void OnTracked(Entity<CFSkillActionsComponent> ent, ref CFTrackDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled)
            return;

        args.Handled = true;
        Track(ent);
    }

    /// <summary>
    /// Survival against DC 15. A success shows the trails creatures have left for a minute, and a critical success
    /// for five: the same trails a creature with the Scent sense sniffs out.
    /// </summary>
    public CFDegree Track(EntityUid user)
    {
        var skills = EnsureComp<CFSkillActionsComponent>(user);
        var now = _timing.CurTime;
        skills.NextTrack = now + TrackCooldown;
        var degree = _conditions.RollSkillAgainst(user, "Survival", CFAbility.Wisdom, WildernessDc, out _);
        if (!degree.IsSuccess())
        {
            _popup.PopupEntity(Loc.GetString("cf-skill-track-fail"), user, user);
            return degree;
        }

        if (!TryComp(user, out CFSmellerComponent? smeller))
        {
            smeller = EnsureComp<CFSmellerComponent>(user);
            skills.BorrowedSmeller = true;
        }

        _scent.SetSniffing((user, smeller), true);
        skills.TrackingUntil = now + (degree == CFDegree.CriticalSuccess ? CriticalTrackTime : TrackTime);
        _popup.PopupEntity(Loc.GetString("cf-skill-track-found"), user, user);
        return degree;
    }

    /// <summary>Covering tracks: half Speed, and no scent trail is laid. Trained in Survival.</summary>
    public void ToggleCoverTracks(EntityUid user)
    {
        var skills = EnsureComp<CFSkillActionsComponent>(user);
        if (!skills.CoveringTracks && !NeedsTrained(user, "Survival"))
            return;

        skills.CoveringTracks = !skills.CoveringTracks;
        Dirty(user, skills);
        _speed.RefreshMovementSpeedModifiers(user);
        _popup.PopupEntity(Loc.GetString(skills.CoveringTracks ? "cf-skill-cover-tracks-on" : "cf-skill-cover-tracks-off"), user, user);
    }

    #endregion

    #region Treat Poison and Treat Disease

    private bool CanTreat(EntityUid user)
    {
        if (!NeedsTrained(user, "Medicine"))
            return false;

        foreach (var held in _hands.EnumerateHeld(user))
        {
            if (HasComp<CFHealersToolkitComponent>(held))
                return true;
        }

        _popup.PopupEntity(Loc.GetString("cf-skill-treat-no-toolkit"), user, user);
        return false;
    }

    /// <summary>One action, with a healer's toolkit in hand: Medicine against the poison's DC.</summary>
    public CFDegree? TryTreatPoison(EntityUid user, EntityUid patient)
    {
        if (!CanTreat(user) || !_actions.TrySpendActions(user, 1))
            return null;

        return Treat(user, patient, CFConditionKind.Poisoned);
    }

    private void StartTreatDisease(EntityUid user, EntityUid patient)
    {
        if (!CanTreat(user))
            return;

        EnsureComp<CFSkillActionsComponent>(user);
        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, user, WorkSeconds, new CFTreatDiseaseDoAfterEvent(), eventTarget: user, target: patient)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
        });
    }

    [SubscribeLocalEvent]
    private void OnTreatedDisease(Entity<CFSkillActionsComponent> ent, ref CFTreatDiseaseDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled || args.Target is not { } patient)
            return;

        args.Handled = true;
        Treat(ent, patient, CFConditionKind.Diseased);
    }

    /// <summary>
    /// The patient's next save against each poison, or each disease, gets +2 circumstance on a success, +4 on a
    /// critical success, and -2 on a critical failure. Robust Recovery makes the success +4.
    /// </summary>
    public CFDegree? Treat(EntityUid user, EntityUid patient, CFConditionKind marker)
    {
        if (_afflictions.HighestDc(patient, marker) is not { } dc)
            return null;

        var degree = _medicine.RollMedicine(user, dc);
        var bonus = degree switch
        {
            CFDegree.CriticalSuccess => 4,
            CFDegree.Success => Options(user).RobustRecovery ? 4 : 2,
            CFDegree.CriticalFailure => -2,
            _ => 0,
        };

        _afflictions.Treat(patient, marker, bonus);
        _popup.PopupEntity(Loc.GetString($"cf-skill-treat-{Key(degree)}"), patient, user);
        return degree;
    }

    #endregion

    #region Repair

    [SubscribeLocalEvent]
    private void OnShieldVerbs(Entity<CFShieldComponent> ent, ref GetVerbsEvent<InteractionVerb> args)
    {
        var user = args.User;
        if (!args.CanAccess || !args.CanInteract || !HasComp<CFCombatantComponent>(user) || ent.Comp.CurrentHp >= ent.Comp.MaxHp)
            return;

        var shield = ent.Owner;
        args.Verbs.Add(new InteractionVerb
        {
            Text = Loc.GetString("cf-skill-repair"),
            Act = () => StartRepair(user, shield),
        });
    }

    private void StartRepair(EntityUid user, EntityUid shield)
    {
        _doAfter.TryStartDoAfter(new DoAfterArgs(
            EntityManager,
            user,
            Options(user).QuickRepair ? QuickRepairSeconds : WorkSeconds,
            new CFRepairDoAfterEvent(),
            eventTarget: shield,
            target: shield)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
        });
    }

    [SubscribeLocalEvent]
    private void OnRepaired(Entity<CFShieldComponent> ent, ref CFRepairDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled)
            return;

        args.Handled = true;
        Repair(args.User, ent);
    }

    /// <summary>
    /// Crafting against the item's DC. A success restores 5 Hit Points and 5 more per rank of Crafting, a critical
    /// success twice that, and a critical failure deals 2d6 damage to the item, less its Hardness.
    /// </summary>
    public CFDegree Repair(EntityUid user, Entity<CFShieldComponent> shield)
    {
        var item = CompOrNull<CFItemComponent>(shield);
        var dc = item != null ? CFCraftingRules.Dc(item.Level, item.Rarity) : CFCraftingRules.LevelDc(0);
        var degree = _conditions.RollSkillAgainst(user, "Crafting", CFAbility.Intelligence, dc, out _);
        var ranks = (int) Rank(user, "Crafting") / 2;
        var change = degree switch
        {
            CFDegree.CriticalSuccess => 10 + 10 * ranks,
            CFDegree.Success => 5 + 5 * ranks,
            CFDegree.CriticalFailure => -Math.Max(0, _effects.Roll(2, 6, 0) - shield.Comp.Hardness),
            _ => 0,
        };

        if (change != 0)
        {
            shield.Comp.Hp = Math.Clamp(shield.Comp.CurrentHp + change, 0, shield.Comp.MaxHp);
            Dirty(shield);
        }

        _popup.PopupEntity(Loc.GetString($"cf-skill-repair-{Key(degree)}"), shield, user);
        return degree;
    }

    #endregion
}
