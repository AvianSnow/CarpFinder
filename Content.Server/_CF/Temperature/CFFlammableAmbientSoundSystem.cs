using Content.Shared.Audio;
using Content.Shared.Atmos;

namespace Content.Server.CF.Temperature;

public sealed partial class CFFlammableAmbientSoundSystem : EntitySystem
{
    [Dependency] private SharedAmbientSoundSystem _ambientSound = default!;

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
