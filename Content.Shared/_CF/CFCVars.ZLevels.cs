using Robust.Shared.Configuration;

namespace Content.Shared.CF;

/// <summary>
/// Settings for the z-level system, which came from CrystallEdge (see <c>Content.Shared/_CF/ZLevels/</c>).
/// Fall damage is not here: it follows the Player Core falling rules in <c>CFFallRules</c>.
/// </summary>
public sealed partial class CFCVars
{
    /// <summary>How many times a second vertical physics steps. Positions between levels are integrated at this rate.</summary>
    public static readonly CVarDef<int> ZLevelsPhysicsTickRate =
        CVarDef.Create("cf.zlevels.physics_tick_rate", 60, CVar.ARCHIVE);

    /// <summary>Whether the client simulates vertical physics itself, for prediction.</summary>
    public static readonly CVarDef<bool> ZLevelsPhysicsClientSimulation =
        CVarDef.Create("cf.zlevels.physics_client_simulation", true, CVar.ARCHIVE | CVar.CLIENT);

    /// <summary>Draws a lip under tile edges bordering an opening into the level below, giving floors visual thickness.</summary>
    public static readonly CVarDef<bool> ZLevelsRenderingTileVolume =
        CVarDef.Create("cf.zlevels.rendering_tile_volume", true, CVar.ARCHIVE | CVar.CLIENTONLY);

    /// <summary>Decibels taken off a positioned sound for every level between it and the listener.</summary>
    public static readonly CVarDef<float> ZLevelsAudioPerLevelAttenuation =
        CVarDef.Create("cf.zlevels.audio_per_level_attenuation_db", 9f, CVar.ARCHIVE | CVar.CLIENT);

    /// <summary>Occlusion added to a sound from another level when an opaque tile lies between it and the listener.</summary>
    public static readonly CVarDef<float> ZLevelsAudioFloorOcclusion =
        CVarDef.Create("cf.zlevels.audio_floor_occlusion", 3f, CVar.ARCHIVE | CVar.CLIENT);
}
