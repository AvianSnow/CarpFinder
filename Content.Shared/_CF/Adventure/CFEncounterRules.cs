using Content.Shared.CF.Shop;

namespace Content.Shared.CF.Adventure;

/// <summary>GM Core threat levels for an encounter.</summary>
public enum CFThreat : byte
{
    Low,
    Moderate,
    Severe,
    Extreme,
}

/// <summary>GM Core's encounter budget and Treasure by Encounter tables.</summary>
public static class CFEncounterRules
{
    /// <summary>The XP budget assumes four characters.</summary>
    public const int BaseParty = 4;

    /// <summary>Gold pieces by party level 1 to 20: low, moderate, severe, extreme.</summary>
    private static readonly int[,] TreasureGold =
    {
        { 13, 18, 26, 35 },
        { 23, 30, 45, 60 },
        { 38, 50, 75, 100 },
        { 65, 85, 130, 170 },
        { 100, 135, 200, 270 },
        { 150, 200, 300, 400 },
        { 220, 290, 440, 580 },
        { 300, 400, 600, 800 },
        { 430, 570, 860, 1140 },
        { 600, 800, 1200, 1600 },
        { 865, 1150, 1725, 2300 },
        { 1250, 1650, 2475, 3300 },
        { 1875, 2500, 3750, 5000 },
        { 2750, 3650, 5500, 7300 },
        { 4100, 5450, 8200, 10900 },
        { 6200, 8250, 12400, 16500 },
        { 9600, 12800, 19200, 25600 },
        { 15600, 20800, 31200, 41600 },
        { 26600, 35500, 53250, 71000 },
        { 36800, 49000, 73500, 98000 },
    };

    /// <summary>XP to spend on an encounter. Each character above or below four adds or removes the adjustment.</summary>
    public static int Budget(CFThreat threat, int partySize)
    {
        var (budget, adjustment) = threat switch
        {
            CFThreat.Low => (60, 20),
            CFThreat.Moderate => (80, 20),
            CFThreat.Severe => (120, 30),
            _ => (160, 40),
        };

        return Math.Max(adjustment, budget + adjustment * (partySize - BaseParty));
    }

    /// <summary>The encounter's treasure in copper, for a party of this level.</summary>
    public static int TreasureCopper(int partyLevel, CFThreat threat)
    {
        var row = Math.Clamp(partyLevel, 1, TreasureGold.GetLength(0)) - 1;
        return TreasureGold[row, (int) threat] * CFCoins.CopperPerGold;
    }
}
