using System.Linq;
using System.Threading.Tasks;
using Content.Server.CF.Character;
using Content.Server.CF.Outdoors;
using Content.Server.Procedural;
using Content.Server.Stack;
using Content.Shared.CF.Adventure;
using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Hazards;
using Content.Shared.CF.Shop;
using Content.Shared.Gravity;
using Content.Shared.Interaction;
using Content.Shared.Maps;
using Content.Shared.Popups;
using Content.Shared.Stacks;
using Content.Shared.Teleportation.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server.CF.Adventure;

/// <summary>
/// Generates adventure sites: a map from a dungeon layout, filled with encounters built from GM Core's XP budget,
/// hazards, and treasure by encounter, all for the party that opened it.
/// </summary>
public sealed partial class CFAdventureSiteSystem : EntitySystem
{
    /// <summary>A layout with less walkable floor than this is not used.</summary>
    private const int MinimumTiles = 50;

    private const int LevelRange = 4;
    private const string PortalPrototype = "CFAdventurePortal";

    private static readonly Vector2i[] Neighbours =
    {
        new(1, 0), new(-1, 0), new(0, 1), new(0, -1),
        new(1, 1), new(1, -1), new(-1, 1), new(-1, -1),
    };

    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private DungeonSystem _dungeon = default!;
    [Dependency] private LinkedEntitySystem _links = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private StackSystem _stacks = default!;
    [Dependency] private CFExperienceSystem _experience = default!;
    [Dependency] private CFOutdoorsSystem _outdoors = default!;

    [SubscribeLocalEvent]
    private void OnInteractHand(Entity<CFAdventureRiftComponent> ent, ref InteractHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        _ = Open(ent, args.User);
    }

    /// <summary>Generates the rift's site for the user's company and replaces the rift with a portal pair.</summary>
    public async Task<bool> Open(Entity<CFAdventureRiftComponent> rift, EntityUid user)
    {
        if (rift.Comp.Opening)
            return false;

        rift.Comp.Opening = true;
        _popup.PopupEntity(Loc.GetString("cf-adventure-opening"), rift, user);

        var site = _prototypes.Index(rift.Comp.Site);
        var (level, size) = _experience.Party(user);
        var entry = await Generate(site, level, size, _random.Next());

        if (TerminatingOrDeleted(rift))
            return false;

        if (entry is not { } coordinates)
        {
            rift.Comp.Opening = false;
            _popup.PopupEntity(Loc.GetString("cf-adventure-failed"), rift, user);
            return false;
        }

        var near = SpawnAtPosition(PortalPrototype, Transform(rift).Coordinates);
        var far = SpawnAtPosition(PortalPrototype, coordinates);
        _links.TryLink(near, far);
        QueueDel(rift);
        return true;
    }

    /// <summary>Builds a site on a new map. Returns the entrance, or null if the layout came out too small.</summary>
    public async Task<EntityCoordinates?> Generate(CFAdventureSitePrototype site, int partyLevel, int partySize, int seed)
    {
        var map = _maps.CreateMap(out _);
        var grid = EnsureComp<MapGridComponent>(map);

        var gravity = EnsureComp<GravityComponent>(map);
        gravity.Enabled = true;
        Dirty(map, gravity);

        _outdoors.ApplySkyWorld(map);

        var light = EnsureComp<MapLightComponent>(map);
        light.AmbientLightColor = site.Light;
        Dirty(map, light);

        var dungeons = await _dungeon.GenerateDungeonAsync(_prototypes.Index(site.Layout), map, grid, Vector2i.Zero, seed);

        var floor = new HashSet<Vector2i>();
        foreach (var dungeon in dungeons)
        {
            floor.UnionWith(dungeon.AllTiles);
        }

        var walkable = LargestArea(floor);
        if (walkable.Count < MinimumTiles)
        {
            Log.Warning($"Adventure site {site.ID} came out with {walkable.Count} walkable tiles.");
            Del(map);
            return null;
        }

        WallIn(map, grid, site, floor);
        return Populate(site, (map, grid), walkable, partyLevel, partySize);
    }

    /// <summary>The biggest connected patch of floor. Noise layouts can leave islands nobody can reach.</summary>
    private static HashSet<Vector2i> LargestArea(HashSet<Vector2i> floor)
    {
        var best = new HashSet<Vector2i>();
        var seen = new HashSet<Vector2i>();
        foreach (var start in floor)
        {
            if (seen.Contains(start))
                continue;

            var area = new HashSet<Vector2i>();
            var open = new Queue<Vector2i>();
            open.Enqueue(start);
            seen.Add(start);
            while (open.TryDequeue(out var tile))
            {
                area.Add(tile);
                for (var i = 0; i < 4; i++)
                {
                    var next = tile + Neighbours[i];
                    if (floor.Contains(next) && seen.Add(next))
                        open.Enqueue(next);
                }
            }

            if (area.Count > best.Count)
                best = area;
        }

        return best;
    }

    /// <summary>Puts a floor and a wall on every empty tile that touches the floor, so nobody walks off the edge.</summary>
    private void WallIn(EntityUid map, MapGridComponent grid, CFAdventureSitePrototype site, HashSet<Vector2i> floor)
    {
        var edge = new HashSet<Vector2i>();
        foreach (var tile in floor)
        {
            foreach (var offset in Neighbours)
            {
                var next = tile + offset;
                if (!floor.Contains(next))
                    edge.Add(next);
            }
        }

        var ground = _maps.GetTileRef(map, grid, floor.First()).Tile;
        foreach (var tile in edge)
        {
            _maps.SetTile(map, grid, tile, ground);
            SpawnAtPosition(site.Wall, _maps.GridTileToLocal(map, grid, tile));
        }
    }

    /// <summary>Tiles in order of walking distance from <paramref name="start"/>.</summary>
    private static List<Vector2i> ByDistance(HashSet<Vector2i> walkable, Vector2i start)
    {
        var order = new List<Vector2i>();
        var seen = new HashSet<Vector2i> { start };
        var open = new Queue<Vector2i>();
        open.Enqueue(start);
        while (open.TryDequeue(out var tile))
        {
            order.Add(tile);
            for (var i = 0; i < 4; i++)
            {
                var next = tile + Neighbours[i];
                if (walkable.Contains(next) && seen.Add(next))
                    open.Enqueue(next);
            }
        }

        return order;
    }

    /// <summary>Places every encounter, hazard and treasure, and returns the entrance.</summary>
    public EntityCoordinates Populate(
        CFAdventureSitePrototype site,
        Entity<MapGridComponent> grid,
        HashSet<Vector2i> walkable,
        int partyLevel,
        int partySize)
    {
        var entrance = _random.Pick(walkable.ToList());
        var order = ByDistance(walkable, entrance);

        // Encounters spread out along the walk, so the last one on the list is the deepest.
        var count = site.Encounters.Count;
        for (var i = 0; i < count; i++)
        {
            var tile = order[order.Count * (i + 1) / (count + 1)];
            var coordinates = _maps.GridTileToLocal(grid, grid.Comp, tile);
            var threat = site.Encounters[i];

            foreach (var creature in PlanEncounter(site.Creatures, threat, partyLevel, partySize))
            {
                SpawnAtPosition(creature, coordinates);
            }

            SpawnTreasure(CFEncounterRules.TreasureCopper(partyLevel, threat), coordinates);
        }

        var hazards = HazardsFor(site, partyLevel);
        for (var i = 0; hazards.Count > 0 && i < site.HazardCount; i++)
        {
            var tile = order[_random.Next(order.Count / 4, order.Count)];
            SpawnAtPosition(_random.Pick(hazards), _maps.GridTileToLocal(grid, grid.Comp, tile));
        }

        return _maps.GridTileToLocal(grid, grid.Comp, entrance);
    }

    private List<EntProtoId> HazardsFor(CFAdventureSitePrototype site, int partyLevel)
    {
        var fitting = new List<EntProtoId>();
        foreach (var id in site.Hazards)
        {
            if (_prototypes.Index(id).TryComp(out CFHazardComponent? hazard, Factory)
                && Math.Abs(hazard.Level - partyLevel) <= LevelRange)
            {
                fitting.Add(id);
            }
        }

        return fitting;
    }

    /// <summary>
    /// Spends the threat's XP budget on creatures from the pool. It never overspends,
    /// and creatures more than 4 levels from the party are worth nothing and are left out.
    /// </summary>
    public List<EntProtoId> PlanEncounter(IReadOnlyList<EntProtoId> pool, CFThreat threat, int partyLevel, int partySize)
    {
        var candidates = new List<(EntProtoId Id, int Xp)>();
        foreach (var id in pool)
        {
            if (_prototypes.Index(id).TryComp(out CFCombatantComponent? combat, Factory)
                && CFExperienceAwards.TryAdversary(combat.Level - partyLevel, out var xp)
                && xp > 0)
            {
                candidates.Add((id, xp));
            }
        }

        var plan = new List<EntProtoId>();
        var budget = CFEncounterRules.Budget(threat, partySize);
        while (true)
        {
            var affordable = candidates.FindAll(c => c.Xp <= budget);
            if (affordable.Count == 0)
                return plan;

            var (id, xp) = _random.Pick(affordable);
            plan.Add(id);
            budget -= xp;
        }
    }

    private void SpawnTreasure(int copper, EntityCoordinates coordinates)
    {
        var platinum = copper / CFCoins.CopperPerPlatinum;
        var gold = copper % CFCoins.CopperPerPlatinum / CFCoins.CopperPerGold;

        if (platinum > 0)
            _stacks.SpawnMultipleAtPosition(new ProtoId<StackPrototype>("CFPlatinumCoin"), platinum, coordinates);

        if (gold > 0)
            _stacks.SpawnMultipleAtPosition(new ProtoId<StackPrototype>("CFGoldCoin"), gold, coordinates);
    }
}
