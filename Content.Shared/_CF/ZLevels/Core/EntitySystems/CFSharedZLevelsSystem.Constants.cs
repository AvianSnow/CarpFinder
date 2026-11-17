// Ported from CrystallEdge's z-level system (https://github.com/crystallpunk-14/crystall-edge),
// with permission from eshhhed. CrystallEdge sublicenses it under the MIT License.
// CarpFinder changes: CF names and namespaces, and whatever each file's comments say.

namespace Content.Shared.CF.ZLevels.Core.EntitySystems;

public abstract partial class CFSharedZLevelsSystem
{
    public static int MaxZLevelsBelowRendering = 5;
    public static int MaxZLevelsAboveRendering = 3;
    public const float ZLevelOffset = 0.7f;

    public const float ZGravityForce = 9.8f;
    private const float ZVelocityLimit = 20.0f;
    private const int MaxStepsPerFrame = 10;

    /// <summary>
    /// The minimum speed required to trigger LandEvent events.
    /// </summary>
    private const float ImpactVelocityLimit = 4.1f;

    /// <summary>
    /// Distance to ground above which an entity is considered airborne (synced to BodyStatus.InAir).
    /// </summary>
    public const float AirborneHeightThreshold = 0.15f;
}
