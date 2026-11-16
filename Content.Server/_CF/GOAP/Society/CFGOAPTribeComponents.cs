using Robust.Shared.Map;

namespace Content.Server.CF.GOAP.Society;

/// <summary>How a tribe treats a stranger. It is one decision for the whole tribe, made by its leader.</summary>
public enum CFTribeStance : byte
{
    /// <summary>Talking: the tribe stands off and demands food, until its patience runs out.</summary>
    Parley,

    /// <summary>Fighting, as any monster would.</summary>
    Hostile,

    /// <summary>Left alone, because they gave food.</summary>
    Peace,
}

/// <summary>What a tribe remembers about one stranger.</summary>
public sealed class CFTribeStranger
{
    public CFTribeStance Stance;

    /// <summary>The short name the language model knows this one by, such as Adventurer_2. Fixed from the first meeting.</summary>
    public string Label = string.Empty;

    /// <summary>When talking ends and fighting starts.</summary>
    public TimeSpan ParleyUntil;

    /// <summary>When the peace their food bought runs out.</summary>
    public TimeSpan PeaceUntil;

    /// <summary>When the demand is next spoken.</summary>
    public TimeSpan NextDemand;

    /// <summary>They drew blood, or let the tribe's patience run out. Nothing but a gift of food changes its mind now.</summary>
    public bool Settled;
}

/// <summary>One thing a tribe member remembers, and who told it, so news is not told back to its teller.</summary>
public readonly record struct CFTribeMemory(string Text, EntityUid? From);

/// <summary>What a tribe saw the last time one of its own looked at a plot. It knows nothing newer than this.</summary>
public sealed class CFSoilRecord
{
    /// <summary>From 0 to 1.</summary>
    public float Water;

    /// <summary>From 0 to 1.</summary>
    public float Nutrients;

    public bool Planted;

    /// <summary>There is a crop to pick, or a dead plant to clear.</summary>
    public bool Ready;

    /// <summary>A living plant grown enough for the clippers to take a seed from, and not sampled yet.</summary>
    public bool Sampleable;

    public TimeSpan At;
}

/// <summary>
/// What a tribe knows is in one of its stores, from living there. The survey counts it, and whoever puts something
/// in or takes something out keeps the count right in between, so nobody has to open a crate to know what it holds.
/// </summary>
public sealed class CFStoreRecord
{
    /// <summary>Things that can be eaten as they are.</summary>
    public int Food;

    /// <summary>Wheat, flour, dough and the like: food that still wants work.</summary>
    public int Ingredients;

    /// <summary>Coins, potions and other things worth keeping.</summary>
    public int Valuables;

    /// <summary>Planks, logs and coal for the fire.</summary>
    public int Fuel;

    /// <summary>When a member last looked inside, or the survey last counted it.</summary>
    public TimeSpan At;
}

/// <summary>How far along a piece of food is, and so what the next errand with it does.</summary>
public enum CFFoodStage : byte
{
    /// <summary>Not food at all.</summary>
    None,

    /// <summary>Wheat: ground at a mortar into flour.</summary>
    Grind,

    /// <summary>Flour: mixed into water for dough.</summary>
    Mix,

    /// <summary>Dough or raw meat: held over the fire.</summary>
    Cook,

    /// <summary>A loaf: cut into slices with a knife, so it goes round. It can also be eaten whole.</summary>
    Slice,

    /// <summary>Eaten as it is.</summary>
    Ready,
}

/// <summary>What a member keeping the nest found to do.</summary>
public enum CFChoreKind : byte
{
    None,

    /// <summary>The fire is out or nearly through its fuel, or a poor plot wants ash: a piece of fuel goes in.</summary>
    FeedFire,

    /// <summary>Rubbish near the nest: carried to the edge and thrown out.</summary>
    Rubbish,

    /// <summary>A puddle near the nest: wiped up.</summary>
    Puddle,

    /// <summary>A coin or a potion lying loose, or something in its own hand nobody can use: put in a store.</summary>
    Valuable,
}

/// <summary>
/// A group that lives together: a nest, stores, plots, water, a leader, and what it thinks of strangers. It sits on
/// the encounter spawner, which is already the group's shared memory. An <c>encounter</c> with <c>tribe: true</c>
/// gets one. Every number can be changed in VV.
/// </summary>
[RegisterComponent]
public sealed partial class CFGOAPTribeComponent : Component
{
    /// <summary>How far from the nest the tribe's stores, plots, water and fire may be, in tiles.</summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float NestTiles = 16f;

    /// <summary>
    /// How many times faster than stock its members get hungry and thirsty. Stock takes about three hours to starve,
    /// which is too slow to watch.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float Appetite = 6f;

    /// <summary>A member is hungry or thirsty below this share of full.</summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float NeedBelow = 0.4f;

    /// <summary>Once it starts eating or drinking, it goes on up to this share of full.</summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float FullAbove = 0.8f;

    /// <summary>With fewer pieces of food than this in its stores, the tribe counts its stock as low.</summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public int LowStock = 4;

    /// <summary>A plot drier than this share of full gets watered.</summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float DryBelow = 0.35f;

    /// <summary>A plot with less than this share of its nutrients gets fertilizer.</summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float PoorBelow = 0.25f;

    /// <summary>With less than this left to burn, and nothing waiting in it, the fire is fed.</summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float LowFuel = 30f;

    /// <summary>How long what was seen at a plot is trusted before someone looks again.</summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public TimeSpan InspectEvery = TimeSpan.FromSeconds(90);

    /// <summary>How long the tribe demands food before it attacks.</summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public TimeSpan ParleyPatience = TimeSpan.FromSeconds(30);

    /// <summary>How long a gift of food buys peace.</summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public TimeSpan PeaceTime = TimeSpan.FromMinutes(10);

    /// <summary>The shortest and longest wait between two chats for one member, in seconds.</summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float ChatMin = 40f;

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float ChatMax = 90f;

    /// <summary>Its members never sleep, so the tribe lives on with no player near. See <c>CFGOAPSleepSystem</c>.</summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public bool AlwaysAwake = true;

    /// <summary>The fire the tribe lives round, or where it spawned with no fire near.</summary>
    [ViewVariables]
    public EntityCoordinates? Nest;

    /// <summary>The most skilled member alive, and the eldest of those. It decides how strangers are treated.</summary>
    [ViewVariables]
    public EntityUid? Leader;

    [ViewVariables]
    public List<EntityUid> Stores = new();

    [ViewVariables]
    public List<EntityUid> Plots = new();

    [ViewVariables]
    public List<EntityUid> Waters = new();

    [ViewVariables]
    public List<EntityUid> Fires = new();

    /// <summary>Mortars lying round the nest, for grinding wheat.</summary>
    [ViewVariables]
    public List<EntityUid> Mortars = new();

    /// <summary>Knives lying round the nest, for slicing a loaf.</summary>
    [ViewVariables]
    public List<EntityUid> Knives = new();

    /// <summary>Clippers lying round the nest, for taking seeds from a grown plant.</summary>
    [ViewVariables]
    public List<EntityUid> Clippers = new();

    /// <summary>Survival knives lying round the nest, for butchering what the tribe hunts.</summary>
    [ViewVariables]
    public List<EntityUid> Butchers = new();

    /// <summary>Pieces of food in <see cref="Stores"/> that can be eaten as they are, as far as the tribe knows.</summary>
    [ViewVariables]
    public int FoodStock;

    /// <summary>What each store holds, as far as the tribe knows. See <see cref="CFStoreRecord"/>.</summary>
    [ViewVariables]
    public Dictionary<EntityUid, CFStoreRecord> Stock = new();

    /// <summary>How many questions its members have sent a language model, and how many answers were usable.</summary>
    [ViewVariables]
    public int Questions;

    [ViewVariables]
    public int Answers;

    /// <summary>The last usable answer, as the model wrote it. For looking at in VV.</summary>
    [ViewVariables]
    public string LastAnswer = string.Empty;

    /// <summary>A member with a question that cannot wait, which had to wait for one already out.</summary>
    public EntityUid? UrgentAsker;

    public TimeSpan NextSurvey;

    public TimeSpan NextUpkeep;

    public Dictionary<EntityUid, CFSoilRecord> Soil = new();

    public Dictionary<EntityUid, CFTribeStranger> Strangers = new();

    /// <summary>How many strangers the tribe has met, so each gets a label of its own.</summary>
    public int StrangersMet;

    /// <summary>Who is on their way to what, and until when, so two members do not go for the same thing.</summary>
    public Dictionary<EntityUid, (EntityUid Worker, TimeSpan Until)> Claims = new();
}

/// <summary>
/// A member of a tribe: who it is, what it remembers, and the errand it is on. The step systems serve every creature,
/// so what one is in the middle of is kept here.
/// </summary>
[RegisterComponent]
public sealed partial class CFGOAPSocialComponent : Component
{
    /// <summary>The most things it remembers. Kept small so a question to the language model stays short.</summary>
    public const int MemoryLimit = 3;

    [ViewVariables(VVAccess.ReadWrite)]
    public string Name = string.Empty;

    [ViewVariables(VVAccess.ReadWrite)]
    public string Role = string.Empty;

    /// <summary>The last few things it saw or was told, newest last.</summary>
    [ViewVariables]
    public List<CFTribeMemory> Memory = new();

    /// <summary>The last thing a stranger said to it, under the stranger's label, for the model to answer.</summary>
    public string? Heard;

    public TimeSpan HeardAt;

    /// <summary>What the current errand is going to: a plot, a store, a tribemate.</summary>
    [ViewVariables]
    public EntityUid? Errand;

    /// <summary>A thing the errand needs first, such as the bucket or the seed.</summary>
    [ViewVariables]
    public EntityUid? Tool;

    /// <summary>What the errand is working on besides: the loaf being sliced, the plant being clipped for a seed.</summary>
    public EntityUid? Working;

    /// <summary>Whether the errand has done its one irreversible thing yet, such as clipping the plant.</summary>
    public bool Acted;

    /// <summary>What the tidying errand found.</summary>
    [ViewVariables]
    public CFChoreKind Chore;

    /// <summary>When it got to where the errand is, or null while it is still walking.</summary>
    public TimeSpan? ArrivedAt;

    /// <summary>What it is walking up to, and the spot beside it that it picked to stand on.</summary>
    public EntityUid? WalkTarget;

    public System.Numerics.Vector2 WalkPoint;

    public TimeSpan NextChat;

    /// <summary>A line the language model gave it, to say at its next chat or demand.</summary>
    [ViewVariables]
    public string? Line;

    /// <summary>After this the line is stale, and is dropped unsaid.</summary>
    public TimeSpan LineUntil;

    /// <summary>Who the language model said to go to, for sharing or a chat.</summary>
    public EntityUid? Preferred;

    public TimeSpan NextQuestion;

    /// <summary>When it last took something in, so it is not asked to bite faster than it can chew.</summary>
    public TimeSpan NextBite;
}
