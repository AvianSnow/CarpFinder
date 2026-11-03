using Content.Server.Administration;
using Content.Shared.Administration;
using Content.Shared.CF.Character;
using Robust.Shared.Console;

namespace Content.Server.CF.Character;

[AdminCommand(AdminFlags.Admin)]
public sealed class CFGiveXpCommand : LocalizedEntityCommands
{
    public override string Command => "givexp";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 2)
        {
            shell.WriteLine(Loc.GetString("cmd-givexp-help", ("command", Command)));
            return;
        }

        if (!TryAward(args, out var amount, out var error))
        {
            shell.WriteLine(error);
            return;
        }

        var xp = EntityManager.System<CFExperienceSystem>();
        if (!xp.TryFindFaction(args[0], out _))
        {
            shell.WriteLine(Loc.GetString("cmd-givexp-unknown", ("name", args[0])));
            return;
        }

        var (count, behind) = xp.AwardFaction(args[0], amount);
        if (count == 0)
        {
            shell.WriteLine(Loc.GetString("cmd-givexp-none", ("name", args[0])));
            return;
        }

        shell.WriteLine(Loc.GetString("cmd-givexp-done", ("name", args[0]), ("count", count), ("xp", amount), ("behind", behind)));
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length == 1)
        {
            var names = new List<string>();
            foreach (var faction in EntityManager.System<CFExperienceSystem>().Factions())
                names.Add(faction);
            return CompletionResult.FromHintOptions(names, Loc.GetString("cmd-givexp-company"));
        }

        if (args.Length == 2)
        {
            return CompletionResult.FromHintOptions(
                new[] { "50", "minor", "moderate", "major", "adversary", "hazard" },
                Loc.GetString("cmd-givexp-amount"));
        }

        return CompletionResult.Empty;
    }

    private bool TryAward(string[] args, out int amount, out string error)
    {
        amount = 0;
        error = "";
        var kind = args[1];
        if (int.TryParse(kind, out amount))
            return amount > 0 ? true : Fail(out error);

        if (CFExperienceAwards.TryAccomplishment(kind, out amount))
            return true;

        if (string.Equals(kind, "adversary", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Length < 3 || !TryRelative(args[2], out var relative) || !CFExperienceAwards.TryAdversary(relative, out amount))
                return Fail(out error);
            return true;
        }

        if (string.Equals(kind, "hazard", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Length < 4
                || !TryComplexity(args[2], out var complex)
                || !TryRelative(args[3], out var relative)
                || !CFExperienceAwards.TryHazard(complex, relative, out amount))
                return Fail(out error);
            return true;
        }

        return Fail(out error);
    }

    private bool Fail(out string error)
    {
        error = Loc.GetString("cmd-givexp-bad");
        return false;
    }

    private static bool TryRelative(string text, out int relative)
    {
        return int.TryParse(text, out relative);
    }

    private static bool TryComplexity(string text, out bool complex)
    {
        if (string.Equals(text, "complex", StringComparison.OrdinalIgnoreCase))
        {
            complex = true;
            return true;
        }

        if (string.Equals(text, "simple", StringComparison.OrdinalIgnoreCase))
        {
            complex = false;
            return true;
        }

        complex = false;
        return false;
    }
}
