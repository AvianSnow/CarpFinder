using Content.Server.Chat.Managers;
using Content.Shared.CF.Character;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Effects.Areas;
using Content.Shared.CF.Scene;
using Content.Shared.Chat;
using Content.Shared.DeviceLinking.Systems;
using Content.Shared.IdentityManagement;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Server.Player;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server.CF.Scene;

/// <summary>DM tools placed on a map: scene narration, and skill checks players start themselves.</summary>
public sealed partial class CFSceneSystem : EntitySystem
{
    private const string PerceptionName = "Perception";

    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(0.5);
    private static readonly VerbCategory RollCategory = new("cf-skillcheck-roll", null);
    private static readonly Color NarrationColor = Color.FromHex("#C9B6E8");

    [Dependency] private IChatManager _chat = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private DeviceLinkSystem _links = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private CFBonusSystem _bonuses = default!;
    [Dependency] private CFConditionSystem _conditions = default!;

    /// <summary>Gives the component its signals, even when it was added to some other object in VV.</summary>
    [SubscribeLocalEvent]
    private void OnNarateInit(Entity<CFNarateComponent> ent, ref MapInitEvent args)
    {
        _links.EnsureSourcePorts(ent, CFNarateComponent.Port);
    }

    [SubscribeLocalEvent]
    private void OnSkillCheckInit(Entity<CFSkillCheckComponent> ent, ref MapInitEvent args)
    {
        _links.EnsureSourcePorts(
            ent,
            CFSkillCheckComponent.CriticalSuccessPort,
            CFSkillCheckComponent.SuccessPort,
            CFSkillCheckComponent.FailurePort,
            CFSkillCheckComponent.CriticalFailurePort);
    }

    /// <summary>Polled instead of a sensor fixture, so a radius edited in VV works at once and nothing extra is saved.</summary>
    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;
        var triggers = EntityQueryEnumerator<CFNarateComponent, TransformComponent>();
        while (triggers.MoveNext(out var uid, out var narate, out var xform))
        {
            if (now < narate.NextCheck)
                continue;

            narate.NextCheck = now + CheckInterval;
            var range = narate.RadiusFeet / CFAreaGrid.FeetPerTile;
            var position = _transform.GetMapCoordinates(uid, xform);

            foreach (var session in _players.Sessions)
            {
                if (session.AttachedEntity is not { } player
                    || narate.Heard.Contains(session.UserId)
                    || !IsLivingCharacter(player))
                {
                    continue;
                }

                var theirs = _transform.GetMapCoordinates(player);
                if (theirs.MapId == position.MapId && (theirs.Position - position.Position).Length() <= range)
                    Narrate((uid, narate), session, player);
            }
        }
    }

    /// <summary>A body with a player in it: not a ghost, and not dead.</summary>
    private bool IsLivingCharacter(EntityUid uid)
    {
        return TryComp(uid, out MobStateComponent? mob) && !_mobState.IsDead(uid, mob);
    }

    /// <summary>Tells one player the text, once, and sends the signal.</summary>
    public bool Narrate(Entity<CFNarateComponent> ent, ICommonSession session, EntityUid player)
    {
        if (!ent.Comp.Heard.Add(session.UserId))
            return false;

        Tell(session, player, ent.Comp.Text);
        _links.InvokePort(ent.Owner, CFNarateComponent.Port);

        var narated = new CFNaratedEvent(player);
        RaiseLocalEvent(ent, ref narated);
        return true;
    }

    /// <summary>Chat keeps a record of it, and the popup gets the player's attention.</summary>
    private void Tell(ICommonSession session, EntityUid player, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        var escaped = FormattedMessage.EscapeText(text);
        var wrapped = Loc.GetString("cf-narrate-wrap", ("text", escaped));
        _chat.ChatMessageToOne(ChatChannel.Emotes, text, wrapped, player, false, session.Channel, NarrationColor);
        _popup.PopupEntity(text, player, player, PopupType.Medium);
    }

    /// <summary>The names in <see cref="CFSkillCheckComponent.Skills"/> that are real skills, in the sheet's spelling.</summary>
    public List<string> SkillsOf(CFSkillCheckComponent check)
    {
        var skills = new List<string>();
        foreach (var part in check.Skills.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part.Equals(PerceptionName, StringComparison.OrdinalIgnoreCase))
            {
                skills.Add(PerceptionName);
                continue;
            }

            foreach (var skill in _prototypes.EnumeratePrototypes<CFAbilitySkillPrototype>())
            {
                if (skill.ID.Equals(part, StringComparison.OrdinalIgnoreCase))
                    skills.Add(skill.ID);
            }
        }

        return skills;
    }

    [SubscribeLocalEvent]
    private void OnGetVerbs(Entity<CFSkillCheckComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        if (!args.CanAccess || !args.CanInteract || !TryComp(args.User, out ActorComponent? actor))
            return;

        if (ent.Comp.OncePerPlayer && ent.Comp.Rolled.Contains(actor.PlayerSession.UserId))
            return;

        var user = args.User;
        var session = actor.PlayerSession;
        foreach (var skill in SkillsOf(ent.Comp))
        {
            var picked = skill;
            args.Verbs.Add(new Verb
            {
                Text = picked,
                Category = RollCategory,
                Act = () => Roll(ent, user, session, picked),
            });
        }
    }

    /// <summary>Rolls the check. Returns the degree, or null if this player has used their attempt.</summary>
    public CFDegree? Roll(Entity<CFSkillCheckComponent> ent, EntityUid user, ICommonSession session, string skill)
    {
        if (ent.Comp.OncePerPlayer && !ent.Comp.Rolled.Add(session.UserId))
            return null;

        var degree = RollSkill(user, skill, ent.Comp.Dc, out var total);
        _popup.PopupEntity(
            Loc.GetString(
                degree.IsSuccess() ? "cf-skillcheck-roll-success" : "cf-skillcheck-roll-failure",
                ("name", Identity.Entity(user, EntityManager)),
                ("skill", skill),
                ("total", total)),
            user,
            degree.IsSuccess() ? PopupType.Medium : PopupType.MediumCaution);

        var (text, port) = degree switch
        {
            CFDegree.CriticalSuccess => (ent.Comp.CriticalSuccess, CFSkillCheckComponent.CriticalSuccessPort),
            CFDegree.Success => (ent.Comp.Success, CFSkillCheckComponent.SuccessPort),
            CFDegree.Failure => (ent.Comp.Failure, CFSkillCheckComponent.FailurePort),
            _ => (ent.Comp.CriticalFailure, CFSkillCheckComponent.CriticalFailurePort),
        };

        Tell(session, user, text);
        _links.InvokePort(ent.Owner, port);

        var rolled = new CFSkillCheckRolledEvent(user, skill, degree, total);
        RaiseLocalEvent(ent, ref rolled);
        return degree;
    }

    private CFDegree RollSkill(EntityUid user, string skill, int dc, out int total)
    {
        if (skill == PerceptionName)
        {
            var modifier = CFDefense.PerceptionModifier(
                _prototypes,
                CompOrNull<CFCombatantComponent>(user),
                CompOrNull<CFAbilityScoreComponent>(user),
                _bonuses.Total(user, CFBonusStat.Perception),
                _conditions.Frightened(user));
            var d20 = _random.Next(1, 21);
            total = d20 + modifier;
            return CFDegrees.DegreeOfSuccess(d20, modifier, dc);
        }

        var ability = _prototypes.Index<CFAbilitySkillPrototype>(skill).Ability;
        return _conditions.RollSkillAgainst(user, skill, ability, dc, out total);
    }
}
