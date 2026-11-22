using JetBrains.Annotations;

namespace Content.Shared.CF.Character.Grants;

/// <summary>
/// One thing a character option gives: a feat, class feature, heritage, ancestry, or background.
/// Lists of these are the <c>grants</c> field on those prototypes. <see cref="CFCharacterFeatureSystem"/> runs them
/// once, at spawn or at the level the option is gained.
/// </summary>
/// <remarks>
/// Written in YAML as <c>!type:CFGrantBonus</c> and so on, the same way as <c>CFEffect</c>. The classes are in Shared
/// because the client reads the prototypes too, but they only run on the server. Grants are data: they must not keep
/// state between calls. Anything that lasts goes on a component.
/// </remarks>
[ImplicitDataDefinitionForInheritors]
[MeansImplicitUse]
public abstract partial class CFGrant
{
    public abstract void Apply(IEntityManager entities, CFGrantArgs args);
}

/// <param name="Mob">The character receiving the grant.</param>
/// <param name="Source">The option it comes from, such as <c>feat:CFDoubleSlice</c>.</param>
public sealed record CFGrantArgs(EntityUid Mob, string Source);
