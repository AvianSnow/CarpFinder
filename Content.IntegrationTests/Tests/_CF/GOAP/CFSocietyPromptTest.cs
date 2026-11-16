using Content.Server.CF.GOAP.Society;

namespace Content.IntegrationTests.Tests.CF.GOAP;

/// <summary>
/// The question a tribe member sends a language model, the answer it reads back, and the rules it lives by with no
/// model at all. Plain code, so these need no game running.
/// </summary>
[TestFixture]
[TestOf(typeof(CFSocietyPrompt))]
public sealed class CFSocietyPromptTest
{
    private static CFSocietyState Sniv()
    {
        var state = new CFSocietyState { Name = "Sniv", Role = "Scout", Hunger = 75, Thirst = 10, StockLow = true };
        state.Inventory.Add("Rusty Dagger");
        state.Nearby.Add(new CFSocietyNearby("Adventurer_1", "Armed, Holding Bread", Stranger: true));
        state.Nearby.Add(new CFSocietyNearby("Kobold_Grik", "Hunger: 80/100"));
        state.Memory.Add("Heard footsteps near the west tunnel.");
        return state;
    }

    [Test]
    public void TheWorldIsAFewShortLines()
    {
        var world = CFSocietyPrompt.World(Sniv());
        Assert.That(world, Does.Contain("Name: Sniv | Role: Scout | Hunger: 75/100"));
        Assert.That(world, Does.Contain("Inventory: [Rusty Dagger]"));
        Assert.That(world, Does.Contain("TribeFoodStock: Low"));
        Assert.That(world, Does.Contain("NearbyEntities: [Adventurer_1 (Armed, Holding Bread), Kobold_Grik (Hunger: 80/100)]"));
        Assert.That(world, Does.Contain("RecentMemory: \"Heard footsteps near the west tunnel.\""));
        Assert.That(world, Does.Not.Contain("Heard:"), "Nobody has spoken to Sniv.");

        var spoken = Sniv();
        spoken.Heard = "Adventurer_1 said to us: 'Give us passage'";
        Assert.That(CFSocietyPrompt.World(spoken), Does.EndWith("\nHeard: Adventurer_1 said to us: 'Give us passage'"));
    }

    [Test]
    public void TheGrammarOnlyAllowsKnownGoalsAndTheCreaturesNamed()
    {
        var grammar = CFSocietyPrompt.Grammar(Sniv());
        Assert.That(grammar, Does.Contain("choice ::= \"\\\"DefendNest\\\", \\\"target\\\": \" stranger | \"\\\"ParleyForFood\\\", \\\"target\\\": \" stranger"
                                          + " | \"\\\"ShareResource\\\", \\\"target\\\": \" mate | \"\\\"Socialize\\\", \\\"target\\\": \" mate"
                                          + " | \"\\\"GatherFood\\\", \\\"target\\\": \" \"\\\"\\\"\"\n"));
        Assert.That(grammar, Does.Contain("stranger ::= \"\\\"Adventurer_1\\\"\"\n"));
        Assert.That(grammar, Does.Contain("mate ::= \"\\\"Kobold_Grik\\\"\"\n"));
        // llama.cpp rejected the whole request when the class held a real newline instead of the escape.
        Assert.That(grammar, Does.Contain(@"line ::= [^""\\\n]{0,120}" + "\n"));

        // With something said to it, the line cannot be empty.
        var spoken = Sniv();
        spoken.Heard = "Adventurer_1 said to us: 'Hello'";
        Assert.That(CFSocietyPrompt.Grammar(spoken), Does.Contain(@"line ::= [^""\\\n]{3,120}" + "\n"));
        Assert.That(grammar.Split('\n').Length, Is.EqualTo(6), "Five rules, each on its own line, with a newline after the last.");

        // Alone, the only answer left is GatherFood.
        var alone = CFSocietyPrompt.Grammar(new CFSocietyState());
        Assert.That(alone, Does.Contain("choice ::= \"\\\"GatherFood\\\", \\\"target\\\": \" \"\\\"\\\"\"\n"));
        Assert.That(alone, Does.Not.Contain("stranger ::="));
        Assert.That(alone, Does.Not.Contain("mate ::="));
    }

    [Test]
    public void TheRequestCarriesTheGrammar()
    {
        var request = CFSocietyPrompt.Request(Sniv());
        Assert.That(request, Does.Contain("\"grammar\""));
        Assert.That(request, Does.Contain("\"max_tokens\":64"));
    }

    [Test]
    public void AnAnswerIsRead()
    {
        const string text = "{\"goal\": \"ParleyForFood\", \"target\": \"Adventurer_1\", \"utterance\": \"Halt, big-folk! Give Sniv that bread and you pass safe!\"}";
        Assert.That(CFSocietyPrompt.TryParse(text, out var answer));
        Assert.That(answer.Goal, Is.EqualTo(CFSocietyGoal.ParleyForFood));
        Assert.That(answer.Target, Is.EqualTo("Adventurer_1"));
        Assert.That(answer.Utterance, Is.EqualTo("Halt, big-folk! Give Sniv that bread and you pass safe!"));
    }

    [Test]
    public void AnAnswerCutShortStillCounts()
    {
        Assert.That(CFSocietyPrompt.TryParse("{\"goal\": \"Socialize\", \"target\": \"Kobold_Grik\", \"utterance\": \"Grik, did you he", out var answer));
        Assert.That(answer.Goal, Is.EqualTo(CFSocietyGoal.Socialize));
        Assert.That(answer.Utterance, Is.EqualTo("Grik, did you he"));
    }

    [Test]
    public void AnythingButAKnownGoalIsDropped()
    {
        Assert.That(CFSocietyPrompt.TryParse("{\"goal\": \"BurnTheVillage\", \"target\": \"\", \"utterance\": \"\"}", out _), Is.False);
        Assert.That(CFSocietyPrompt.TryParse("{\"goal\": \"1\", \"target\": \"\", \"utterance\": \"\"}", out _), Is.False);
        Assert.That(CFSocietyPrompt.TryParse("I think Sniv should parley.", out _), Is.False);
        Assert.That(CFSocietyPrompt.TryParse(null, out _), Is.False);
    }

    [Test]
    public void TheModelsTextIsFoundInTheServersReply()
    {
        Assert.That(CFSocietyPrompt.ReadContent("{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"hello\"}}]}"), Is.EqualTo("hello"));
        Assert.That(CFSocietyPrompt.ReadContent("{\"content\":\"plain\"}"), Is.EqualTo("plain"));
        Assert.That(CFSocietyPrompt.ReadContent("not json"), Is.Null);
    }

    /// <summary>Only an armed stranger inside the nest of a tribe that is fed and not outnumbered is attacked on sight.</summary>
    [Test]
    public void TheRulesTalkFirstUnlessTheNestIsThreatened()
    {
        Assert.That(CFSocietyRules.Greet(new CFSocietySituation(true, true, false, false)), Is.EqualTo(CFTribeStance.Hostile));
        Assert.That(CFSocietyRules.Greet(new CFSocietySituation(true, true, true, false)), Is.EqualTo(CFTribeStance.Parley), "Hungry.");
        Assert.That(CFSocietyRules.Greet(new CFSocietySituation(true, true, false, true)), Is.EqualTo(CFTribeStance.Parley), "Outnumbered.");
        Assert.That(CFSocietyRules.Greet(new CFSocietySituation(false, true, false, false)), Is.EqualTo(CFTribeStance.Parley), "Unarmed.");
        Assert.That(CFSocietyRules.Greet(new CFSocietySituation(true, false, false, false)), Is.EqualTo(CFTribeStance.Parley), "Outside the nest.");
    }
}
