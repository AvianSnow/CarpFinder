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

/// <summary>
/// Something near the creature asking, under the name the model is given for it. A stranger can be defended against
/// or asked for food; a tribemate can be shared with or talked to. The grammar keeps each goal to its own kind.
/// </summary>
public readonly record struct CFSocietyNearby(string Label, string Notes, bool Stranger = false);

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

    /// <summary>What a stranger just said to it, if anything. The answer's line is a reply to this.</summary>
    public string? Heard;
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
        "Hunger and Thirst run from 0 (full) to 100 (starving). 80/100 is very hungry.\n" +
        "Goals: DefendNest and ParleyForFood target an Adventurer. ShareResource and Socialize target a Kobold. GatherFood has no target.\n" +
        "The utterance is one short line in the kobold's own rough voice, under 15 words, or empty.\n" +
        "If there is a Heard line, an Adventurer is talking to you: the utterance is your reply to it, in your own words. Never repeat what they said.\n" +
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
        if (state.Heard != null)
            text.Append("\nHeard: ").Append(state.Heard.Replace('"', '\''));

        return text.ToString();
    }

    /// <summary>
    /// A GBNF grammar for llama.cpp, so the model cannot write anything but one of the five goals, each with a
    /// target of the kind it is about: a stranger for DefendNest and ParleyForFood, a tribemate for ShareResource and
    /// Socialize, nothing for GatherFood. A goal with nobody of its kind in sight is left out, so with nothing near
    /// the only answer is GatherFood. Without this a small model parleys with its own tribemates, which is dropped.
    /// </summary>
    public static string Grammar(CFSocietyState state)
    {
        var strangers = new StringBuilder();
        var mates = new StringBuilder();
        foreach (var nearby in state.Nearby)
        {
            var list = nearby.Stranger ? strangers : mates;
            if (list.Length > 0)
                list.Append(" | ");

            list.Append("\"\\\"").Append(nearby.Label).Append("\\\"\"");
        }

        var choices = new StringBuilder();
        void Choice(CFSocietyGoal goal, string target)
        {
            if (choices.Length > 0)
                choices.Append(" | ");

            choices.Append("\"\\\"").Append(goal).Append("\\\", \\\"target\\\": \" ").Append(target);
        }

        if (strangers.Length > 0)
        {
            Choice(CFSocietyGoal.DefendNest, "stranger");
            Choice(CFSocietyGoal.ParleyForFood, "stranger");
        }

        if (mates.Length > 0)
        {
            Choice(CFSocietyGoal.ShareResource, "mate");
            Choice(CFSocietyGoal.Socialize, "mate");
        }

        Choice(CFSocietyGoal.GatherFood, "\"\\\"\\\"\"");

        var grammar = new StringBuilder();
        grammar.Append("root ::= \"{\\\"goal\\\": \" choice \", \\\"utterance\\\": \\\"\" line \"\\\"}\"\n");
        grammar.Append("choice ::= ").Append(choices).Append('\n');
        if (strangers.Length > 0)
            grammar.Append("stranger ::= ").Append(strangers).Append('\n');
        if (mates.Length > 0)
            grammar.Append("mate ::= ").Append(mates).Append('\n');

        // GBNF wants the newline in the class written as the escape \n. A real newline there made llama.cpp refuse
        // every request with "failed to parse grammar". The line is capped so a cut-off answer is rare, and with
        // something to answer it cannot be empty: a small model otherwise leaves a greeting unanswered.
        grammar.Append(state.Heard != null ? @"line ::= [^""\\\n]{3,120}" : @"line ::= [^""\\\n]{0,120}").Append('\n');
        return grammar.ToString();
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
