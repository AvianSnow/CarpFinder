using Content.Shared.CF.DamageTypes;

namespace Content.Shared.CF.Outdoors;

/// <summary>
/// GM Core's Temperature Effects table. Intervals are world minutes. Damage is the low end of the category:
/// minor is 1d6 and moderate is 4d6. The book has no save against it.
/// </summary>
public static class CFEnvironmentRules
{
    /// <summary>The damage a band deals, or null if it deals none.</summary>
    public static (float WorldMinutes, int Dice, string DamageType)? Exposure(CFTemperatureBand band)
    {
        return band switch
        {
            CFTemperatureBand.IncredibleCold => (1f, 4, "Cold"),
            CFTemperatureBand.ExtremeCold => (10f, 1, "Cold"),
            CFTemperatureBand.SevereCold => (60f, 1, "Cold"),
            CFTemperatureBand.SevereHeat => (60f, 1, "Fire"),
            CFTemperatureBand.ExtremeHeat => (10f, 1, "Fire"),
            CFTemperatureBand.IncredibleHeat => (1f, 4, "Fire"),
            _ => null,
        };
    }

    /// <summary>
    /// The band a creature feels. Cold-weather gear negates severe cold and turns extreme cold into severe.
    /// Desert gear negates severe heat, and armor cancels it.
    /// </summary>
    public static CFTemperatureBand Felt(CFTemperatureBand band, bool coldGear, bool heatGear, bool armored)
    {
        if (coldGear && band == CFTemperatureBand.SevereCold)
            return CFTemperatureBand.MildCold;

        if (coldGear && band == CFTemperatureBand.ExtremeCold)
            return CFTemperatureBand.SevereCold;

        if (heatGear && !armored && band == CFTemperatureBand.SevereHeat)
            return CFTemperatureBand.MildHeat;

        return band;
    }

    /// <summary>
    /// A creature that finds heat or cold less severe, by steps of the table. A negative number of steps is worse. Heat steps
    /// only touch heat bands and cold steps only cold ones, and neither goes past normal or the incredible end.
    /// </summary>
    public static CFTemperatureBand Eased(CFTemperatureBand band, int heatSteps, int coldSteps)
    {
        var value = (int) band;
        const int normal = (int) CFTemperatureBand.Normal;
        if (value > normal && heatSteps != 0)
            value = Math.Clamp(value - heatSteps, normal, (int) CFTemperatureBand.IncredibleHeat);
        else if (value < normal && coldSteps != 0)
            value = Math.Clamp(value + coldSteps, (int) CFTemperatureBand.IncredibleCold, normal);

        return (CFTemperatureBand) value;
    }
}
