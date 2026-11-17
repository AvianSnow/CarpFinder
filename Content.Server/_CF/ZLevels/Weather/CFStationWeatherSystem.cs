// Ported from CrystallEdge's z-level system (https://github.com/crystallpunk-14/crystall-edge),
// with permission from eshhhed. CrystallEdge sublicenses it under the MIT License.
// CarpFinder changes: CF names and namespaces, and whatever each file's comments say.

using System.Linq;
using Content.Server.CF.ZLevels.Core.Components;
using Content.Shared.CF.ZLevels.Core.Components;
using Content.Shared.CF.ZLevels.Weather;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server.CF.ZLevels.Weather;

/// <summary>
/// Walks the weather graph of <see cref="CFStationWeatherComponent"/> and applies each node's weather to the station's z-network.
/// </summary>
public sealed partial class CFStationWeatherSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private CFWeatherSystem _weather = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<CFStationWeatherComponent, CFStationZLevelsComponent>();
        while (query.MoveNext(out var uid, out var weather, out var zLevels))
        {
            if (!weather.Enabled)
                continue;

            if (weather.CurrentNode is not null && _timing.CurTime < weather.NextChangeTime)
                continue;

            if (zLevels.ZNetworkEntity is not { } network || !TryComp<CFZMapNetworkComponent>(network, out var networkComp))
                continue;

            if (weather.Nodes.Count == 0)
                continue;

            var next = PickNextNode((uid, weather));
            var node = weather.Nodes[next];

            var duration = _random.Next(node.MinDuration, node.MaxDuration);
            weather.CurrentNode = next;
            weather.NextChangeTime = _timing.CurTime + duration;

            _weather.SetWeather((network, networkComp), node.Weather, duration);
        }
    }

    private string PickNextNode(Entity<CFStationWeatherComponent> ent)
    {
        if (ent.Comp.CurrentNode is not { } current || !ent.Comp.Nodes.TryGetValue(current, out var currentNode))
            return PickRoundStartNode(ent);

        var picked = PickWeighted(currentNode.Transitions, ent.Comp.Nodes);
        if (picked is not null)
            return picked;

        Log.Error($"Weather node '{current}' of {ToPrettyString(ent)} has no valid transitions, picking a random node.");
        return _random.Pick(ent.Comp.Nodes.Keys.ToList());
    }

    private string PickRoundStartNode(Entity<CFStationWeatherComponent> ent)
    {
        var candidates = new List<string>();
        foreach (var (key, node) in ent.Comp.Nodes)
        {
            if (node.RoundStart)
                candidates.Add(key);
        }

        if (candidates.Count > 0)
            return _random.Pick(candidates);

        Log.Error($"{ToPrettyString(ent)} has no round start weather nodes, picking a random node.");
        return _random.Pick(ent.Comp.Nodes.Keys.ToList());
    }

    private string? PickWeighted(Dictionary<string, float> transitions, Dictionary<string, CFWeatherNode> nodes)
    {
        var total = 0f;
        foreach (var (key, weight) in transitions)
        {
            if (weight > 0f && nodes.ContainsKey(key))
                total += weight;
        }

        if (total <= 0f)
            return null;

        var roll = _random.NextFloat() * total;
        foreach (var (key, weight) in transitions)
        {
            if (weight <= 0f || !nodes.ContainsKey(key))
                continue;

            roll -= weight;
            if (roll <= 0f)
                return key;
        }

        return null;
    }
}
