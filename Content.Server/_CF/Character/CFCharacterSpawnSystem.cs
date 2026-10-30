using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Preferences;
using Content.Shared.GameTicking;
using Content.Shared.Inventory;
using Robust.Shared.Prototypes;

namespace Content.Server.CF.Character;

/// <summary>
/// Applies Pathfinder build data from the character profile when a player spawns.
/// </summary>
public sealed partial class CFCharacterSpawnSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private InventorySystem _inventory = default!;

    [SubscribeLocalEvent]
    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent args)
    {
        ApplyBuild(args.Mob, args.Profile);
    }

    public void ApplyBuild(EntityUid mob, Content.Shared.Preferences.HumanoidCharacterProfile profile)
    {
        var sheet = CFAttributeMath.Snapshot(_prototypes, profile);

        var abilities = EnsureComp<CFAbilityScoreComponent>(mob);
        abilities.AbilityModifiers = new Dictionary<CFAbility, int>(sheet.Modifiers);
        Dirty(mob, abilities);

        var armorBonus = 0;
        int? dexCap = null;
        var armorProficiency = "Unarmored";
        foreach (var item in _inventory.GetHandOrInventoryEntities(mob))
        {
            if (!TryComp(item, out CFArmorComponent? armor))
                continue;

            // Use the highest item bonus if multiple armor pieces somehow exist.
            if (armor.ItemBonus < armorBonus)
                continue;

            armorBonus = armor.ItemBonus;
            dexCap = armor.DexCap;
            armorProficiency = armor.Proficiency;
        }

        var trainedArmor = armorProficiency == "Unarmored"
            ? CFAttributeMath.IsUnarmoredTrained(sheet.Class)
            : CFAttributeMath.IsArmorTrained(sheet.Class, armorProficiency);

        var combatant = EnsureComp<CFCombatantComponent>(mob);
        combatant.Level = sheet.Level;
        combatant.ArmorClass = CFAttributeMath.ComputeAc(
            sheet.Modifiers,
            sheet.Level,
            trainedArmor,
            armorBonus,
            dexCap);
        combatant.MaxHp = sheet.MaxHp;
        combatant.CurrentHp = sheet.MaxHp;
        combatant.ClassId = profile.CFBuild.Class?.Id;
        Dirty(mob, combatant);
    }
}
