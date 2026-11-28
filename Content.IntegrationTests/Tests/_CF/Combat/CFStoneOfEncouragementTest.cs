using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Server.CF.Items;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.Actions.Events;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Items;
using Content.Shared.Chat;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.Analyzers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests.CF.Combat;

[TestFixture]
public sealed partial class CFStoneOfEncouragementTest : GameTest
{
    private sealed partial class SpeechListenerSystem : EntitySystem
    {
        public EntityUid Source;
        public string Message = string.Empty;
        public bool IsWhisper;

        public override void Initialize()
        {
            base.Initialize();
            SubscribeLocalEvent<EntitySpokeEvent>(OnSpoke);
        }

        private void OnSpoke(EntitySpokeEvent args)
        {
            if (args.Source != Source)
                return;

            Message = args.Message;
            IsWhisper = args.ObfuscatedMessage != null;
        }
    }

    private static readonly Box2i Footprint = new(-1, -1, 2, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFStoneOfEncouragementTestHero
  components:
  - type: CFCombatant
    level: 1
    maxHp: 50
    currentHp: 50
  - type: CFAbilityScore
  - type: CFCombatOptions
  - type: CFActionPool
    maxActions: 30
";

    [Test]
    public async Task HeldStoneActivatesAndWhispersEveryThirtySeconds()
    {
        var map = await CFDevMap.Load(Pair, Footprint);
        var hasActivation = false;
        var boostedStatCount = 0;
        var expectedBoostedStatCount = Enum.GetValues<CFBonusStat>().Length - 1;
        var speedBonus = 0;
        var durationMatches = false;
        var cooldownActive = false;
        var cooldownSeconds = 0d;
        var whisperMessage = string.Empty;
        var whisperObfuscated = false;
        var stonePaused = false;
        var whisperTimerAdvanced = false;

        await Server.WaitPost(() =>
        {
            SEntMan.System<SharedMapSystem>().SetPaused(map.MapId, false);
            var stone = SEntMan.SpawnEntity("stone_of_encouragment", map.Local(0, 0));
            stonePaused = SEntMan.IsPaused(stone);
            var timing = Server.ResolveDependency<IGameTiming>();
            var stoneComponent = SEntMan.GetComponent<CFStoneOfEncouragementComponent>(stone);
            Assert.That(stoneComponent.NextWhisper, Is.EqualTo(timing.CurTime + TimeSpan.FromSeconds(30)));

            var hero = SEntMan.SpawnEntity("CFStoneOfEncouragementTestHero", map.Local(0, 0));
            SEntMan.System<SharedHandsSystem>().TryPickupAnyHand(hero, stone, checkActionBlocker: false);

            var actions = SEntMan.System<SharedActionsSystem>().GetActions(hero)
                .Where(candidate =>
                    SEntMan.GetComponent<MetaDataComponent>(candidate).EntityPrototype?.ID == "CFActStoneOfEncouragement")
                .ToArray();
            hasActivation = actions.Length > 0;

            if (hasActivation)
            {
                var action = actions[0];
                var attempt = new ActionAttemptEvent(hero, null);
                SEntMan.EventBus.RaiseLocalEvent(action, ref attempt);
                if (!attempt.Cancelled)
                    SEntMan.System<SharedActionsSystem>().PerformAction(hero, action);

                var actionComponent = SEntMan.GetComponent<ActionComponent>(action);
                cooldownActive = SEntMan.System<SharedActionsSystem>().IsCooldownActive(actionComponent);
                cooldownSeconds = actionComponent.Cooldown?.End.Subtract(timing.CurTime).TotalSeconds ?? 0;
            }

            var bonuses = SEntMan.System<CFBonusSystem>();
            foreach (var stat in Enum.GetValues<CFBonusStat>())
            {
                if (stat == CFBonusStat.Speed)
                {
                    speedBonus = bonuses.Total(hero, stat);
                    continue;
                }

                if (bonuses.Total(hero, stat) == 1)
                    boostedStatCount++;
            }
            var activeBonuses = SEntMan.GetComponent<CFBonusesComponent>(hero).Bonuses;
            durationMatches = activeBonuses.Count == expectedBoostedStatCount
                              && activeBonuses.All(bonus => bonus.Expires == timing.CurTime + TimeSpan.FromSeconds(6));

            var listener = SEntMan.System<SpeechListenerSystem>();
            listener.Source = hero;
            stoneComponent.NextWhisper = timing.CurTime;
            SEntMan.System<CFStoneOfEncouragementSystem>().Update(0f);
            whisperTimerAdvanced = stoneComponent.NextWhisper > timing.CurTime;
            whisperMessage = listener.Message;
            whisperObfuscated = listener.IsWhisper;
        });

        Assert.Multiple(() =>
        {
            Assert.That(hasActivation, Is.True);
            Assert.That(boostedStatCount, Is.EqualTo(expectedBoostedStatCount));
            Assert.That(speedBonus, Is.Zero);
            Assert.That(durationMatches, Is.True);
            Assert.That(cooldownActive, Is.True);
            Assert.That(cooldownSeconds, Is.EqualTo(300).Within(0.1));
            Assert.That(stonePaused, Is.False);
            Assert.That(whisperTimerAdvanced, Is.True);
            Assert.That(whisperMessage, Is.EqualTo("Keep going. You can do this."));
            Assert.That(whisperObfuscated, Is.True);
        });
    }
}
