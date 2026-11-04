using Content.Shared.Actions;

namespace Content.Shared.CF.Effects;

/// <summary>
/// Puts the chosen mode and volley count on an action perform request, and reads them back on the server.
/// The upstream action code calls these so the CF fields stay in one place.
/// </summary>
public static class CFCastRequest
{
    /// <summary>
    /// Client: stamp the request. For a volley the first shot carries the full count;
    /// each later shot is marked as a continuation and fires one stored bolt.
    /// </summary>
    public static void Write(IEntityManager entities, EntityUid action, RequestPerformActionEvent request)
    {
        if (entities.TryGetComponent(action, out CFModeChoiceComponent? mode))
            request.CFMode = mode.Chosen;

        if (!entities.TryGetComponent(action, out CFProjectileDeliveryComponent? bolts) || !bolts.IsVolley)
            return;

        request.CFBolts = (byte) bolts.ClampedBolts;
        request.CFVolleyContinue = bolts.VolleyArmed;
        bolts.VolleyArmed = true;
        if (bolts.VolleyLeft > 0)
            bolts.VolleyLeft--;
    }

    /// <summary>Server: copy the request onto the action before the action cost is checked.</summary>
    public static void Read(IEntityManager entities, EntityUid action, RequestPerformActionEvent request)
    {
        if (entities.TryGetComponent(action, out CFModeChoiceComponent? mode))
            mode.Chosen = request.CFMode;

        if (!entities.TryGetComponent(action, out CFProjectileDeliveryComponent? bolts) || request.CFBolts == 0)
            return;

        if (!bolts.IsVolley)
        {
            bolts.ChosenBolts = request.CFBolts;
            return;
        }

        bolts.PayThisShot = !request.CFVolleyContinue;
        if (bolts.PayThisShot)
            bolts.ChosenBolts = request.CFBolts;
    }

    /// <summary>The chosen mode clicks an object, so a click on the floor means "whatever is on that tile".</summary>
    public static bool TargetsObject(IEntityManager entities, EntityUid action)
    {
        return entities.TryGetComponent(action, out CFModeChoiceComponent? mode)
               && mode.ChosenOption() is { TargetsObject: true };
    }
}
