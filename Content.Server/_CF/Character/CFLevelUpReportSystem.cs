using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Content.Server.Database;
using Content.Server.GameTicking;
using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Languages;
using Content.Shared.CF.Preferences;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager;

namespace Content.Server.CF.Character;

/// <summary>
/// A level can be taken with picks still open, so a player is never stuck behind a choice the sheet cannot offer. When
/// that happens this writes a report beside the server's logs, in <c>logs/cf-levelup/</c>: what was left open, what each
/// slot could have taken, and what the character looked like, so the cause can be found afterwards.
/// </summary>
public sealed partial class CFLevelUpReportSystem : EntitySystem
{
    /// <summary>The folder inside the server's log folder.</summary>
    public const string Folder = "cf-levelup";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private ISerializationManager _serialization = default!;
    [Dependency] private ServerGameTicker _ticker = default!;

    /// <summary>Where reports go: the server's log folder, resolved the way the engine resolves it, plus <see cref="Folder"/>.</summary>
    public string ReportFolder()
    {
        var path = Path.Combine(_config.GetCVar(CVars.LogPath), Folder);
        if (Path.IsPathRooted(path))
            return path;

        // The engine's own helper is internal. Its log files sit beside Robust.Shared, so these do too.
        var engine = Path.GetDirectoryName(typeof(IConfigurationManager).Assembly.Location) ?? string.Empty;
        return Path.GetFullPath(Path.Combine(engine, path));
    }

    /// <summary>
    /// Writes a report when <paramref name="unfilled"/> is not empty. Returns the file written, or null when there was
    /// nothing to report or the file could not be written. Never throws: a report must not stop a level.
    /// </summary>
    /// <param name="before">The build before the level, as the character had it.</param>
    /// <param name="sent">The picks the client sent.</param>
    /// <param name="after">The build with this level's picks taken.</param>
    /// <param name="modifiers">Ability modifiers before the level.</param>
    public string? Report(
        EntityUid mob,
        int level,
        IReadOnlyList<string> unfilled,
        CFCharacterBuildData? before,
        CFCharacterBuildData sent,
        CFCharacterBuildData after,
        string? ancestryId,
        IReadOnlyDictionary<CFAbility, int> modifiers)
    {
        if (unfilled.Count == 0)
            return null;

        var name = Name(mob);
        try
        {
            var report = Build(mob, level, unfilled, before, sent, after, ancestryId, modifiers);
            var folder = ReportFolder();
            Directory.CreateDirectory(folder);
            var file = Path.Combine(folder, $"{DateTime.UtcNow:yyyy-MM-dd_HH-mm-ss}_{SafeFileName(name)}_level{level}.json");
            File.WriteAllText(file, report.ToJsonString(JsonOptions));
            Log.Warning($"{ToPrettyString(mob)} took level {level} with {unfilled.Count} picks open: {string.Join("; ", unfilled)}. Report: {file}");
            return file;
        }
        catch (Exception e)
        {
            Log.Error($"Could not write the level up report for {ToPrettyString(mob)} at level {level}: {e}");
            return null;
        }
    }

    private JsonObject Build(
        EntityUid mob,
        int level,
        IReadOnlyList<string> unfilled,
        CFCharacterBuildData? before,
        CFCharacterBuildData sent,
        CFCharacterBuildData after,
        string? ancestryId,
        IReadOnlyDictionary<CFAbility, int> modifiers)
    {
        var report = new JsonObject
        {
            ["timeUtc"] = DateTime.UtcNow.ToString("O"),
            ["round"] = _ticker.RoundId,
            ["character"] = Name(mob),
            ["entity"] = ToPrettyString(mob).ToString(),
            ["fromLevel"] = level - 1,
            ["toLevel"] = level,
            ["unfilled"] = Strings(unfilled),
        };

        if (TryComp(mob, out ActorComponent? actor))
        {
            report["player"] = actor.PlayerSession.Name;
            report["userId"] = actor.PlayerSession.UserId.ToString();
        }

        // What each slot that asks for a pick could have taken, so an empty list shows up as one.
        var known = CompOrNull<CFLanguagesComponent>(mob)?.Known ?? new List<ProtoId<CFLanguagePrototype>>();
        var slots = new JsonObject();
        foreach (var slot in CFLevelUp.SlotsAt(_prototypes, after, level))
        {
            slots[slot.ToString()] = slot switch
            {
                CFLevelSlot.Boosts => Strings(after.AbilityBoostsLevel5.Select(ability => ability.ToString())),
                CFLevelSlot.SkillIncrease or CFLevelSlot.BonusSkillIncrease => new JsonObject
                {
                    ["picked"] = CFLevelUp.Pick(after, slot, level),
                    ["options"] = Strings(CFLevelUp.SkillOptions(_prototypes, after, ancestryId, level).Select(skill => skill.ID)),
                },
                _ => new JsonObject
                {
                    ["picked"] = CFLevelUp.Pick(after, slot, level),
                    ["options"] = Strings(CFLevelUp.FeatOptions(_prototypes, after, ancestryId, slot, level).Select(feat => feat.ID)),
                },
            };
        }

        if (CFLevelUp.NeedsLanguage(after, level, modifiers))
        {
            slots[CFLevelSlot.Language.ToString()] = new JsonObject
            {
                ["picked"] = CFLevelUp.LevelFiveLanguage(_prototypes, after, ancestryId, modifiers),
                ["options"] = Strings(CFLevelUp.LanguageOptions(_prototypes, after, ancestryId, modifiers, known).Select(language => language.ID)),
            };
        }

        var options = new JsonArray();
        foreach (var option in CFLevelUp.NewOptionSlots(_prototypes, after, ancestryId, level))
        {
            options.Add(new JsonObject
            {
                ["source"] = option.Source,
                ["slot"] = option.Slot,
                ["kind"] = option.Choice.Kind.ToString(),
                ["picked"] = after.OptionPicks.FirstOrDefault(pick => pick.Source == option.Source && pick.Slot == option.Slot)?.Value,
                ["options"] = Strings(CFOptionChoices.Allowed(_prototypes, option.Choice, after).Select(allowed => allowed.ToString())),
            });
        }

        slots["Options"] = options;
        slots["Spells"] = Strings(CFLevelUp.NewSpellSlots(_prototypes, after, level).Select(spell => $"rank {spell.Rank} slot {spell.Index}"));
        report["slots"] = slots;

        var character = new JsonObject
        {
            ["ancestry"] = ancestryId,
            ["languagesKnown"] = Strings(known.Select(language => language.Id)),
        };

        if (TryComp(mob, out CFCombatantComponent? combat))
        {
            character["class"] = combat.ClassId?.ToString();
            character["level"] = combat.Level;
            character["hp"] = $"{combat.CurrentHp}/{combat.MaxHp}";
        }

        var abilities = new JsonObject();
        foreach (var (ability, value) in modifiers)
        {
            abilities[ability.ToString()] = value;
        }

        character["abilityModifiers"] = abilities;
        report["characterBefore"] = character;
        report["buildBefore"] = Json(before);
        report["picksSent"] = Json(sent);
        report["buildAfter"] = Json(after);
        return report;
    }

    private JsonNode? Json(CFCharacterBuildData? build)
    {
        return build == null
            ? null
            : _serialization.WriteValue(build, alwaysWrite: true, notNullableOverride: true).ToJsonNode();
    }

    private static JsonArray Strings(IEnumerable<string> values)
    {
        var array = new JsonArray();
        foreach (var value in values)
        {
            array.Add(value);
        }

        return array;
    }

    private string Name(EntityUid mob)
    {
        return TryComp(mob, out MetaDataComponent? meta) ? meta.EntityName : mob.ToString();
    }

    private static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(name.Select(c => invalid.Contains(c) || char.IsWhiteSpace(c) ? '_' : c).ToArray());
        return safe.Length > 40 ? safe[..40] : safe;
    }
}
