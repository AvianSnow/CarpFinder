using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Content.Server.CF.GOAP.Society;

/// <summary>What a language model may tell a tribe member to do. GOAP plans and carries out whichever it picks.</summary>
public enum CFSocietyGoal : byte
{
    DefendNest,
    ParleyForFood,
    ShareResource,
    Socialize,
    GatherFood,
}

/// <summary>What the tribe can tell about a stranger at a glance. The built-in rules decide on this alone.</summary>
public readonly record struct CFSocietySituation(bool Armed, bool InNest, bool Hungry, bool Outnumbered);

/// <summary>The rules a tribe lives by with no language model, and falls back on when one does not answer.</summary>
public static class CFSocietyRules
{
    /// <summary>
    /// How a stranger is met. The nest is defended against anyone armed who walks into it, but a tribe that is hungry
    /// or outnumbered talks first, and so does one that meets someone unarmed or outside the nest.
    /// </summary>
    public static CFTribeStance Greet(CFSocietySituation situation)
    {
        return situation is { Armed: true, InNest: true, Hungry: false, Outnumbered: false }
            ? CFTribeStance.Hostile
            : CFTribeStance.Parley;
    }
}

/// <summary>Something near the creature asking, under the name the model is given for it.</summary>
public readonly record struct CFSocietyNearby(string Label, string Notes);

/// <summary>Everything one question tells the model. Kept short, so a small model on a CPU answers in seconds.</summary>
public sealed class CFSocietyState
{
    public string Name = string.Empty;
    public string Role = string.Empty;

    /// <summary>From 0, full, to 100, starving.</summary>
    public int Hunger;

    public int Thirst;
    public List<string> Inventory = new();
    public bool StockLow;
    public List<CFSocietyNearby> Nearby = new();
    public List<string> Memory = new();
}

public readonly record struct CFSocietyAnswer(CFSocietyGoal Goal, string Target, string Utterance);

/// <summary>
/// Writes the question for the language model and reads its answer. Plain code with no game in it, so it tests
/// without one. The model only ever picks a goal: it never plans, and anything it sends that is not a known goal or
/// a creature it was told about is dropped.
/// </summary>
public static class CFSocietyPrompt
{
    public const string System =
        "You are the decision brain for a Kobold in a tribal cave.\n" +
        "Rules:\n" +
        "1. Protect the nest from adventurers, BUT if you are hungry or outnumbered, try to parley and demand food before attacking.\n" +
        "2. If adventurers drop or offer food, accept it and leave them alone.\n" +
        "3. Share extra food with hungry tribemates.\n" +
        "4. Chat with nearby kobolds when safe to spread news.\n" +
        "5. With no adventurers near and low food, gather food.\n" +
        "\n" +
        "Output ONLY valid JSON matching this schema:\n" +
        "{\"goal\": \"DefendNest|ParleyForFood|ShareResource|Socialize|GatherFood\", \"target\": \"string\", \"utterance\": \"short spoken line or empty\"}";

    private static readonly Regex GoalPattern = new("\"goal\"\\s*:\\s*\"(\\w+)\"", RegexOptions.Compiled);
    private static readonly Regex TargetPattern = new("\"target\"\\s*:\\s*\"([^\"]*)\"", RegexOptions.Compiled);
    private static readonly Regex UtterancePattern = new("\"utterance\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)", RegexOptions.Compiled);

    /// <summary>The world as the creature knows it, in the few lines the model reads.</summary>
    public static string World(CFSocietyState state)
    {
        var text = new StringBuilder();
        text.Append("Name: ").Append(state.Name);
        text.Append(" | Role: ").Append(state.Role);
        text.Append(" | Hunger: ").Append(state.Hunger).Append("/100");
        text.Append(" | Thirst: ").Append(state.Thirst).Append("/100");
        text.Append(" | Inventory: [").Append(string.Join(", ", state.Inventory)).Append("]\n");
        text.Append("TribeFoodStock: ").Append(state.StockLow ? "Low" : "Ok").Append('\n');

        text.Append("NearbyEntities: [");
        for (var i = 0; i < state.Nearby.Count; i++)
        {
            if (i > 0)
                text.Append(", ");

            text.Append(state.Nearby[i].Label);
            if (state.Nearby[i].Notes.Length > 0)
                text.Append(" (").Append(state.Nearby[i].Notes).Append(')');
        }

        text.Append("]\n");
        text.Append("RecentMemory: ");
        text.Append(state.Memory.Count == 0 ? "\"Nothing new.\"" : "\"" + string.Join(" ", state.Memory).Replace('"', '\'') + "\"");
        return text.ToString();
    }

    /// <summary>
    /// A GBNF grammar for llama.cpp, so the model cannot write anything but one of the five goals and one of the
    /// creatures it was told about.
    /// </summary>
    public static string Grammar(CFSocietyState state)
    {
        var targets = new StringBuilder("\"\"");
        foreach (var nearby in state.Nearby)
        {
            targets.Append(" | \"").Append(nearby.Label).Append('"');
        }

        var goals = new StringBuilder();
        foreach (var goal in Enum.GetNames<CFSocietyGoal>())
        {
            if (goals.Length > 0)
                goals.Append(" | ");

            goals.Append('"').Append(goal).Append('"');
        }

        return "root ::= \"{\\\"goal\\\": \\\"\" goal \"\\\", \\\"target\\\": \\\"\" target \"\\\", \\\"utterance\\\": \\\"\" line \"\\\"}\"\n"
               + "goal ::= " + goals + "\n"
               + "target ::= " + targets + "\n"
               + "line ::= [^\"\\\\\\n]*\n";
    }

    /// <summary>The body of a chat completion request, as llama.cpp's server and others like it take.</summary>
    public static string Request(CFSocietyState state)
    {
        var body = new JsonObject
        {
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = System },
                new JsonObject { ["role"] = "user", ["content"] = World(state) },
            },
            ["temperature"] = 0.7,
            ["max_tokens"] = 64,
            ["stream"] = false,
            ["cache_prompt"] = true,
            ["grammar"] = Grammar(state),
        };

        return body.ToJsonString();
    }

    /// <summary>Takes the model's text out of the server's reply, whichever of the common shapes it has.</summary>
    public static string? ReadContent(string reply)
    {
        try
        {
            using var document = JsonDocument.Parse(reply);
            var root = document.RootElement;
            if (root.TryGetProperty("choices", out var choices)
                && choices.ValueKind == JsonValueKind.Array
                && choices.GetArrayLength() > 0
                && choices[0].TryGetProperty("message", out var message)
                && message.TryGetProperty("content", out var chat))
                return chat.GetString();

            if (root.TryGetProperty("message", out var single) && single.TryGetProperty("content", out var text))
                return text.GetString();

            return root.TryGetProperty("content", out var plain) ? plain.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads an answer. It is lenient about the JSON, because a small model may stop before the closing brace, and
    /// strict about the goal, which has to be one of the five.
    /// </summary>
    public static bool TryParse(string? text, out CFSocietyAnswer answer)
    {
        answer = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var goal = GoalPattern.Match(text);
        if (!goal.Success || char.IsDigit(goal.Groups[1].Value[0]) || !Enum.TryParse<CFSocietyGoal>(goal.Groups[1].Value, out var chosen) || !Enum.IsDefined(chosen))
            return false;

        var target = TargetPattern.Match(text);
        var utterance = UtterancePattern.Match(text);
        answer = new CFSocietyAnswer(
            chosen,
            target.Success ? target.Groups[1].Value.Trim() : string.Empty,
            utterance.Success ? utterance.Groups[1].Value.Replace("\\\"", "\"").Replace("\\n", " ").Trim() : string.Empty);
        return true;
    }
}
