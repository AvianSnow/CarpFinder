using Content.Shared.CF.Preferences;
using Robust.Shared.GameStates;

namespace Content.Shared.CF.Character;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CFAbilityScoreComponent : Component
{
    [DataField("abilityScores"), AutoNetworkedField]
    public Dictionary<CFAbility, int> AbilityScores = new()
    {
        { CFAbility.Strength, 10 },
        { CFAbility.Dexterity, 10 },
        { CFAbility.Constitution, 10 },
        { CFAbility.Intelligence, 10 },
        { CFAbility.Wisdom, 10 },
        { CFAbility.Charisma, 10 }
    };

    public int GetAbilityScore(CFAbility ability)
    {
        return AbilityScores.TryGetValue(ability, out var score) ? score : 10;
    }
}
