using System.Numerics;
using Content.Server.CF.GOAP.Encounters;
using Content.Server.CF.GOAP.Steering;
using Content.Server.CF.Temperature;
using Content.Server.Chat.Systems;
using Content.Shared.Atmos.Components;
using Content.Shared.Body;
using Content.Shared.Body.Components;
using Content.Shared.Botany.Components;
using Content.Shared.Botany.Items.Components;
using Content.Shared.CF.Alchemy;
using Content.Shared.CF.Cooking;
using Content.Shared.CF.Shop;
using Content.Shared.CF.Temperature;
using Content.Shared.Buckle.Components;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Factions;
using Content.Shared.CF.GOAP;
using Content.Shared.CF.Harvest;
using Content.Shared.CF.Hazards;
using Content.Shared.CF.Languages;
using Content.Shared.Chat;
using Content.Shared.Chemistry.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Humanoid;
using Content.Shared.Interaction.Events;
using Content.Shared.Inventory;
using Content.Shared.Item;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Nutrition.Prototypes;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Tag;
using Content.Shared.Temperature.Components;
using Content.Shared.Tools;
using Content.Shared.Tools.Components;
using Content.Shared.Tools.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server.CF.GOAP.Society;

/// <summary>
/// A tribe: a group that lives round a nest, keeps stores and plots, and decides together how to treat a stranger.
/// This holds what the tribe knows. Sensors read it, the errand steps in this folder act on it, and
/// <see cref="CFSocietyAdvisorSystem"/> may ask a language model what to do, but every rule also works without one.
/// </summary>
public sealed partial class CFGOAPTribeSystem : EntitySystem
{
    /// <summary>How far a spoken line carries to tribemates, in tiles.</summary>
    public const float EarshotTiles = 10f;

    /// <summary>How long a claim on a plot or a piece of food lasts if nobody lets go of it.</summary>
    private static readonly TimeSpan ClaimTime = TimeSpan.FromSeconds(45);

    private static readonly TimeSpan SurveyInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan UpkeepInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan DemandInterval = TimeSpan.FromSeconds(9);

    /// <summary>How long an animal a fed tribe let be is left alone before the tribe decides about it again.</summary>
    private static readonly TimeSpan GameLookAgain = TimeSpan.FromSeconds(60);

    /// <summary>Appetite never takes a member below this share of full, so a tribe with no food is hungry, not dead.</summary>
    private const float AppetiteFloor = 0.12f;

    private static readonly string[] Names =
    {
        "Sniv", "Grik", "Meepo", "Drak", "Yip", "Tark", "Kib", "Zeb", "Nix", "Ruk", "Vex", "Jik",
    };

    private static readonly ProtoId<CFGOAPGoalPrototype>[] TribeGoals =
    {
        "Parley", "Drink", "Eat", "ShareFood", "StockFood", "TendCrops", "KeepNest", "Socialize",
    };

    private static readonly ProtoId<CFGOAPActionPrototype>[] TribeActions =
    {
        "Parley", "Drink", "Eat", "TakeStoredFood", "PickUpFood", "PrepareFood", "StoreFood", "GiveFood", "InspectSoil",
        "WaterSoil", "FeedSoil", "PlantSeed", "Harvest", "KeepNest", "Chat",
    };

    /// <summary>Tags of what a mortar grinds. The tribe knows wheat is for grinding before it finds a mortar.</summary>
    private static readonly ProtoId<TagPrototype>[] Grindable = { "Wheat", "CFWheat" };

    private static readonly ProtoId<TagPrototype> RawTag = "Raw";
    private static readonly ProtoId<TagPrototype> TrashTag = "Trash";
    private static readonly ProtoId<ToolQualityPrototype> Slicing = "Slicing";

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private IngestionSystem _ingestion = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SatiationSystem _satiation = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedStorageSystem _storage = default!;
    [Dependency] private SharedToolSystem _tools = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private TagSystem _tags = default!;
    [Dependency] private BodySystem _body = default!;
    [Dependency] private CFFactionSystem _factions = default!;
    [Dependency] private CFLanguageSystem _language = default!;
    [Dependency] private CFGOAPSteeringSystem _steering = default!;
    [Dependency] private CFSocietyAdvisorSystem _advisor = default!;

    /// <summary>The tribe a creature belongs to, if it is in one.</summary>
    public bool TryGetTribe(EntityUid uid, out Entity<CFGOAPTribeComponent> tribe)
    {
        tribe = default;
        if (!TryComp(uid, out CFGOAPEncounterMemberComponent? member)
            || !TryComp(member.Group, out CFGOAPTribeComponent? comp))
            return false;

        tribe = (member.Group, comp);
        return true;
    }

    /// <summary>The living members, eldest first.</summary>
    public List<EntityUid> Members(EntityUid tribe)
    {
        var members = new List<EntityUid>();
        if (!TryComp(tribe, out CFGOAPEncounterSpawnerComponent? group))
            return members;

        foreach (var member in group.Members)
        {
            if (!TerminatingOrDeleted(member) && _mobState.IsAlive(member))
                members.Add(member);
        }

        return members;
    }

    /// <summary>Makes a tribe of a group that has just spawned.</summary>
    [SubscribeLocalEvent]
    private void OnSpawned(Entity<CFGOAPEncounterSpawnerComponent> ent, ref CFEncounterSpawnedEvent args)
    {
        if (!args.Encounter.Tribe)
            return;

        var tribe = EnsureComp<CFGOAPTribeComponent>(ent);
        tribe.Strangers.Clear();
        tribe.Soil.Clear();
        tribe.Claims.Clear();
        tribe.Nest = FindNest((ent, tribe));
        Survey((ent, tribe));

        for (var i = 0; i < ent.Comp.Members.Count; i++)
        {
            Join((ent, tribe), ent.Comp.Members[i], i);
        }

        ElectLeader((ent, tribe));
    }

    /// <summary>The fire nearest where the tribe appeared, or that spot itself when there is none.</summary>
    private EntityCoordinates FindNest(Entity<CFGOAPTribeComponent> ent)
    {
        var here = _transform.GetMapCoordinates(ent);
        EntityUid? best = null;
        var nearest = ent.Comp.NestTiles;
        foreach (var fire in FiresNear(here, ent.Comp.NestTiles))
        {
            var distance = (_transform.GetMapCoordinates(fire).Position - here.Position).Length();
            if (distance >= nearest)
                continue;

            nearest = distance;
            best = fire;
        }

        return best is { } found ? Transform(found).Coordinates : Transform(ent).Coordinates;
    }

    private List<EntityUid> FiresNear(MapCoordinates here, float range)
    {
        var fires = new List<EntityUid>();
        foreach (var fire in _lookup.GetEntitiesInRange<CFFireplaceComponent>(here, range))
        {
            fires.Add(fire);
        }

        // Stock bonfires, which always burn.
        foreach (var fire in _lookup.GetEntitiesInRange<AlwaysHotComponent>(here, range))
        {
            fires.Add(fire);
        }

        return fires;
    }

    private void Join(Entity<CFGOAPTribeComponent> tribe, EntityUid mob, int index)
    {
        var social = EnsureComp<CFGOAPSocialComponent>(mob);
        social.Name = Names[index % Names.Length];
        social.Role = HasComp<CFTrapLayerComponent>(mob) ? "Trapper" : index % 2 == 0 ? "Warrior" : "Scout";
        social.NextChat = _timing.CurTime + TimeSpan.FromSeconds(_random.NextFloat(tribe.Comp.ChatMin));

        // A leader that keeps its own lists still lives as the tribe does.
        var goap = EnsureComp<CFGOAPComponent>(mob);
        foreach (var goal in TribeGoals)
        {
            if (!goap.Goals.Contains(goal))
                goap.Goals.Add(goal);
        }

        foreach (var action in TribeActions)
        {
            if (!goap.Actions.Contains(action))
                goap.Actions.Add(action);
        }

        goap.NeverSleeps = tribe.Comp.AlwaysAwake;
        goap.HomeRange = MathF.Max(goap.HomeRange, tribe.Comp.NestTiles);

        // Home is a place of its own beside the fire, so the tribe gathers round it without standing in it.
        if (tribe.Comp.Nest is not { } nest)
            return;

        var angle = MathF.Tau * index / Math.Max(1, Names.Length / 2);
        var spot = nest.Offset(new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * 2f);
        if (_steering.IsWalkable(mob, _transform.ToMapCoordinates(spot)))
            goap.Home = spot;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<CFGOAPTribeComponent>();
        while (query.MoveNext(out var uid, out var tribe))
        {
            if (now >= tribe.NextSurvey)
                Survey((uid, tribe));

            if (now < tribe.NextUpkeep)
                continue;

            tribe.NextUpkeep = now + UpkeepInterval;
            Upkeep((uid, tribe), (float) UpkeepInterval.TotalSeconds);
        }
    }

    /// <summary>
    /// Finds what the tribe has round its nest. This is what any member knows from living there, so it is not looked
    /// for by sight. What state a plot is in is another matter: that is only known by going to look.
    /// </summary>
    public void Survey(Entity<CFGOAPTribeComponent> ent)
    {
        var tribe = ent.Comp;
        tribe.NextSurvey = _timing.CurTime + SurveyInterval;
        // What members carry in their packs is remembered across surveys; only the shared stores are counted again.
        foreach (var store in tribe.Stores)
        {
            tribe.Stock.Remove(store);
        }

        tribe.Stores.Clear();
        tribe.Plots.Clear();
        tribe.Waters.Clear();
        tribe.Fires.Clear();
        tribe.Mortars.Clear();
        tribe.Knives.Clear();
        tribe.Clippers.Clear();
        tribe.Butchers.Clear();
        tribe.FoodStock = 0;
        if (tribe.Nest is not { } nest || !Exists(nest.EntityId))
            return;

        var here = _transform.ToMapCoordinates(nest);
        tribe.Fires.AddRange(FiresNear(here, tribe.NestTiles));

        foreach (var plot in _lookup.GetEntitiesInRange<PlantTrayComponent>(here, tribe.NestTiles))
        {
            tribe.Plots.Add(plot);
        }

        foreach (var store in _lookup.GetEntitiesInRange<StorageComponent>(here, tribe.NestTiles))
        {
            // A crate or a chest: not a bag, not a body, and not the fire's own fuel box.
            if (HasComp<ItemComponent>(store) || HasComp<MobStateComponent>(store) || HasComp<CFFireplaceComponent>(store))
                continue;

            tribe.Stores.Add(store);
            tribe.FoodStock += LookInto(tribe, store).Food;
        }

        // Its tools, lying about the nest. One in somebody's hand is theirs for now.
        foreach (var mortar in _lookup.GetEntitiesInRange<CFMortarComponent>(here, tribe.NestTiles))
        {
            if (!_containers.IsEntityInContainer(mortar))
                tribe.Mortars.Add(mortar);
        }

        foreach (var clippers in _lookup.GetEntitiesInRange<BotanySampleTakerComponent>(here, tribe.NestTiles))
        {
            if (!_containers.IsEntityInContainer(clippers))
                tribe.Clippers.Add(clippers);
        }

        foreach (var knife in _lookup.GetEntitiesInRange<CFHarvestToolComponent>(here, tribe.NestTiles))
        {
            if (!_containers.IsEntityInContainer(knife))
                tribe.Butchers.Add(knife);
        }

        foreach (var tool in _lookup.GetEntitiesInRange<ToolComponent>(here, tribe.NestTiles))
        {
            if (HasComp<ItemComponent>(tool) && !_containers.IsEntityInContainer(tool) && _tools.HasQuality(tool, Slicing.Id, tool.Comp))
                tribe.Knives.Add(tool);
        }

        foreach (var water in _lookup.GetEntitiesInRange<DrainableSolutionComponent>(here, tribe.NestTiles))
        {
            if (!HasComp<ItemComponent>(water) && IsDrink(water))
                tribe.Waters.Add(water);
        }
    }

    private void Upkeep(Entity<CFGOAPTribeComponent> ent, float seconds)
    {
        var now = _timing.CurTime;
        var tribe = ent.Comp;
        var members = Members(ent);

        if (tribe.Leader is not { } leader || !members.Contains(leader))
            ElectLeader(ent);

        foreach (var member in members)
        {
            Hunger(member, tribe, seconds);
        }

        var gone = new List<EntityUid>();
        foreach (var (stranger, record) in tribe.Strangers)
        {
            if (TerminatingOrDeleted(stranger) || !_mobState.IsAlive(stranger))
            {
                gone.Add(stranger);
                continue;
            }

            if (record.Stance == CFTribeStance.Peace && now >= record.PeaceUntil)
            {
                gone.Add(stranger);
                continue;
            }

            if (record.Stance != CFTribeStance.Parley || now < record.ParleyUntil)
                continue;

            record.Stance = CFTribeStance.Hostile;
            record.Settled = true;
            if (tribe.Leader is { } speaker)
                Say(speaker, Loc.GetString("cf-tribe-line-attack"));
        }

        foreach (var stranger in gone)
        {
            tribe.Strangers.Remove(stranger);
        }

        _advisor.AskSomeone(ent, members);
    }

    /// <summary>The extra hunger and thirst a tribe's <see cref="CFGOAPTribeComponent.Appetite"/> adds to stock's.</summary>
    private void Hunger(EntityUid member, CFGOAPTribeComponent tribe, float seconds)
    {
        if (tribe.Appetite <= 1f || !TryComp(member, out SatiationComponent? satiation))
            return;

        Starve((member, satiation), SatiationSystem.Hunger, tribe.Appetite, seconds);
        Starve((member, satiation), SatiationSystem.Thirst, tribe.Appetite, seconds);
    }

    private void Starve(Entity<SatiationComponent> ent, ProtoId<SatiationTypePrototype> type, float appetite, float seconds)
    {
        if (ent.Comp.GetOrNull(type) is not { } need
            || need.ActualChangeRate >= 0f
            || Share(ent.AsNullable(), type) is not { } share
            || share <= AppetiteFloor)
            return;

        _satiation.ModifyValue(ent, type, need.ActualChangeRate * (appetite - 1f) * seconds);
    }

    /// <summary>How full it is, from 0 to 1, or null for a body that does not feel this need.</summary>
    public float? Share(Entity<SatiationComponent?> ent, ProtoId<SatiationTypePrototype> type)
    {
        if (!Resolve(ent, ref ent.Comp, false)
            || _satiation.GetValueOrNull((ent, ent.Comp), type) is not { } value
            || _satiation.GetMaximumValue((ent, ent.Comp), type) is not { } most
            || most <= 0)
            return null;

        return Math.Clamp(value / most, 0f, 1f);
    }

    /// <summary>The most skilled member alive leads, and among equals the eldest.</summary>
    private void ElectLeader(Entity<CFGOAPTribeComponent> ent)
    {
        EntityUid? best = null;
        var bestLevel = int.MinValue;
        foreach (var member in Members(ent))
        {
            var level = CompOrNull<CFCombatantComponent>(member)?.Level ?? 0;
            if (level <= bestLevel)
                continue;

            bestLevel = level;
            best = member;
        }

        if (best == ent.Comp.Leader)
            return;

        ent.Comp.Leader = best;
        if (best is not { } leader || !TryComp(leader, out CFGOAPSocialComponent? social))
            return;

        social.Role = "Chief";
        foreach (var member in Members(ent))
        {
            Remember(member, Loc.GetString("cf-tribe-memory-leader", ("name", social.Name)), leader);
        }
    }

    /// <summary>Something to eat, or something that becomes food with work: wheat, flour, dough, a loaf to slice.</summary>
    public bool IsFood(EntityUid uid)
    {
        return Stage(uid) != CFFoodStage.None;
    }

    /// <summary>Food that can be eaten as it is, by anyone with an ordinary stomach.</summary>
    public bool IsEdible(EntityUid uid)
    {
        return HasComp<ItemComponent>(uid)
               && TryComp(uid, out EdibleComponent? edible)
               && !edible.RequiresSpecialDigestion
               && _ingestion.GetEdibleType((uid, edible)) == IngestionSystem.Food;
    }

    /// <summary>Whether this creature can digest this food. Stock wheat wants a cow.</summary>
    public bool CanEat(EntityUid eater, EntityUid food)
    {
        return TryComp(food, out EdibleComponent? edible)
               && _ingestion.GetEdibleType((food, edible)) == IngestionSystem.Food
               && _body.TryGetOrgansWithComponent<StomachComponent>(eater, out var stomachs)
               && _ingestion.IsDigestibleBy(food, stomachs, out _);
    }

    /// <summary>
    /// How far along food is. Wheat is ground, flour is mixed into water, dough and raw meat are cooked, a loaf is
    /// sliced, and the rest is eaten. Whether the tribe can do the next step is <see cref="CFGOAPSocietySensorSystem.TryFindStation"/>.
    /// </summary>
    public CFFoodStage Stage(EntityUid uid)
    {
        if (!HasComp<ItemComponent>(uid))
            return CFFoodStage.None;

        if (HasComp<CFMixIntoComponent>(uid))
            return CFFoodStage.Mix;

        if (HasComp<TemperatureComponent>(uid)
            && (_tags.HasTag(uid, RawTag)
                || TryComp(uid, out CFTemperatureTransformationComponent? heat) && CFTemperatureTransformationSystem.IsCookable(heat)))
            return CFFoodStage.Cook;

        foreach (var tag in Grindable)
        {
            if (_tags.HasTag(uid, tag))
                return CFFoodStage.Grind;
        }

        if (!IsEdible(uid))
            return CFFoodStage.None;

        if (TryComp(uid, out ToolRefinableComponent? refinable) && refinable.QualityNeeded == Slicing)
            return CFFoodStage.Slice;

        return CFFoodStage.Ready;
    }

    /// <summary>A coin, a potion, or anything else the tribe keeps in its stores because it is worth something.</summary>
    public bool IsValuable(EntityUid uid)
    {
        return HasComp<ItemComponent>(uid)
               && !IsFood(uid)
               && (HasComp<CFCoinComponent>(uid)
                   || HasComp<CFAlchemicalItemComponent>(uid)
                   || TryComp(uid, out CFItemPriceComponent? price) && price.FullResale);
    }

    /// <summary>Rubbish, by the stock trash tag.</summary>
    public bool IsRubbish(EntityUid uid)
    {
        return HasComp<ItemComponent>(uid) && _tags.HasTag(uid, TrashTag);
    }

    /// <summary>Something the fire burns: planks, logs, coal.</summary>
    public bool IsFuel(EntityUid uid)
    {
        return HasComp<ItemComponent>(uid) && HasComp<CFFireplaceFuelComponent>(uid);
    }

    /// <summary>
    /// An animal the tribe hunts rather than talks to: alive, speaks no language, is nobody's character, and can be
    /// butchered, and is no humanoid. A pig, a chicken, a boar. Anything with a tongue or a face, however hostile, is a stranger.
    /// </summary>
    public bool IsGame(EntityUid uid)
    {
        return HasComp<MobStateComponent>(uid)
               && _mobState.IsAlive(uid)
               && HasComp<CFHarvestableComponent>(uid)
               && !HasComp<CFGOAPSocialComponent>(uid)
               && !HasComp<CFLanguagesComponent>(uid)
               && !HasComp<CFPvpComponent>(uid)
               && !HasComp<ActorComponent>(uid)
               && !HasComp<HumanoidProfileComponent>(uid);
    }

    /// <summary>
    /// The nearest store with room for this, as far as the tribe knows. With none, a member falls back on the pack
    /// on its own back, so what it carries does not end up on the ground.
    /// </summary>
    public bool TryFindStore(CFGOAPTribeComponent tribe, EntityUid item, MapCoordinates here, out EntityUid store, EntityUid? member = null)
    {
        store = default;
        var nearest = float.MaxValue;
        foreach (var candidate in tribe.Stores)
        {
            if (TerminatingOrDeleted(candidate) || !_storage.CanInsert(candidate, item, out _))
                continue;

            var distance = (_transform.GetMapCoordinates(candidate).Position - here.Position).Length();
            if (distance >= nearest)
                continue;

            nearest = distance;
            store = candidate;
        }

        if (nearest < float.MaxValue)
            return true;

        if (member is not { } who || !TryGetPack(who, out var pack) || !_storage.CanInsert(pack, item, out _))
            return false;

        store = pack;
        return true;
    }

    /// <summary>The pack on a member's back, if it wears one that holds things.</summary>
    public bool TryGetPack(EntityUid member, out EntityUid pack)
    {
        pack = default;
        if (!_inventory.TryGetSlotEntity(member, "back", out var worn) || !HasComp<StorageComponent>(worn.Value))
            return false;

        pack = worn.Value;
        return true;
    }

    /// <summary>
    /// Counts what a store holds, from its contents. The survey does this for every store, and a hungry member for a
    /// store nobody has looked into for a while, in case something was put there that the tribe did not see.
    /// </summary>
    public CFStoreRecord LookInto(CFGOAPTribeComponent tribe, EntityUid store)
    {
        var record = new CFStoreRecord { At = _timing.CurTime };
        if (tribe.Stock.TryGetValue(store, out var old) && tribe.Stores.Contains(store))
            tribe.FoodStock = Math.Max(0, tribe.FoodStock - old.Food);

        tribe.Stock[store] = record;
        if (!TryComp(store, out StorageComponent? storage))
            return record;

        foreach (var item in storage.Container.ContainedEntities)
        {
            Count(record, item, 1);
        }

        return record;
    }

    /// <summary>A member put this in a store, so the count of what is there follows without anyone looking.</summary>
    public void NoteStored(CFGOAPTribeComponent tribe, EntityUid store, EntityUid item)
    {
        Note(tribe, store, item, 1);
    }

    /// <summary>A member took this out of a store.</summary>
    public void NoteTaken(CFGOAPTribeComponent tribe, EntityUid store, EntityUid item)
    {
        Note(tribe, store, item, -1);
    }

    private void Note(CFGOAPTribeComponent tribe, EntityUid store, EntityUid item, int by)
    {
        if (!tribe.Stock.TryGetValue(store, out var record))
            tribe.Stock[store] = record = new CFStoreRecord();

        var food = record.Food;
        Count(record, item, by);

        // The tribe counts only what is in a shared store. A pack is one member and nobody else can reach it.
        if (tribe.Stores.Contains(store))
            tribe.FoodStock = Math.Max(0, tribe.FoodStock + record.Food - food);
    }

    private void Count(CFStoreRecord record, EntityUid item, int by)
    {
        // A loaf or raw meat can be eaten as it is, so it is food, however much better it would be sliced or cooked.
        var stage = Stage(item);
        if (IsEdible(item))
            record.Food = Math.Max(0, record.Food + by);
        else if (stage != CFFoodStage.None)
            record.Ingredients = Math.Max(0, record.Ingredients + by);
        // Fuel first: planks and logs are trade goods with a full resale price, and must still count as firewood.
        else if (IsFuel(item))
            record.Fuel = Math.Max(0, record.Fuel + by);
        else if (IsValuable(item))
            record.Valuables = Math.Max(0, record.Valuables + by);
    }

    public bool IsDrink(EntityUid uid)
    {
        return TryComp(uid, out EdibleComponent? edible)
               && _ingestion.GetEdibleType((uid, edible)) == IngestionSystem.Drink;
    }

    /// <summary>Food it has in a hand.</summary>
    public bool TryHeldFood(EntityUid uid, out EntityUid food)
    {
        foreach (var held in _hands.EnumerateHeld(uid))
        {
            if (!IsFood(held))
                continue;

            food = held;
            return true;
        }

        food = default;
        return false;
    }

    /// <summary>A fire the tribe could cook on now. One that has to be lit counts only while it burns.</summary>
    public bool TryGetFire(CFGOAPTribeComponent tribe, out EntityUid fire)
    {
        foreach (var candidate in tribe.Fires)
        {
            if (TerminatingOrDeleted(candidate)
                || TryComp(candidate, out FlammableComponent? flammable) && !flammable.OnFire)
                continue;

            fire = candidate;
            return true;
        }

        fire = default;
        return false;
    }

    // Claims: two members do not walk to the same plot or the same piece of bread.

    /// <summary>Whether nobody else is on their way to it.</summary>
    public bool IsFree(CFGOAPTribeComponent tribe, EntityUid target, EntityUid worker)
    {
        return !tribe.Claims.TryGetValue(target, out var claim)
               || claim.Worker == worker
               || _timing.CurTime >= claim.Until
               || TerminatingOrDeleted(claim.Worker)
               || !_mobState.IsAlive(claim.Worker);
    }

    public void Claim(CFGOAPTribeComponent tribe, EntityUid target, EntityUid worker)
    {
        tribe.Claims[target] = (worker, _timing.CurTime + ClaimTime);
    }

    public void Release(CFGOAPTribeComponent tribe, EntityUid target, EntityUid worker)
    {
        if (tribe.Claims.TryGetValue(target, out var claim) && claim.Worker == worker)
            tribe.Claims.Remove(target);
    }

    // Memory and speech.

    /// <summary>Adds to what it remembers, dropping the oldest. News it already has is not added twice.</summary>
    public void Remember(EntityUid uid, string text, EntityUid? from = null)
    {
        if (!TryComp(uid, out CFGOAPSocialComponent? social))
            return;

        foreach (var memory in social.Memory)
        {
            if (memory.Text == text)
                return;
        }

        social.Memory.Add(new CFTribeMemory(text, from));
        while (social.Memory.Count > CFGOAPSocialComponent.MemoryLimit)
        {
            social.Memory.RemoveAt(0);
        }
    }

    /// <summary>Every member in earshot of a place remembers it.</summary>
    public void Witness(Entity<CFGOAPTribeComponent> tribe, MapCoordinates where, string text, EntityUid? from = null)
    {
        foreach (var member in Members(tribe))
        {
            var at = _transform.GetMapCoordinates(member);
            if (member != from && at.MapId == where.MapId && (at.Position - where.Position).Length() <= EarshotTiles)
                Remember(member, text, from);
        }
    }

    /// <summary>
    /// Speaks a line aloud, in its own tongue, and tribemates in earshot remember it. <paramref name="news"/> is what
    /// they remember when that is not the line itself, as when it passes on something it saw.
    /// </summary>
    public void Say(EntityUid speaker, string line, string? news = null)
    {
        line = Clean(line);
        if (line.Length == 0)
            return;

        _chat.TrySendInGameICMessage(speaker, line, InGameICChatType.Speak, ChatTransmitRange.Normal, hideLog: true, ignoreActionBlocker: true);
        if (!TryGetTribe(speaker, out var tribe))
            return;

        var name = CompOrNull<CFGOAPSocialComponent>(speaker)?.Name ?? Name(speaker);
        var heard = news ?? Loc.GetString("cf-tribe-memory-heard", ("name", name), ("line", line));
        Witness(tribe, _transform.GetMapCoordinates(speaker), heard, speaker);
    }

    /// <summary>How far a whisper carries, in tiles: stock's clear whisper range.</summary>
    private const float WhisperTiles = 2f;

    /// <summary>
    /// A stranger speaking near the tribe. Every member in earshot remembers the words if it knows the language, or
    /// that it was spoken to in a tongue it does not know, and the leader asks the language model at once, so the
    /// tribe can answer. A tribemate's own lines go round through <see cref="Say"/>.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnSpoke(EntitySpokeEvent args)
    {
        if (TryGetTribe(args.Source, out _) || !HasComp<MobStateComponent>(args.Source))
            return;

        var where = _transform.GetMapCoordinates(args.Source);
        var reach = args.ObfuscatedMessage != null ? WhisperTiles : EarshotTiles;
        var query = EntityQueryEnumerator<CFGOAPTribeComponent>();
        while (query.MoveNext(out var uid, out var tribe))
        {
            Hear((uid, tribe), args.Source, args.Message, where, reach);
        }
    }

    /// <summary>What one tribe makes of a stranger's words.</summary>
    public void Hear(Entity<CFGOAPTribeComponent> tribe, EntityUid speaker, string words, MapCoordinates where, float reach)
    {
        var heard = false;
        var leaderHeard = false;
        foreach (var member in Members(tribe))
        {
            var at = _transform.GetMapCoordinates(member);
            if (at.MapId != where.MapId || (at.Position - where.Position).Length() > reach)
                continue;

            if (!heard)
            {
                // Hearing someone is meeting them.
                heard = true;
                Meet(tribe, member, speaker);
            }

            var label = tribe.Comp.Strangers[speaker].Label;
            var understood = !_language.TryGetSpoken(speaker, out var language) || _language.Understands(member, language.ID);
            var text = understood
                ? Loc.GetString("cf-tribe-memory-stranger-said", ("who", label), ("line", Clean(words)))
                : Loc.GetString("cf-tribe-memory-strange-tongue", ("who", label));
            Remember(member, text, speaker);
            if (TryComp(member, out CFGOAPSocialComponent? social))
            {
                social.Heard = text;
                social.HeardAt = _timing.CurTime;
            }

            leaderHeard |= member == tribe.Comp.Leader;
        }

        if (leaderHeard && tribe.Comp.Leader is { } leader)
            _advisor.Ask(tribe, leader, urgent: true);
    }

    /// <summary>A line from a language model is one short sentence of plain text, whatever it sent.</summary>
    public static string Clean(string line)
    {
        line = line.Replace('\n', ' ').Replace('\r', ' ').Replace("[", string.Empty).Replace("]", string.Empty).Trim();
        return line.Length > 120 ? line[..120] : line;
    }

    /// <summary>The line a language model gave it, if it is still fresh. Taking it uses it up.</summary>
    public string? TakeLine(CFGOAPSocialComponent social)
    {
        var line = social.Line;
        social.Line = null;
        return line != null && _timing.CurTime < social.LineUntil ? line : null;
    }

    // Strangers.

    /// <summary>What the tribe makes of a creature, or null if it has not met it.</summary>
    public CFTribeStance? Stance(CFGOAPTribeComponent tribe, EntityUid stranger)
    {
        return tribe.Strangers.TryGetValue(stranger, out var record) ? record.Stance : null;
    }

    /// <summary>Whether a creature's tribe has taken food from this one and is leaving it alone.</summary>
    public bool AtPeaceWith(EntityUid member, EntityUid other)
    {
        return TryGetTribe(member, out var tribe) && Stance(tribe.Comp, other) == CFTribeStance.Peace;
    }

    /// <summary>
    /// A member has seen a stranger the tribe has no opinion of yet. The built-in rules decide at once, so nothing
    /// waits on a language model: it is asked as well, and its answer may change the tribe's mind.
    /// </summary>
    public CFTribeStance Meet(Entity<CFGOAPTribeComponent> tribe, EntityUid member, EntityUid stranger)
    {
        if (tribe.Comp.Strangers.TryGetValue(stranger, out var known))
            return known.Stance;

        var now = _timing.CurTime;

        // An animal is nobody to talk to. A tribe that wants food hunts it; one that does not lets it be for a while.
        if (IsGame(stranger))
        {
            var wanted = Situation(tribe, stranger).Hungry;
            var game = new CFTribeStranger
            {
                Stance = wanted ? CFTribeStance.Hostile : CFTribeStance.Peace,
                PeaceUntil = now + GameLookAgain,
                Label = $"Beast_{++tribe.Comp.StrangersMet}",
            };
            tribe.Comp.Strangers[stranger] = game;
            if (wanted)
                Witness(tribe, _transform.GetMapCoordinates(stranger), Loc.GetString("cf-tribe-memory-game", ("who", Name(stranger))));

            return game.Stance;
        }

        var record = new CFTribeStranger
        {
            Stance = CFSocietyRules.Greet(Situation(tribe, stranger)),
            ParleyUntil = now + tribe.Comp.ParleyPatience,
            Label = $"Adventurer_{++tribe.Comp.StrangersMet}",
        };
        tribe.Comp.Strangers[stranger] = record;

        var where = _transform.GetMapCoordinates(stranger);
        Witness(tribe, where, Loc.GetString("cf-tribe-memory-stranger", ("who", Name(stranger))));
        if (tribe.Comp.Leader is { } leader)
            _advisor.Ask(tribe, leader, urgent: true);

        return record.Stance;
    }

    /// <summary>What the rules, and a language model, are told about a stranger.</summary>
    public CFSocietySituation Situation(Entity<CFGOAPTribeComponent> tribe, EntityUid stranger)
    {
        var members = Members(tribe);
        var hungry = tribe.Comp.FoodStock < tribe.Comp.LowStock;
        foreach (var member in members)
        {
            if (TryComp(member, out CFGOAPComponent? goap) && CFGOAPSystem.Knows(goap, CFGOAPFact.Hungry))
                hungry = true;
        }

        var strangers = 1;
        foreach (var (other, record) in tribe.Comp.Strangers)
        {
            if (other != stranger && record.Stance != CFTribeStance.Peace)
                strangers++;
        }

        var inNest = tribe.Comp.Nest is { } nest
                     && Exists(nest.EntityId)
                     && (_transform.ToMapCoordinates(nest).Position - _transform.GetMapCoordinates(stranger).Position).Length()
                     <= tribe.Comp.NestTiles / 2f;

        return new CFSocietySituation(IsArmed(stranger), inNest, hungry, strangers >= members.Count);
    }

    public bool IsArmed(EntityUid uid)
    {
        foreach (var held in _hands.EnumerateHeld(uid))
        {
            if (HasComp<CFWeaponComponent>(held))
                return true;
        }

        return false;
    }

    /// <summary>The leader changes its mind about a stranger, unless that is already settled.</summary>
    public bool TrySetStance(CFGOAPTribeComponent tribe, EntityUid stranger, CFTribeStance stance)
    {
        if (!tribe.Strangers.TryGetValue(stranger, out var record)
            || record.Settled
            || record.Stance == CFTribeStance.Peace
            || stance == CFTribeStance.Peace)
            return false;

        if (record.Stance == CFTribeStance.Hostile && stance == CFTribeStance.Parley)
            record.ParleyUntil = _timing.CurTime + tribe.ParleyPatience;

        record.Stance = stance;
        return true;
    }

    /// <summary>Makes the tribe's demand, when it is next due.</summary>
    public void Demand(Entity<CFGOAPTribeComponent> tribe, EntityUid speaker, EntityUid stranger)
    {
        // The leader speaks for the tribe. With the leader somewhere else, whoever is there does.
        var now = _timing.CurTime;
        var leaderIsHere = TryComp(tribe.Comp.Leader, out CFGOAPComponent? leading)
                           && CFGOAPSystem.Knows(leading, CFGOAPFact.Parleying);
        if (speaker != tribe.Comp.Leader && leaderIsHere
            || !tribe.Comp.Strangers.TryGetValue(stranger, out var record)
            || now < record.NextDemand
            || !TryComp(speaker, out CFGOAPSocialComponent? social))
            return;

        record.NextDemand = now + DemandInterval;
        Say(speaker, TakeLine(social) ?? Loc.GetString("cf-tribe-line-demand"));
    }

    /// <summary>Blood settles it: whoever hurts a member is the tribe's enemy, whatever was said before.</summary>
    [SubscribeLocalEvent]
    private void OnDamaged(Entity<CFGOAPSocialComponent> ent, ref CFDamagedEvent args)
    {
        var source = args.Source;
        if (source == ent.Owner
            || !HasComp<MobStateComponent>(source)
            || HasComp<CFGOAPSocialComponent>(source)
            || !TryGetTribe(ent, out var tribe))
            return;

        if (!tribe.Comp.Strangers.TryGetValue(source, out var record))
        {
            record = new CFTribeStranger();
            tribe.Comp.Strangers[source] = record;
        }

        if (record.Stance == CFTribeStance.Hostile && record.Settled)
            return;

        record.Stance = CFTribeStance.Hostile;
        record.Settled = true;
        Witness(
            tribe,
            _transform.GetMapCoordinates(ent),
            Loc.GetString("cf-tribe-memory-attacked", ("who", Name(source)), ("name", ent.Comp.Name)));
    }

    /// <summary>
    /// Food put down in front of the tribe is tribute. Whoever dropped it is left alone for a while, even one the
    /// tribe was already fighting.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnFoodDropped(Entity<EdibleComponent> ent, ref DroppedEvent args)
    {
        var giver = args.User;
        if (HasComp<CFGOAPSocialComponent>(giver) || !HasComp<MobStateComponent>(giver) || !IsFood(ent))
            return;

        var where = _transform.GetMapCoordinates(giver);
        var query = EntityQueryEnumerator<CFGOAPTribeComponent>();
        while (query.MoveNext(out var uid, out var tribe))
        {
            if (!Watching((uid, tribe), giver, where))
                continue;

            if (!tribe.Strangers.TryGetValue(giver, out var record))
            {
                record = new CFTribeStranger();
                tribe.Strangers[giver] = record;
            }

            record.Stance = CFTribeStance.Peace;
            record.Settled = false;
            record.PeaceUntil = _timing.CurTime + tribe.PeaceTime;
            Witness((uid, tribe), where, Loc.GetString("cf-tribe-memory-tribute", ("who", Name(giver))));
            if (tribe.Leader is { } leader)
                Say(leader, Loc.GetString("cf-tribe-line-thanks"));

            // The fight is over for them at once, not at the next think.
            foreach (var member in Members(uid))
            {
                if (TryComp(member, out CFGOAPComponent? goap) && goap.Target == giver)
                {
                    goap.Target = null;
                    goap.Lkp = null;
                    goap.NextThink = TimeSpan.Zero;
                }
            }

            if (TryComp(uid, out CFGOAPEncounterSpawnerComponent? group) && group.Target == giver)
                group.Target = null;
        }
    }

    /// <summary>Whether the tribe has met them, or a member is near enough to see the gift.</summary>
    private bool Watching(Entity<CFGOAPTribeComponent> tribe, EntityUid giver, MapCoordinates where)
    {
        if (tribe.Comp.Strangers.ContainsKey(giver))
            return true;

        foreach (var member in Members(tribe))
        {
            var at = _transform.GetMapCoordinates(member);
            if (at.MapId == where.MapId
                && (at.Position - where.Position).Length() <= EarshotTiles
                && _factions.CanHarm(member, giver))
                return true;
        }

        return false;
    }
}

/// <summary>Raised on an encounter spawner once its group stands in the world.</summary>
[ByRefEvent]
public readonly record struct CFEncounterSpawnedEvent(CFGOAPEncounterPrototype Encounter);
