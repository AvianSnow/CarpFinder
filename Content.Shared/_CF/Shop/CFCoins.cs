using Content.Shared.CF.Alchemy;
using Content.Shared.CF.Preferences;
using Content.Shared.Clothing;
using Content.Shared.Preferences;
using Content.Shared.Preferences.Loadouts;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Shop;

/// <summary>
/// Carpfinder coins. 10 copper become 1 silver, 10 silver become 1 gold, and 10 gold become 1 platinum.
/// The purse is stored as copper so a purchase can break a higher coin.
/// </summary>
public static class CFCoins
{
    public const int CopperPerSilver = 10;
    public const int CopperPerGold = 100;
    public const int CopperPerPlatinum = 1000;

    /// <summary>The shop's quantity box stops at 99, and the first of those is the loadout itself.</summary>
    public const int MaxCopies = 98;

    /// <summary>Level 0 starts with 5 gp. Level 1 and up start with 15 gp.</summary>
    public static int StartingCopper(int level)
    {
        return (level <= 0 ? 5 : 15) * CopperPerGold;
    }

    public static int Remaining(HumanoidCharacterProfile profile, string? jobId, IPrototypeManager prototypes)
    {
        var left = StartingCopper(profile.CFBuild.Level);
        if (string.IsNullOrEmpty(jobId))
            return left;

        var roleId = LoadoutSystem.GetJobPrototype(jobId);
        if (!profile.Loadouts.TryGetValue(roleId, out var role))
            return left;

        left -= Spent(role, prototypes);
        foreach (var (loadout, _) in OwnedLoadouts(role, prototypes))
        {
            left -= LoadoutPrice(prototypes, loadout) * CopiesOf(profile.CFBuild, loadout.ID);
        }

        return left;
    }

    /// <summary>Extra copies bought of a loadout, past the first.</summary>
    public static int CopiesOf(CFCharacterBuildData build, string loadoutId)
    {
        return Math.Clamp(build.LoadoutCopies.GetValueOrDefault(loadoutId), 0, MaxCopies);
    }

    /// <summary>
    /// The extra copies this character can really pay for, and the coin left over. The copies come from a client, so
    /// this counts them out of the purse instead of trusting them.
    /// </summary>
    public static List<(string Item, int Count)> AffordableCopies(
        HumanoidCharacterProfile profile,
        string? jobId,
        IPrototypeManager prototypes,
        out int left)
    {
        var given = new List<(string Item, int Count)>();
        left = StartingCopper(profile.CFBuild.Level);
        if (string.IsNullOrEmpty(jobId) || !profile.Loadouts.TryGetValue(LoadoutSystem.GetJobPrototype(jobId), out var role))
            return given;

        left -= Spent(role, prototypes);
        foreach (var (loadout, item) in OwnedLoadouts(role, prototypes))
        {
            var price = LoadoutPrice(prototypes, loadout);
            var copies = CopiesOf(profile.CFBuild, loadout.ID);
            if (price <= 0 || copies <= 0 || left < price)
                continue;

            var count = Math.Min(copies, left / price);
            left -= price * count;
            given.Add((item, count));
        }

        return given;
    }

    private static IEnumerable<(LoadoutPrototype Loadout, string Item)> OwnedLoadouts(RoleLoadout role, IPrototypeManager prototypes)
    {
        foreach (var picks in role.SelectedLoadouts.Values)
        {
            foreach (var pick in picks)
            {
                if (prototypes.TryIndex(pick.Prototype, out var loadout) && LoadoutItem(loadout) is { } item)
                    yield return (loadout, item);
            }
        }
    }

    public static int Spent(RoleLoadout role, IPrototypeManager prototypes)
    {
        var total = 0;
        foreach (var picks in role.SelectedLoadouts.Values)
        {
            foreach (var pick in picks)
            {
                if (!prototypes.TryIndex(pick.Prototype, out var loadout))
                    continue;

                total += LoadoutPrice(prototypes, loadout);
            }
        }

        return total;
    }

    public static int LoadoutPrice(IPrototypeManager prototypes, LoadoutPrototype loadout)
    {
        var id = LoadoutItem(loadout);
        if (id == null || !prototypes.TryIndex<EntityPrototype>(id, out var proto))
            return 0;

        return EntityPrice(proto);
    }

    public static int EntityPrice(EntityPrototype proto)
    {
        if (proto.TryGetComponent<CFItemPriceComponent>(out var price))
            return Math.Max(0, price.Copper);

        if (proto.TryGetComponent<CFAlchemicalItemComponent>(out var brew))
            return Math.Max(0, brew.PriceGp * CopperPerGold);

        return 0;
    }

    /// <summary>
    /// What a merchant pays for <paramref name="count"/> of a stack whose prototype holds
    /// <paramref name="prototypeCount"/>: the share of the printed Price, halved unless it resells in full.
    /// </summary>
    public static int SaleValue(int price, bool fullResale, int count, int prototypeCount)
    {
        var worth = price * count / Math.Max(1, prototypeCount);
        return fullResale ? worth : worth / 2;
    }

    /// <summary>Item price, lowest coins omitted when they are zero.</summary>
    public static string FormatPrice(int copper)
    {
        if (copper <= 0)
            return "—";

        var pp = copper / CopperPerPlatinum;
        var gp = copper % CopperPerPlatinum / CopperPerGold;
        var sp = copper % CopperPerGold / CopperPerSilver;
        var cp = copper % CopperPerSilver;
        var parts = new List<string>();
        if (pp > 0)
            parts.Add($"{pp} pp");
        if (gp > 0)
            parts.Add($"{gp} gp");
        if (sp > 0)
            parts.Add($"{sp} sp");
        if (cp > 0)
            parts.Add($"{cp} cp");
        return string.Join(", ", parts);
    }

    /// <summary>Always shows all four coins, already rolled up at every 10.</summary>
    public static string FormatWallet(int copper)
    {
        var negative = copper < 0;
        var left = Math.Abs(copper);
        var text = Loc.GetString("cf-shop-wallet",
            ("pp", left / CopperPerPlatinum),
            ("gp", left % CopperPerPlatinum / CopperPerGold),
            ("sp", left % CopperPerGold / CopperPerSilver),
            ("cp", left % CopperPerSilver));
        return negative ? "-" + text : text;
    }

    /// <summary>The item a loadout is priced by and sold as.</summary>
    public static string? LoadoutItem(LoadoutPrototype loadout)
    {
        foreach (var held in loadout.Inhand)
        {
            if (!string.IsNullOrEmpty(held.Id))
                return held.Id;
        }

        foreach (var pair in loadout.Equipment)
        {
            if (!string.IsNullOrEmpty(pair.Value.Id))
                return pair.Value.Id;
        }

        return null;
    }
}
