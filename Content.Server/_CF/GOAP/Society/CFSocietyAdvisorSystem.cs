using System.Collections.Concurrent;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Content.Shared.CF;
using Content.Shared.CF.GOAP;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Nutrition.EntitySystems;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server.CF.GOAP.Society;

/// <summary>
/// Asks a small language model, running beside the server, what a tribe member should do next. The model only picks
/// one of five goals, a creature and a line to say. GOAP still plans and does everything, and the tribe's own rules
/// are already in force before the answer comes, so a slow, wrong or missing answer costs nothing.
/// It asks on events (a stranger is met) and on a slow cooldown, never every tick, and one question at a time,
/// because the model runs on a CPU. See <c>.agent/society.md</c>.
/// </summary>
public sealed partial class CFSocietyAdvisorSystem : EntitySystem
{
    /// <summary>How long a goal the model picked is held before the creature's own priorities take over again.</summary>
    private static readonly TimeSpan ForceTime = TimeSpan.FromSeconds(20);

    private static readonly TimeSpan LineTime = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan WarnInterval = TimeSpan.FromMinutes(1);

    private static readonly ProtoId<CFGOAPGoalPrototype> ShareGoal = "ShareFood";
    private static readonly ProtoId<CFGOAPGoalPrototype> SocializeGoal = "Socialize";
    private static readonly ProtoId<CFGOAPGoalPrototype> StockGoal = "StockFood";
    private static readonly ProtoId<CFGOAPGoalPrototype> CropsGoal = "TendCrops";

    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private CFGOAPTribeSystem _tribes = default!;

    // The plumbing to the model, not game state: an answer that is lost with a restart was only advice.
    private readonly HttpClient _http = new();
    private readonly ConcurrentQueue<Reply> _replies = new();
    private bool _waiting;
    private TimeSpan _nextWarning;

    private sealed record Reply(EntityUid Member, Dictionary<string, EntityUid> Labels, string? Content, string? Error);

    public override void Shutdown()
    {
        base.Shutdown();
        _http.Dispose();
    }

    public bool Enabled => _config.GetCVar(CFCVars.SocietyLlm);

    /// <summary>Lets the member who has waited longest ask, if nobody is asking and nothing is going on.</summary>
    public void AskSomeone(Entity<CFGOAPTribeComponent> tribe, List<EntityUid> members)
    {
        if (!Enabled || _waiting)
            return;

        if (tribe.Comp.UrgentAsker is { } urgent)
        {
            tribe.Comp.UrgentAsker = null;
            if (members.Contains(urgent))
            {
                Ask(tribe, urgent, urgent: true);
                return;
            }
        }

        EntityUid? next = null;
        var longest = TimeSpan.MaxValue;
        foreach (var member in members)
        {
            if (!TryComp(member, out CFGOAPSocialComponent? social)
                || social.NextQuestion >= longest
                || !TryComp(member, out CFGOAPComponent? goap)
                || CFGOAPSystem.Knows(goap, CFGOAPFact.TargetVisible))
                continue;

            longest = social.NextQuestion;
            next = member;
        }

        if (next is { } asker)
            Ask(tribe, asker);
    }

    /// <summary>
    /// Sends one question. <paramref name="urgent"/> skips the creature's cooldown, for something that cannot wait,
    /// such as a stranger walking in. Behind a question already out it waits its turn, and asks next.
    /// </summary>
    public void Ask(Entity<CFGOAPTribeComponent> tribe, EntityUid member, bool urgent = false)
    {
        if (!Enabled || !TryComp(member, out CFGOAPSocialComponent? social))
            return;

        if (_waiting)
        {
            // It asks as soon as the question that is out comes back.
            if (urgent)
                tribe.Comp.UrgentAsker = member;

            return;
        }

        var now = _timing.CurTime;
        if (!urgent && now < social.NextQuestion)
            return;

        social.NextQuestion = now + TimeSpan.FromSeconds(_config.GetCVar(CFCVars.SocietyLlmCooldown));
        var state = Describe(tribe, (member, social), out var labels);
        tribe.Comp.Questions++;
        _waiting = true;
        _ = Send(
            _config.GetCVar(CFCVars.SocietyLlmUrl),
            CFSocietyPrompt.Request(state),
            _config.GetCVar(CFCVars.SocietyLlmTimeout),
            member,
            labels);
    }

    private async Task Send(string url, string body, float timeout, EntityUid member, Dictionary<string, EntityUid> labels)
    {
        string? content = null;
        string? error = null;
        try
        {
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(timeout));
            using var request = new StringContent(body, Encoding.UTF8, "application/json");
            using var response = await _http.PostAsync(url, request, cancel.Token).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync(cancel.Token).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
                content = CFSocietyPrompt.ReadContent(text);
            else
                error = $"{(int) response.StatusCode}: {text}";
        }
        catch (Exception e)
        {
            error = e.Message;
        }

        // Read on the game thread, in Update.
        _replies.Enqueue(new Reply(member, labels, content, error));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        while (_replies.TryDequeue(out var reply))
        {
            _waiting = false;
            if (reply.Error != null && _timing.RealTime >= _nextWarning)
            {
                _nextWarning = _timing.RealTime + WarnInterval;
                Log.Warning($"The society language model did not answer, so the tribe's own rules decide: {reply.Error}");
            }

            if (CFSocietyPrompt.TryParse(reply.Content, out var answer))
                Apply(reply.Member, answer, reply.Labels);
        }
    }

    /// <summary>What a member is told about its world. Each creature near it gets a short name the model can answer with.</summary>
    public CFSocietyState Describe(
        Entity<CFGOAPTribeComponent> tribe,
        Entity<CFGOAPSocialComponent> member,
        out Dictionary<string, EntityUid> labels)
    {
        labels = new Dictionary<string, EntityUid>();
        var state = new CFSocietyState
        {
            Name = member.Comp.Name,
            Role = member.Comp.Role,
            Hunger = Need(member, SatiationSystem.Hunger),
            Thirst = Need(member, SatiationSystem.Thirst),
            StockLow = tribe.Comp.FoodStock < tribe.Comp.LowStock,
        };

        foreach (var held in _hands.EnumerateHeld(member.Owner))
        {
            state.Inventory.Add(Name(held));
        }

        foreach (var memory in member.Comp.Memory)
        {
            state.Memory.Add(memory.Text);
        }

        var here = _transform.GetMapCoordinates(member);
        var count = 0;
        foreach (var (stranger, record) in tribe.Comp.Strangers)
        {
            var at = _transform.GetMapCoordinates(stranger);
            if (at.MapId != here.MapId || (at.Position - here.Position).Length() > CFGOAPTribeSystem.EarshotTiles * 1.5f)
                continue;

            var label = $"Adventurer_{++count}";
            labels[label] = stranger;
            var notes = _tribes.IsArmed(stranger) ? "Armed" : "Unarmed";
            if (_tribes.TryHeldFood(stranger, out var food))
                notes += $", Holding {Name(food)}";
            if (record.Stance == CFTribeStance.Peace)
                notes += ", GaveFood";
            else if (record.Settled)
                notes += ", AttackedUs";

            state.Nearby.Add(new CFSocietyNearby(label, notes));
        }

        foreach (var mate in _tribes.Members(tribe))
        {
            if (mate == member.Owner || !TryComp(mate, out CFGOAPSocialComponent? other))
                continue;

            var at = _transform.GetMapCoordinates(mate);
            if (at.MapId != here.MapId || (at.Position - here.Position).Length() > CFGOAPTribeSystem.EarshotTiles)
                continue;

            var label = $"Kobold_{other.Name}";
            labels[label] = mate;
            state.Nearby.Add(new CFSocietyNearby(label, $"Hunger: {Need(mate, SatiationSystem.Hunger)}/100"));
        }

        return state;
    }

    /// <summary>From 0, full, to 100, starving. A body with no such need is always at 0.</summary>
    private int Need(EntityUid uid, ProtoId<Content.Shared.Nutrition.Prototypes.SatiationTypePrototype> type)
    {
        return _tribes.Share(uid, type) is { } share ? (int) MathF.Round((1f - share) * 100f) : 0;
    }

    /// <summary>
    /// Turns an answer into something GOAP can act on. How a stranger is treated is the leader's call, so only the
    /// leader's answer changes that. Anything else forces one of the member's own goals for a while: if no plan
    /// reaches it, the planner simply moves on to the next.
    /// </summary>
    public void Apply(EntityUid member, CFSocietyAnswer answer, Dictionary<string, EntityUid> labels)
    {
        if (TerminatingOrDeleted(member)
            || !TryComp(member, out CFGOAPSocialComponent? social)
            || !TryComp(member, out CFGOAPComponent? goap)
            || !_tribes.TryGetTribe(member, out var tribe))
            return;

        tribe.Comp.Answers++;
        tribe.Comp.LastAnswer = $"{social.Name}: {answer.Goal} {answer.Target} \"{answer.Utterance}\"";

        var now = _timing.CurTime;
        EntityUid? target = labels.TryGetValue(answer.Target, out var found) && !TerminatingOrDeleted(found) ? found : null;
        var line = CFGOAPTribeSystem.Clean(answer.Utterance);

        switch (answer.Goal)
        {
            case CFSocietyGoal.DefendNest:
            case CFSocietyGoal.ParleyForFood:
                if (member != tribe.Comp.Leader || target is not { } stranger)
                    return;

                var stance = answer.Goal == CFSocietyGoal.DefendNest ? CFTribeStance.Hostile : CFTribeStance.Parley;
                if (!_tribes.TrySetStance(tribe.Comp, stranger, stance))
                    return;

                if (stance == CFTribeStance.Hostile)
                {
                    _tribes.Say(member, line);
                }
                else if (line.Length > 0)
                {
                    // The Parley step speaks it, as the tribe's demand.
                    social.Line = line;
                    social.LineUntil = now + LineTime;
                    tribe.Comp.Strangers[stranger].NextDemand = now;
                }

                Rethink(tribe);
                return;

            case CFSocietyGoal.ShareResource:
                social.Preferred = target;
                Keep(social, line, now);
                Force(goap, ShareGoal, now);
                return;

            case CFSocietyGoal.Socialize:
                social.Preferred = target;
                social.NextChat = now;
                Keep(social, line, now);
                Force(goap, SocializeGoal, now);
                return;

            case CFSocietyGoal.GatherFood:
                // Said at once, so the tribemates who hear it know what it is off to do.
                _tribes.Say(member, line);
                var hasFood = CFGOAPSystem.Knows(goap, CFGOAPFact.LooseFood) || CFGOAPSystem.Knows(goap, CFGOAPFact.HasFood);
                Force(goap, hasFood ? StockGoal : CropsGoal, now);
                return;
        }
    }

    private static void Keep(CFGOAPSocialComponent social, string line, TimeSpan now)
    {
        if (line.Length == 0)
            return;

        social.Line = line;
        social.LineUntil = now + LineTime;
    }

    private static void Force(CFGOAPComponent goap, ProtoId<CFGOAPGoalPrototype> goal, TimeSpan now)
    {
        goap.ForcedGoal = goal;
        goap.ForcedUntil = now + ForceTime;
        goap.NextThink = TimeSpan.Zero;
    }

    /// <summary>The whole tribe thinks again at once, so a change of mind about a stranger shows at once.</summary>
    private void Rethink(Entity<CFGOAPTribeComponent> tribe)
    {
        foreach (var member in _tribes.Members(tribe))
        {
            if (TryComp(member, out CFGOAPComponent? goap))
                goap.NextThink = TimeSpan.Zero;
        }
    }
}
