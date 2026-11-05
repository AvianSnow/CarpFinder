using Content.Server.Atmos.EntitySystems;
using Content.Server.Weather;
using Content.Shared.Atmos.Components;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Farming;
using Content.Shared.CF.Outdoors;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Light.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.StatusEffectNew.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server.CF.Outdoors;

/// <summary>Runs an outdoor map: its light cycle, its changing weather, and what the climate does to creatures.</summary>
public sealed partial class CFOutdoorsWorldSystem : EntitySystem
{
    /// <summary>Daylight at noon before the cycle scales it. Night stays dark, not black.</summary>
    private static readonly Color Daylight = Color.FromSrgb(Color.FromHex("#C8C8C8"));

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private WeatherSystem _weather = default!;
    [Dependency] private FlammableSystem _flammable = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private CFArmorSystem _armor = default!;
    [Dependency] private CFEffectSystem _effects = default!;
    [Dependency] private CFOutdoorsSystem _outdoors = default!;

    [SubscribeLocalEvent]
    private void OnMapInit(Entity<CFOutdoorsComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.NextWeather = _timing.CurTime;
        ent.Comp.NextExposure = _timing.CurTime + Interval(ent.Comp.Temperature);

        if (HasComp<LightCycleComponent>(ent) || _outdoors.Clock() is not { } clock)
            return;

        var light = EnsureComp<MapLightComponent>(ent);
        light.AmbientLightColor = Daylight;
        Dirty(ent, light);

        // The stock curve is dark at 0 and brightest halfway. Line that noon up with the middle of the clock's day.
        var day = clock.DayRealHours;
        var cycle = day + clock.NightRealHours;
        var lightCycle = Factory.GetComponent<LightCycleComponent>();
        lightCycle.Duration = TimeSpan.FromHours(cycle);
        lightCycle.Offset = TimeSpan.FromHours((cycle - day) / 2);
        lightCycle.InitialOffset = false;
        AddComp(ent, lightCycle);
    }

    /// <summary>Wall-clock time between exposure ticks, converted from the book's world minutes.</summary>
    private TimeSpan Interval(CFTemperatureBand band)
    {
        var clock = _outdoors.Clock();
        if (CFEnvironmentRules.Exposure(band) is not { } exposure
            || clock == null
            || !clock.TryGetRealSeconds(exposure.WorldMinutes * 60f, Shared.CF.Time.CFTimeScale.CarpFinder, out var seconds))
        {
            return TimeSpan.FromMinutes(1);
        }

        return TimeSpan.FromSeconds(seconds);
    }

    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;

        var maps = EntityQueryEnumerator<CFOutdoorsComponent, MapComponent>();
        while (maps.MoveNext(out var uid, out var outdoors, out var map))
        {
            if (outdoors.Weather.Count > 0 && now >= outdoors.NextWeather)
                PickWeather(outdoors, map.MapId);

            if (now < outdoors.NextExposure)
                continue;

            outdoors.NextExposure = now + Interval(outdoors.Temperature);
            Expose((uid, outdoors));
        }

        var weather = EntityQueryEnumerator<CFWeatherEffectsComponent, StatusEffectComponent>();
        while (weather.MoveNext(out var effects, out var status))
        {
            if (now < effects.NextEffect || status.AppliedTo is not { } target)
                continue;

            effects.NextEffect = now + effects.Frequency;
            ApplyWeather(effects, target);
        }
    }

    private void PickWeather(CFOutdoorsComponent outdoors, MapId map)
    {
        var total = 0f;
        foreach (var entry in outdoors.Weather)
        {
            total += entry.Weight;
        }

        var roll = _random.NextFloat(total);
        foreach (var entry in outdoors.Weather)
        {
            roll -= entry.Weight;
            if (roll > 0f)
                continue;

            var length = TimeSpan.FromMinutes(_random.NextFloat(entry.MinMinutes, entry.MaxMinutes));
            outdoors.NextWeather = _timing.CurTime + length;
            if (entry.Weather is { } weather)
                _weather.TryAddWeather(map, weather, out _, length);

            return;
        }
    }

    /// <summary>One tick of the climate for every living creature out in it.</summary>
    public void Expose(Entity<CFOutdoorsComponent> map)
    {
        var band = map.Comp.Temperature;
        if (CFEnvironmentRules.Exposure(band) is not { } exposure)
            return;

        map.Comp.ExposureTicks++;

        var creatures = EntityQueryEnumerator<CFCombatantComponent, MobStateComponent, TransformComponent>();
        while (creatures.MoveNext(out var uid, out _, out _, out var xform))
        {
            if (xform.MapUid != map.Owner || !_outdoors.IsExposed(uid))
                continue;

            if (CFEnvironmentRules.Exposure(Felt(uid, band)) is not { } felt)
                continue;

            // Gear can slow the damage down: severe hurts every sixth tick of an extreme map's ten minutes.
            var everyNth = Math.Max(1, (int) (felt.WorldMinutes / exposure.WorldMinutes));
            if (map.Comp.ExposureTicks % everyNth != 0)
                continue;

            var damage = 0;
            for (var i = 0; i < felt.Dice; i++)
            {
                damage += _random.Next(1, 7);
            }

            _effects.DealDamage(uid, uid, damage, felt.DamageType, false);
        }
    }

    private CFTemperatureBand Felt(EntityUid uid, CFTemperatureBand band)
    {
        var cold = false;
        var heat = false;
        var slots = _inventory.GetSlotEnumerator(uid);
        while (slots.MoveNext(out var slot))
        {
            if (!TryComp(slot.ContainedEntity, out CFTemperatureGearComponent? gear))
                continue;

            cold |= gear.Kind == CFTemperatureGearKind.Cold;
            heat |= gear.Kind == CFTemperatureGearKind.Heat;
        }

        return CFEnvironmentRules.Felt(band, cold, heat, _armor.TryGetWornArmor(uid, out _));
    }

    private void ApplyWeather(CFWeatherEffectsComponent effects, EntityUid map)
    {
        if (effects.WaterChance > 0f)
        {
            var plants = EntityQueryEnumerator<CFPlantComponent, TransformComponent>();
            while (plants.MoveNext(out var uid, out var plant, out var xform))
            {
                if (xform.MapUid != map || !_random.Prob(effects.WaterChance) || !_outdoors.IsExposed(uid))
                    continue;

                if (_solutions.TryGetSolution(uid, plant.Solution, out var solution))
                    _solutions.TryAddReagent(solution.Value, CFPlantComponent.Water, effects.WaterAmount, out _);
            }
        }

        if (effects.ExtinguishChance <= 0f)
            return;

        var fires = EntityQueryEnumerator<FlammableComponent, TransformComponent>();
        while (fires.MoveNext(out var uid, out var flammable, out var xform))
        {
            if (flammable.OnFire
                && xform.MapUid == map
                && _random.Prob(effects.ExtinguishChance)
                && _outdoors.IsExposed(uid))
            {
                _flammable.Extinguish(uid, flammable);
            }
        }
    }
}
