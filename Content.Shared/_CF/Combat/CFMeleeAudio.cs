using Content.Shared.CF.DamageTypes;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Combat;

/// <summary>
/// Default hit sounds for CF weapons that do not set <c>soundHit</c> on <c>MeleeWeapon</c>.
/// The sound comes from the <c>cfDamageType</c> prototype; types without one use the blade sound.
/// Swing sounds stay on that component (<c>soundSwing</c>, fist whoosh unless a weapon overrides it).
/// </summary>
public static class CFMeleeAudio
{
    private static readonly SoundSpecifier Slash = new SoundPathSpecifier("/Audio/Weapons/bladeslice.ogg");

    public static SoundSpecifier HitSound(IPrototypeManager prototypes, string cfDamageType)
    {
        return CFDamageTypes.TryResolve(prototypes, cfDamageType, out var type) && type.HitSound != null
            ? type.HitSound
            : Slash;
    }
}
