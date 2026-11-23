using Content.Shared.CF.Crafting;
using Content.Shared.CF.Spells;
using Content.Shared.CF.Companions;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Combat;
using Content.Shared.CF.DamageTypes;
using Content.Shared.CF.Detection;
using Content.Shared.CF.Outdoors;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Terrain;
using Content.Shared.CF.Traits;
using Content.Shared.CF.ZLevels;
using Content.Shared.Inventory;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Character.Grants;

/// <summary>Raises proficiency ranks. A rank the character already beats is left alone.</summary>
public sealed partial class CFGrantProficiency : CFGrant
{
    /// <summary>
    /// What is raised: a save, <c>Perception</c>, a skill, a name from a class's <c>attacks:</c> or <c>defenses:</c> list,
    /// a <see cref="CFWeaponGroup"/>, <see cref="CFDefense.ClassDcProficiency"/>, or <see cref="CFDefense.SpellProficiency"/>.
    /// </summary>
    [DataField(required: true)]
    public List<string> Proficiencies = new();

    [DataField(required: true)]
    public CFProficiencyRank Rank;

    public override void Apply(IEntityManager entities, CFGrantArgs args)
    {
        var features = entities.System<CFCharacterFeatureSystem>();
        foreach (var proficiency in Proficiencies)
        {
            features.RaiseProficiency(args.Mob, proficiency, Rank);
        }
    }
}

/// <summary>More Hit Points for every level so far, and at each level to come.</summary>
public sealed partial class CFGrantHitPoints : CFGrant
{
    [DataField(required: true)]
    public int PerLevel;

    public override void Apply(IEntityManager entities, CFGrantArgs args)
    {
        var combat = entities.EnsureComponent<CFCombatantComponent>(args.Mob);
        var gain = PerLevel * Math.Max(1, combat.Level);
        combat.BonusHpPerLevel += PerLevel;
        combat.MaxHp += gain;
        combat.CurrentHp += gain;
        entities.Dirty(args.Mob, combat);
    }
}

/// <summary>Trained in the lightest armor the character is not yet trained in: light, then medium, then heavy.</summary>
public sealed partial class CFGrantArmorProficiency : CFGrant
{
    private static readonly string[] Order = { "LightArmor", "MediumArmor", "HeavyArmor" };

    public override void Apply(IEntityManager entities, CFGrantArgs args)
    {
        var prototypes = IoCManager.Resolve<IPrototypeManager>();
        var combat = entities.EnsureComponent<CFCombatantComponent>(args.Mob);
        foreach (var armor in Order)
        {
            if (CFDefense.ArmorRank(prototypes, combat, armor) >= CFProficiencyRank.Trained)
                continue;

            entities.System<CFCharacterFeatureSystem>().RaiseProficiency(args.Mob, armor, CFProficiencyRank.Trained);
            entities.System<CFArmorSystem>().RefreshArmorClass(args.Mob);
            return;
        }
    }
}

/// <summary>A lasting bonus. It stacks by the usual rules, so a status bonus here does not add to a status spell.</summary>
public sealed partial class CFGrantBonus : CFGrant
{
    [DataField(required: true)]
    public CFBonusStat Stat;

    [DataField(required: true)]
    public CFBonusType Type;

    /// <summary>Negative for a penalty.</summary>
    [DataField(required: true)]
    public int Value;

    /// <summary>When set, the bonus only counts against a source with this trait.</summary>
    [DataField]
    public ProtoId<CFTraitPrototype>? Trait;

    /// <summary>When set with <see cref="CFBonusStat.SkillChecks"/>, the bonus only counts for this skill.</summary>
    [DataField]
    public ProtoId<CFAbilitySkillPrototype>? Skill;

    public override void Apply(IEntityManager entities, CFGrantArgs args)
    {
        entities.System<CFBonusSystem>().AddPermanentBonus(args.Mob, Stat, Type, Value, Trait, Skill?.Id);
    }
}

/// <summary>A lasting bonus that only counts while raging, with panache, or with no armor on.</summary>
public sealed partial class CFGrantWhile : CFGrant
{
    [DataField(required: true)]
    public CFBonusWhile While;

    [DataField(required: true)]
    public CFBonusStat Stat;

    [DataField(required: true)]
    public CFBonusType Type;

    /// <summary>Negative for a penalty.</summary>
    [DataField(required: true)]
    public int Value;

    /// <summary>When set, the bonus only counts against a source with this trait.</summary>
    [DataField]
    public ProtoId<CFTraitPrototype>? Trait;

    /// <summary>When set with <see cref="CFBonusStat.SkillChecks"/>, the bonus only counts for this skill.</summary>
    [DataField]
    public ProtoId<CFAbilitySkillPrototype>? Skill;

    public override void Apply(IEntityManager entities, CFGrantArgs args)
    {
        entities.System<CFWhileBonusSystem>().Add(args.Mob, new CFWhileBonus
        {
            While = While,
            Stat = Stat,
            Type = Type,
            Value = Value,
            Trait = Trait,
            Skill = Skill?.Id,
        });
    }
}

/// <summary>Gives the creature an aura, or adds to the one it has. With no effects it is an aura that does nothing yet.</summary>
public sealed partial class CFGrantAura : CFGrant
{
    [DataField]
    public List<CFAuraEffect> Effects = new();

    /// <summary>Makes the aura at least this wide, in feet.</summary>
    [DataField]
    public int SizeFeet;

    public override void Apply(IEntityManager entities, CFGrantArgs args)
    {
        entities.System<CFAuraSystem>().Add(args.Mob, Effects, SizeFeet);
    }
}

/// <summary>What a Barbarian's instinct does to Rage.</summary>
public sealed partial class CFGrantRage : CFGrant
{
    /// <summary>Replaces the 2 that Rage adds to melee damage. 0 leaves it alone.</summary>
    [DataField]
    public int Damage;

    /// <summary>The die of an unarmed attack the creature has while raging. 0 means none.</summary>
    [DataField]
    public int AttackDie;

    [DataField]
    public string AttackType = string.Empty;

    /// <summary>Rage's extra damage is of this type, dealt apart from the weapon's (Dragon, Spirit).</summary>
    [DataField]
    public string DamageType = string.Empty;

    /// <summary>What Rage adds against a creature that cast a spell in the last hour (Superstition).</summary>
    [DataField]
    public int CasterDamage;

    /// <summary>What Rage adds with an oversized weapon (Giant).</summary>
    [DataField]
    public int OversizedDamage;

    /// <summary>Entering a Rage restores Hit Points equal to its temporary ones, once per 10 minutes (Superstition).</summary>
    [DataField]
    public bool Heals;

    [DataField]
    public bool Darkvision;

    [DataField]
    public bool Scent;

    /// <summary>Flat checks of DC 3 against concealed and DC 9 against hidden targets while raging (Supernatural Senses).</summary>
    [DataField]
    public bool KeenSenses;

    /// <summary>More Bulk while raging (Adrenaline Rush).</summary>
    [DataField]
    public int Bulk;

    public override void Apply(IEntityManager entities, CFGrantArgs args)
    {
        entities.System<CFCharacterFeatureSystem>().SetRage(args.Mob, Damage, AttackDie, AttackType);
        var options = entities.EnsureComponent<CFCombatOptionsComponent>(args.Mob);
        if (DamageType.Length > 0)
            options.RageDamageType = DamageType;

        options.RageCasterDamage = Math.Max(options.RageCasterDamage, CasterDamage);
        options.RageOversizedDamage = Math.Max(options.RageOversizedDamage, OversizedDamage);
        options.RageHeals |= Heals;
        options.RageDarkvision |= Darkvision;
        options.RageScent |= Scent;
        options.RageKeenSenses |= KeenSenses;
        options.RageBulk = Math.Max(options.RageBulk, Bulk);
        entities.Dirty(args.Mob, options);
    }
}

/// <summary>Makes one result of a save a degree better, such as a success against fear becoming a critical success.</summary>
public sealed partial class CFGrantSaveUpgrade : CFGrant
{
    /// <summary>Null means every save.</summary>
    [DataField]
    public CFDefenseType? Save;

    /// <summary>When set, it only works against a source with this trait.</summary>
    [DataField]
    public ProtoId<CFTraitPrototype>? Trait;

    /// <summary>The result that gets better.</summary>
    [DataField]
    public CFDegree From = CFDegree.Success;

    public override void Apply(IEntityManager entities, CFGrantArgs args)
    {
        entities.System<CFBonusSystem>().AddSaveUpgrade(args.Mob, Save, Trait, From);
    }
}

/// <summary>A lasting resistance. Set <see cref="Amount"/>, or <see cref="HalfLevel"/> for one that grows.</summary>
public sealed partial class CFGrantResistance : CFGrant
{
    [DataField(required: true)]
    public ProtoId<CFDamageTypePrototype> DamageType;

    [DataField]
    public int Amount;

    /// <summary>Half the creature's level, at least 1, worked out when damage lands.</summary>
    [DataField]
    public bool HalfLevel;

    public override void Apply(IEntityManager entities, CFGrantArgs args)
    {
        entities.System<CFBonusSystem>().AddPermanentResistance(args.Mob, DamageType, Amount, HalfLevel);
    }
}

/// <summary>Weapons a character's people use. For proficiency they count as simpler than they are.</summary>
public sealed partial class CFGrantWeaponFamiliarity : CFGrant
{
    /// <summary>These count as simple weapons, and the character is at least trained with them.</summary>
    [DataField]
    public List<EntProtoId> Weapons = new();

    /// <summary>A weapon with one of these traits counts as one category simpler: advanced as martial, martial as simple.</summary>
    [DataField]
    public List<ProtoId<CFTraitPrototype>> Traits = new();

    /// <summary>These are a deity's favored weapon, which feats such as Deadly Simplicity look for.</summary>
    [DataField]
    public bool Favored;

    /// <summary>From this level, a critical hit with one of these weapons gets its critical specialization. 0 means never.</summary>
    [DataField]
    public int CriticalSpecializationLevel;

    public override void Apply(IEntityManager entities, CFGrantArgs args)
    {
        entities.System<CFCharacterFeatureSystem>().AddFamiliarity(args.Mob, Weapons, Traits, Favored);
        if (CriticalSpecializationLevel <= 0)
            return;

        entities.System<CFCriticalSpecializationSystem>().Add(args.Mob, new CFCriticalSpecialization
        {
            Weapons = new List<EntProtoId>(Weapons),
            WeaponTraits = new List<ProtoId<CFTraitPrototype>>(Traits),
            MinLevel = CriticalSpecializationLevel,
        });
    }
}

/// <summary>Critical specialization with some weapons, or under some condition. See <see cref="CFCriticalSpecialization"/>.</summary>
public sealed partial class CFGrantCriticalSpecialization : CFGrant
{
    [DataField(required: true)]
    public CFCriticalSpecialization Specialization = default!;

    public override void Apply(IEntityManager entities, CFGrantArgs args)
    {
        entities.System<CFCriticalSpecializationSystem>().Add(args.Mob, Specialization);
    }
}

/// <summary>A critical hit with a Strike recharges the character's breath weapons, as Kobold Momentum does for Kobold Breath.</summary>
public sealed partial class CFGrantBreathMomentum : CFGrant
{
    public override void Apply(IEntityManager entities, CFGrantArgs args)
    {
        entities.EnsureComponent<CFDraconicMomentumComponent>(args.Mob);
    }
}

/// <summary>
/// Spell energy carried into a weapon: after the character casts a spell, its next Strike that hits within 6 seconds deals
/// a die more of some damage type. Bespell Strikes, Psi Strikes, Apparition's Enhancement.
/// </summary>
public sealed partial class CFGrantBespell : CFGrant
{
    [DataField]
    public int Die = 6;

    [DataField]
    public ProtoId<CFDamageTypePrototype> DamageType = "Force";

    /// <summary>A cantrip counts as the spell too.</summary>
    [DataField]
    public bool Cantrips;

    public override void Apply(IEntityManager entities, CFGrantArgs args)
    {
        var options = entities.EnsureComponent<CFCombatOptionsComponent>(args.Mob);
        options.BespellDie = Die;
        options.BespellType = DamageType.Id;
        options.BespellCantrips |= Cantrips;
        entities.Dirty(args.Mob, options);
    }
}

/// <summary>
/// A spell a prepared caster can cast in place of any spell it has prepared, at that spell's rank, as Call of the Wild
/// does with Summon Animal. The rules take 10 minutes to make the swap: here it happens as the spell is cast.
/// </summary>
public sealed partial class CFGrantSwapSpell : CFGrant
{
    [DataField(required: true)]
    public ProtoId<CFSpellPrototype> Spell;

    public override void Apply(IEntityManager entities, CFGrantArgs args)
    {
        entities.System<CFCharacterFeatureSystem>().GrantSpell(args.Mob, Spell);
        var book = entities.EnsureComponent<CFSpellbookComponent>(args.Mob);
        if (!book.SwapSpells.Contains(Spell))
            book.SwapSpells.Add(Spell);

        entities.Dirty(args.Mob, book);
    }
}

/// <summary>A sense beyond normal sight.</summary>
public sealed partial class CFGrantSense : CFGrant
{
    [DataField(required: true)]
    public CFSense Sense;

    /// <summary>How far an imprecise sense reaches. Vision ignores this.</summary>
    [DataField]
    public int RangeFeet;

    public override void Apply(IEntityManager entities, CFGrantArgs args)
    {
        entities.System<CFCharacterFeatureSystem>().GrantSense(args.Mob, Sense, RangeFeet);
    }
}

/// <summary>
/// A spell from outside the character's slots. It needs a spell action entity. A cantrip is at will, and so is a ranked
/// spell on a creature with no slots of that rank, because daily uses and focus points do not exist yet (roadmap 1f).
/// </summary>
public sealed partial class CFGrantSpell : CFGrant
{
    [DataField(required: true)]
    public ProtoId<CFSpellPrototype> Spell;

    public override void Apply(IEntityManager entities, CFGrantArgs args)
    {
        entities.System<CFCharacterFeatureSystem>().GrantSpell(args.Mob, Spell);
    }
}

/// <summary>A focus spell. It adds a Focus Point to the character's pool, up to 3, and the Refocus action.</summary>
public sealed partial class CFGrantFocusSpell : CFGrant
{
    [DataField(required: true)]
    public ProtoId<CFSpellPrototype> Spell;

    public override void Apply(IEntityManager entities, CFGrantArgs args)
    {
        entities.System<CFCharacterFeatureSystem>().GrantFocusSpell(args.Mob, Spell);
    }
}

/// <summary>Focus Points with no focus spell attached, as a Psychic's, and the Refocus action.</summary>
public sealed partial class CFGrantFocusPoints : CFGrant
{
    [DataField]
    public int Count = 1;

    public override void Apply(IEntityManager entities, CFGrantArgs args)
    {
        entities.System<CFCharacterFeatureSystem>().AddFocusPoints(args.Mob, Count);
    }
}

/// <summary>A resistance that counts while the character wears their armor innovation.</summary>
public sealed partial class CFGrantInnovationResistance : CFGrant
{
    [DataField(required: true)]
    public CFInnovationResistance Resistance = default!;

    public override void Apply(IEntityManager entities, CFGrantArgs args)
    {
        entities.System<CFInventorSystem>().AddResistance(args.Mob, Resistance);
    }
}

/// <summary>An Animist attunes to an apparition: its spells, and its vessel spell if it is the primary one.</summary>
public sealed partial class CFGrantApparition : CFGrant
{
    [DataField]
    public List<ProtoId<CFSpellPrototype>> Spells = new();

    [DataField]
    public ProtoId<CFSpellPrototype>? Vessel;

    [DataField]
    public bool Primary;

    public override void Apply(IEntityManager entities, CFGrantArgs args)
    {
        entities.System<CFApparitionSystem>().Attune(args.Mob, Spells, Vessel, Primary);
    }
}

/// <summary>Apparition spell slots.</summary>
public sealed partial class CFGrantApparitionSlots : CFGrant
{
    [DataField(required: true)]
    public int Rank;

    [DataField]
    public int Count = 1;

    public override void Apply(IEntityManager entities, CFGrantArgs args)
    {
        entities.System<CFApparitionSystem>().AddSlots(args.Mob, Rank, Count);
    }
}

/// <summary>What a Summoner's eidolon is. See <see cref="CFEidolonLinkComponent"/>.</summary>
public sealed partial class CFGrantEidolon : CFGrant
{
    [DataField(required: true)]
    public string Name = string.Empty;

    [DataField]
    public int Constitution;

    [DataField]
    public int Intelligence;

    [DataField]
    public int Wisdom;

    [DataField]
    public int Charisma;

    [DataField]
    public int SpeedFeet = 25;

    [DataField]
    public ProtoId<CFDamageTypePrototype>? Resistance;

    [DataField]
    public bool VoidHealing;

    public override void Apply(IEntityManager entities, CFGrantArgs args)
    {
        var link = entities.EnsureComponent<CFEidolonLinkComponent>(args.Mob);
        link.Name = Name;
        link.Constitution = Constitution;
        link.Intelligence = Intelligence;
        link.Wisdom = Wisdom;
        link.Charisma = Charisma;
        link.SpeedFeet = SpeedFeet;
        link.Resistance = Resistance;
        link.VoidHealing = VoidHealing;
    }
}

/// <summary>An eidolon's key attribute and the damage its attack deals.</summary>
public sealed partial class CFGrantEidolonBuild : CFGrant
{
    /// <summary>Null leaves the key attribute alone.</summary>
    [DataField]
    public bool? DexterityKey;

    /// <summary>Empty leaves the attack alone.</summary>
    [DataField]
    public string AttackType = string.Empty;

    /// <summary>Feet added to the eidolon's Speed.</summary>
    [DataField]
    public int ExtraSpeed;

    public override void Apply(IEntityManager entities, CFGrantArgs args)
    {
        var link = entities.EnsureComponent<CFEidolonLinkComponent>(args.Mob);
        if (DexterityKey is { } dexterity)
            link.DexterityKey = dexterity;

        if (AttackType.Length > 0)
            link.AttackType = AttackType;

        link.ExtraSpeed += ExtraSpeed;
    }
}

/// <summary>Recipes the character knows without reading a formula, such as a Runesmith's.</summary>
public sealed partial class CFGrantFormulas : CFGrant
{
    [DataField(required: true)]
    public List<ProtoId<CFCraftingRecipePrototype>> Recipes = new();

    public override void Apply(IEntityManager entities, CFGrantArgs args)
    {
        var book = entities.EnsureComponent<CFFormulaBookComponent>(args.Mob);
        foreach (var recipe in Recipes)
        {
            book.Known.Add(recipe);
        }

        entities.Dirty(args.Mob, book);
    }
}

/// <summary>An ikon for an Exemplar.</summary>
public sealed partial class CFGrantIkon : CFGrant
{
    [DataField(required: true)]
    public ProtoId<CFIkonPrototype> Ikon;

    public override void Apply(IEntityManager entities, CFGrantArgs args)
    {
        entities.System<CFIkonSystem>().Add(args.Mob, Ikon);
    }
}

/// <summary>What an Exemplar's root epithet does after a transcendence.</summary>
public sealed partial class CFGrantEpithet : CFGrant
{
    [DataField(required: true)]
    public CFEpithet Epithet;

    public override void Apply(IEntityManager entities, CFGrantArgs args)
    {
        entities.EnsureComponent<CFExemplarComponent>(args.Mob).Epithet = Epithet;
    }
}

/// <summary>What a Necromancer's thralls are like.</summary>
public sealed partial class CFGrantThralls : CFGrant
{
    /// <summary>Thralls made per casting of Create Thrall, beyond the usual two.</summary>
    [DataField]
    public int Extra;

    [DataField]
    public bool BloodReturns;

    [DataField]
    public int ExtraSpeed;

    /// <summary>Replaces the thralls' damage type.</summary>
    [DataField]
    public string DamageType = string.Empty;

    public override void Apply(IEntityManager entities, CFGrantArgs args)
    {
        var necromancer = entities.EnsureComponent<CFNecromancerComponent>(args.Mob);
        necromancer.ExtraThralls += Extra;
        necromancer.BloodReturns |= BloodReturns;
        necromancer.ExtraSpeed += ExtraSpeed;
        if (DamageType.Length > 0)
            necromancer.DamageType = DamageType;
    }
}

/// <summary>Gear placed at the character's feet.</summary>
public sealed partial class CFGrantItem : CFGrant
{
    [DataField(required: true)]
    public EntProtoId Item;

    [DataField]
    public int Count = 1;

    /// <summary>
    /// The item is built for a larger creature: a Giant instinct's weapon. It gets <c>CFOversizedWeapon</c>, which makes
    /// whoever holds it clumsy 1.
    /// </summary>
    [DataField]
    public bool Oversized;

    /// <summary>Goes in the backpack, else on the ground at the character's feet.</summary>
    public override void Apply(IEntityManager entities, CFGrantArgs args)
    {
        var coordinates = entities.GetComponent<TransformComponent>(args.Mob).Coordinates;
        var inventory = entities.System<InventorySystem>();
        var storage = entities.System<SharedStorageSystem>();
        for (var i = 0; i < Count; i++)
        {
            var item = entities.SpawnAtPosition(Item, coordinates);
            if (Oversized)
            {
                entities.EnsureComponent<Content.Shared.CF.Combat.CFOversizedWeaponComponent>(item);
                var meta = entities.GetComponent<MetaDataComponent>(item);
                entities.System<MetaDataSystem>().SetEntityName(item, Loc.GetString("cf-oversized-name", ("name", meta.EntityName)), meta);
            }

            if (inventory.TryGetSlotEntity(args.Mob, "back", out var bag)
                && entities.TryGetComponent(bag, out StorageComponent? store)
                && storage.CanInsert(bag.Value, item, out _, store))
                storage.Insert(bag.Value, item, out _, args.Mob, store, playSound: false);
        }
    }
}

/// <summary>An action on the wheel or hotbar.</summary>
public sealed partial class CFGrantAction : CFGrant
{
    [DataField(required: true)]
    public EntProtoId Action;

    public override void Apply(IEntityManager entities, CFGrantArgs args)
    {
        entities.System<CFCharacterFeatureSystem>().GrantAction(args.Mob, Action);
    }
}

/// <summary>
/// The option gives an animal companion, a familiar or a construct companion. The body is spawned with the character by
/// the server's spawn system, from the Pet tab's pick, so there is nothing to apply here: <see cref="CFPetMath"/> looks
/// for this grant to know the build has one.
/// </summary>
public sealed partial class CFGrantPet : CFGrant
{
    [DataField(required: true)]
    public CFPetKind Kind;

    public override void Apply(IEntityManager entities, CFGrantArgs args)
    {
    }
}

/// <summary>A natural attack, such as jaws or claws. It replaces the fist, unless the character already has a bigger die.</summary>
public sealed partial class CFGrantUnarmedAttack : CFGrant
{
    [DataField]
    public int Dice = 1;

    /// <summary>Required unless <see cref="TraitsOnly"/>.</summary>
    [DataField]
    public int Die;

    [DataField]
    public string DamageType = string.Empty;

    [DataField]
    public bool Finesse;

    [DataField]
    public bool Agile;

    /// <summary>The deadly die, or 0.</summary>
    [DataField]
    public int Deadly;

    /// <summary>A damage type it can deal instead, or empty.</summary>
    [DataField]
    public string Versatile = string.Empty;

    /// <summary>Only add the traits to the attack the character already has, as Dogtooth Tengu makes the beak deadly.</summary>
    [DataField]
    public bool TraitsOnly;

    public override void Apply(IEntityManager entities, CFGrantArgs args)
    {
        var features = entities.System<CFCharacterFeatureSystem>();
        if (TraitsOnly)
            features.AddUnarmedTraits(args.Mob, Deadly, Versatile);
        else
            features.GrantUnarmedAttack(args.Mob, Dice, Die, DamageType, Finesse, Agile, Deadly, Versatile);
    }
}

/// <summary>Falling never hurts this character, or with <see cref="HalfDistance"/> hurts half as much.</summary>
public sealed partial class CFGrantSafeFall : CFGrant
{
    /// <summary>It only works with a hand free.</summary>
    [DataField]
    public bool NeedsFreeHand;

    /// <summary>Falls count as half their distance instead of none.</summary>
    [DataField]
    public bool HalfDistance;

    public override void Apply(IEntityManager entities, CFGrantArgs args)
    {
        var safe = entities.EnsureComponent<CFSafeFallComponent>(args.Mob);
        safe.NeedsFreeHand = NeedsFreeHand;
        safe.HalfDistance = HalfDistance;
    }
}

/// <summary>A reaction: what sets it off and what it does. It fires by itself. See <see cref="CFReaction"/>.</summary>
public sealed partial class CFGrantReaction : CFGrant
{
    [DataField(required: true)]
    public CFReaction Reaction = default!;

    public override void Apply(IEntityManager entities, CFGrantArgs args)
    {
        var level = entities.System<CFCharacterFeatureSystem>().SourceLevel(args.Source);
        entities.System<CFReactionSystem>().Add(args.Mob, Reaction, args.Source, level);
    }
}

/// <summary>Sets a flag or mark on <see cref="CFCombatOptionsComponent"/>, for behaviour that is already coded.</summary>
public sealed partial class CFGrantOption : CFGrant
{
    [DataField]
    public CFCombatOption? Option;

    /// <summary>What pointing at an enemy does for this character.</summary>
    [DataField]
    public CFPointMark? PointMark;

    public override void Apply(IEntityManager entities, CFGrantArgs args)
    {
        var features = entities.System<CFCharacterFeatureSystem>();
        if (Option is { } option)
        {
            features.SetOption(args.Mob, option);

            // Reactions still coded as a flag take their place in the reaction order too.
            if (CFReactionSystem.BuiltIn(option) is { } reaction)
                entities.System<CFReactionSystem>().AddEntry(args.Mob, reaction.Key, reaction.Name, features.SourceLevel(args.Source));
        }

        if (PointMark is { } mark)
            features.SetPointMark(args.Mob, mark);
    }
}

/// <summary>
/// Another feat, with everything it gives. The builder counts it as taken, so it is not offered again and feats that need it
/// see it. A feat the character already has is not doubled.
/// </summary>
public sealed partial class CFGrantFeat : CFGrant
{
    [DataField(required: true)]
    public ProtoId<CFFeatPrototype> Feat;

    public override void Apply(IEntityManager entities, CFGrantArgs args)
    {
        var prototypes = IoCManager.Resolve<IPrototypeManager>();
        if (prototypes.TryIndex(Feat, out var feat))
            entities.System<CFCharacterFeatureSystem>().ApplyFeat(args.Mob, feat, adjustSpeed: false);
    }
}

/// <summary>Heat or cold one step less severe, by the temperature table. Negative is one step worse.</summary>
public sealed partial class CFGrantTemperatureTolerance : CFGrant
{
    [DataField]
    public int Heat;

    [DataField]
    public int Cold;

    public override void Apply(IEntityManager entities, CFGrantArgs args)
    {
        var tolerance = entities.EnsureComponent<CFTemperatureToleranceComponent>(args.Mob);
        tolerance.HeatSteps += Heat;
        tolerance.ColdSteps += Cold;
    }
}

/// <summary>Keener senses for finding creatures. See <see cref="CFSeekSenseComponent"/>.</summary>
public sealed partial class CFGrantSeek : CFGrant
{
    [DataField]
    public int Bonus;

    [DataField]
    public int RangeFeet = 30;

    [DataField]
    public int HiddenFlatDc;

    [DataField]
    public int ConcealedFlatDc;

    public override void Apply(IEntityManager entities, CFGrantArgs args)
    {
        var sense = entities.EnsureComponent<CFSeekSenseComponent>(args.Mob);
        sense.SeekBonus = Math.Max(sense.SeekBonus, Bonus);
        sense.RangeFeet = Math.Max(sense.RangeFeet, RangeFeet);
        if (HiddenFlatDc > 0)
            sense.HiddenFlatDc = sense.HiddenFlatDc > 0 ? Math.Min(sense.HiddenFlatDc, HiddenFlatDc) : HiddenFlatDc;

        if (ConcealedFlatDc > 0)
            sense.ConcealedFlatDc = sense.ConcealedFlatDc > 0 ? Math.Min(sense.ConcealedFlatDc, ConcealedFlatDc) : ConcealedFlatDc;

        entities.Dirty(args.Mob, sense);
    }
}

/// <summary>Better at climbing: a bonus, a success that counts as a critical success, or a climb Speed that needs no check.</summary>
public sealed partial class CFGrantClimb : CFGrant
{
    [DataField]
    public int Bonus;

    [DataField]
    public bool SuccessIsCritical;

    [DataField]
    public bool ClimbSpeed;

    public override void Apply(IEntityManager entities, CFGrantArgs args)
    {
        var options = entities.EnsureComponent<CFCombatOptionsComponent>(args.Mob);
        options.ClimbBonus += Bonus;
        options.ClimbSuccessCritical |= SuccessIsCritical;
        options.ClimbSpeed |= ClimbSpeed;
        entities.Dirty(args.Mob, options);
    }
}

/// <summary>Adds traits to the creature, or takes some away, as a Fungus Leshy is fungus and not plant.</summary>
public sealed partial class CFGrantTrait : CFGrant
{
    [DataField]
    public List<ProtoId<CFTraitPrototype>> Add = new();

    [DataField]
    public List<ProtoId<CFTraitPrototype>> Remove = new();

    public override void Apply(IEntityManager entities, CFGrantArgs args)
    {
        var traits = entities.EnsureComponent<CFTraitComponent>(args.Mob);
        foreach (var trait in Remove)
        {
            traits.Traits.Remove(trait);
        }

        foreach (var trait in Add)
        {
            traits.Traits.Add(trait);
        }

        entities.Dirty(args.Mob, traits);
    }
}

/// <summary>
/// A resistance to the damage type the player chose for this option (a <c>DamageType</c> entry in its <c>choices</c>),
/// as an Elementheart Kobold picks its element. Nothing happens until a type is picked.
/// </summary>
public sealed partial class CFGrantChosenResistance : CFGrant
{
    [DataField]
    public int Amount;

    [DataField]
    public bool HalfLevel;

    public override void Apply(IEntityManager entities, CFGrantArgs args)
    {
        var features = entities.System<CFCharacterFeatureSystem>();
        if (features.Pick(args.Mob, args.Source) is not { } type
            || !IoCManager.Resolve<IPrototypeManager>().HasIndex<CFDamageTypePrototype>(type))
            return;

        entities.System<CFBonusSystem>().AddPermanentResistance(args.Mob, type, Amount, HalfLevel);
    }
}

/// <summary>Widens a reaction the character already has, found by its name: Lucky Break makes Cat's Luck cover more saves and skills.</summary>
public sealed partial class CFGrantWidenReaction : CFGrant
{
    [DataField(required: true)]
    public LocId Name;

    /// <summary>Drops the reaction's limit to one save.</summary>
    [DataField]
    public bool AnySave;

    [DataField]
    public List<string> Skills = new();

    /// <summary>The reaction answers failed skill checks too.</summary>
    [DataField]
    public bool AndSkillChecks;

    public override void Apply(IEntityManager entities, CFGrantArgs args)
    {
        entities.System<CFReactionSystem>().Widen(args.Mob, Name, AnySave, Skills, AndSkillChecks);
    }
}

/// <summary>
/// One of several actions, by what the player picked for this option: Kobold Breath gives the breath of the chosen element.
/// Keys are the pick's values, such as damage type ids. Nothing happens until a pick is made.
/// </summary>
public sealed partial class CFGrantChosenAction : CFGrant
{
    [DataField(required: true)]
    public Dictionary<string, EntProtoId> Actions = new();

    public override void Apply(IEntityManager entities, CFGrantArgs args)
    {
        var features = entities.System<CFCharacterFeatureSystem>();
        if (features.Pick(args.Mob, args.Source) is { } pick && Actions.TryGetValue(pick, out var action))
            features.GrantAction(args.Mob, action);
    }
}

/// <summary>
/// Difficult terrain of these causes is ignored, and greater difficult terrain of them is only difficult (Wildwood
/// Halfling, Rock Runner). <see cref="NonMagicalOnly"/> leaves terrain made by magic alone. See <see cref="CFTerrainSystem"/>.
/// </summary>
public sealed partial class CFGrantIgnoreTerrain : CFGrant
{
    [DataField]
    public List<CFTerrainCause> Causes = new();

    [DataField]
    public bool NonMagicalOnly;

    /// <summary>The creature can Step while in difficult terrain (Feather Step).</summary>
    [DataField]
    public bool FeatherStep;

    public override void Apply(IEntityManager entities, CFGrantArgs args)
    {
        var walker = entities.System<CFTerrainSystem>().Walker(args.Mob);
        (NonMagicalOnly ? walker.IgnoresNonMagical : walker.Ignores).UnionWith(Causes);
        walker.FeatherStep |= FeatherStep;
        entities.Dirty(args.Mob, walker);
    }
}

/// <summary>Ignores the non-magical difficult terrain of the favored terrain the player picked (Favored Terrain).</summary>
public sealed partial class CFGrantFavoredTerrain : CFGrant
{
    public override void Apply(IEntityManager entities, CFGrantArgs args)
    {
        if (entities.System<CFCharacterFeatureSystem>().Pick(args.Mob, args.Source) is not { } pick
            || !Enum.TryParse<CFFavoredTerrain>(pick, out var terrain))
        {
            return;
        }

        var walker = entities.System<CFTerrainSystem>().Walker(args.Mob);
        walker.IgnoresNonMagical.UnionWith(CFFavoredTerrains.Causes(terrain));
        entities.Dirty(args.Mob, walker);
    }
}

/// <summary>Gives the domain spell of the domain the player picked for this option (Domain Initiate, Deity's Domain).</summary>
public sealed partial class CFGrantDomainSpell : CFGrant
{
    public override void Apply(IEntityManager entities, CFGrantArgs args)
    {
        var features = entities.System<CFCharacterFeatureSystem>();
        if (features.Pick(args.Mob, args.Source) is not { } pick
            || !IoCManager.Resolve<IPrototypeManager>().TryIndex<CFDomainPrototype>(pick, out var domain))
        {
            return;
        }

        // A source of its own: the option's own source has already run, and Apply runs each source once.
        features.Apply(args.Mob, domain.Grants, $"{args.Source}:domain:{domain.ID}");
    }
}

/// <summary>
/// A focus spell by what the player picked for another option: Order Magic gives the order spell of the order that Order
/// Explorer joined. Keys are that pick's values.
/// </summary>
public sealed partial class CFGrantChosenFocusSpell : CFGrant
{
    /// <summary>The option whose pick is read. Null reads this option's own.</summary>
    [DataField]
    public string? PickOf;

    [DataField(required: true)]
    public Dictionary<string, ProtoId<CFSpellPrototype>> Spells = new();

    public override void Apply(IEntityManager entities, CFGrantArgs args)
    {
        var features = entities.System<CFCharacterFeatureSystem>();
        if (features.Pick(args.Mob, PickOf ?? args.Source) is { } pick && Spells.TryGetValue(pick, out var spell))
            features.GrantFocusSpell(args.Mob, spell);
    }
}

/// <summary>A language the character knows beside its ancestry's and its picks, such as the druids' Wildsong.</summary>
public sealed partial class CFGrantLanguage : CFGrant
{
    [DataField(required: true)]
    public ProtoId<Content.Shared.CF.Languages.CFLanguagePrototype> Language;

    public override void Apply(IEntityManager entities, CFGrantArgs args)
    {
        var languages = entities.EnsureComponent<Content.Shared.CF.Languages.CFLanguagesComponent>(args.Mob);
        if (languages.Known.Contains(Language))
            return;

        languages.Known.Add(Language);
        entities.Dirty(args.Mob, languages);
    }
}

/// <summary>
/// More grants, by what the player picked for a <c>Named</c> choice: a Witch's lesson gives its hex, an armament its attack.
/// Keys are the choice's <c>options</c>. Nothing happens until a pick is made.
/// </summary>
public sealed partial class CFGrantChosen : CFGrant
{
    [DataField(required: true)]
    public Dictionary<string, List<CFGrant>> Options = new();

    public override void Apply(IEntityManager entities, CFGrantArgs args)
    {
        var features = entities.System<CFCharacterFeatureSystem>();
        if (features.Pick(args.Mob, args.Source) is not { } pick
            || !pick.StartsWith(CFOptionChoices.NamedPrefix, StringComparison.Ordinal))
        {
            return;
        }

        var name = pick[CFOptionChoices.NamedPrefix.Length..];
        if (Options.TryGetValue(name, out var grants))
        {
            // A source of its own: the option's own source has already run, and Apply runs each source once.
            features.Apply(args.Mob, grants, $"{args.Source}:pick:{name}");
        }
    }
}

/// <summary>
/// A Witch's familiar gift: each time the Witch casts a hex, these effects run on a creature near the familiar, or where
/// the familiar stands. See <c>CFWitchSystem</c>.
/// </summary>
public sealed partial class CFGrantFamiliarGift : CFGrant
{
    [DataField(required: true)]
    public Content.Shared.CF.Spells.CFFamiliarGift Gift = new();

    public override void Apply(IEntityManager entities, CFGrantArgs args)
    {
        var witch = entities.EnsureComponent<Content.Shared.CF.Spells.CFWitchComponent>(args.Mob);
        witch.Gifts.Add(Gift);
    }
}

/// <summary>
/// Changes what a reaction the character already has does to the enemy that set it off, by the reaction's name: Brilliant
/// Flash adds off-guard to Flash of Grandeur, Weight of Guilt replaces what Glimpse of Redemption does.
/// </summary>
public sealed partial class CFGrantChangeReaction : CFGrant
{
    [DataField(required: true)]
    public LocId Name;

    [DataField]
    public List<CFEffect> AddOnEnemy = new();

    [DataField]
    public List<CFEffect> ReplaceOnEnemy = new();

    public override void Apply(IEntityManager entities, CFGrantArgs args)
    {
        entities.System<CFReactionSystem>().ChangeEffects(args.Mob, Name, AddOnEnemy, ReplaceOnEnemy);
    }
}

/// <summary>Mercy: a 2-action Lay on Hands also ends one of these conditions on the target.</summary>
public sealed partial class CFGrantMercy : CFGrant
{
    [DataField(required: true)]
    public List<CFConditionKind> Conditions = new();

    public override void Apply(IEntityManager entities, CFGrantArgs args)
    {
        var options = entities.EnsureComponent<CFCombatOptionsComponent>(args.Mob);
        foreach (var condition in Conditions)
        {
            if (!options.MercyConditions.Contains(condition))
                options.MercyConditions.Add(condition);
        }

        entities.Dirty(args.Mob, options);
    }
}
