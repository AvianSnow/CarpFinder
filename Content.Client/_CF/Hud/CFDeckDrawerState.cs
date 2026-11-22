namespace Content.Client.CF.Hud;

/// <summary>
/// When the HUD deck is open: hovering the tab or clicking it opens it, and it closes 5 seconds after the mouse leaves the
/// bar unless pinned. Kept apart from the controls so the timing can be tested without a window.
/// </summary>
public sealed class CFDeckDrawerState
{
    public const float CollapseSeconds = 5f;

    /// <summary>Hover opens the deck the moment the mouse arrives, so a click that lands just after is not a request to close it.</summary>
    public const float HoverClickGraceSeconds = 0.5f;

    private float _idle;
    private float _sinceHoverOpened = float.MaxValue;
    private bool _wasOverTab;

    public bool Expanded { get; set; }
    public bool Pinned { get; set; }

    public void Press()
    {
        _idle = 0f;
        if (Expanded && _sinceHoverOpened < HoverClickGraceSeconds)
            return;

        Expanded = !Expanded;
    }

    /// <param name="overTab">The mouse is on the expand tab.</param>
    /// <param name="overFrame">The mouse is anywhere on the bar, or an action is being dragged.</param>
    public void Update(float seconds, bool overTab, bool overFrame)
    {
        if (_sinceHoverOpened < float.MaxValue)
            _sinceHoverOpened += seconds;

        // Only on arrival: resting on the tab after a click closed the deck must not open it again.
        if (overTab && !_wasOverTab && !Expanded)
        {
            Expanded = true;
            _sinceHoverOpened = 0f;
        }

        _wasOverTab = overTab;

        if (!Expanded || Pinned || overTab || overFrame)
        {
            _idle = 0f;
            return;
        }

        _idle += seconds;
        if (_idle < CollapseSeconds)
            return;

        _idle = 0f;
        Expanded = false;
    }
}
