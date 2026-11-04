using Content.Shared.Audio;
using Content.Shared.Atmos;

namespace Content.Server._CF.Temperature;

public sealed class CFFlammableAmbientSoundSystem : EntitySystem
{
    [Dependency] private readonly SharedAmbientSoundSystem _ambientSound = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CFFlammableAmbientSoundComponent, IgnitedEvent>(OnIgnited);
        SubscribeLocalEvent<CFFlammableAmbientSoundComponent, ExtinguishedEvent>(OnExtinguished);
    }

    private void OnIgnited(EntityUid uid, CFFlammableAmbientSoundComponent component, ref IgnitedEvent args)
    {
        _ambientSound.SetAmbience(uid, true);
    }

    private void OnExtinguished(EntityUid uid, CFFlammableAmbientSoundComponent component, ref ExtinguishedEvent args)
    {
        _ambientSound.SetAmbience(uid, false);
    }
}
