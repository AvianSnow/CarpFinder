using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.UserInterface;
using Robust.Shared.Input;
using Robust.Shared.Timing;

namespace Content.Client.CF.Hud;

/// <summary>
/// A red handle between two sections of the deck. Dragging it asks for whole columns to move. The mouse is read each frame
/// while dragging, so a drag carries on when the pointer strays off this thin control.
/// </summary>
public sealed class CFDeckDivider : Control
{
    private const float BarWidth = 4f;

    private static readonly Color Idle = new(150, 36, 36);
    private static readonly Color Hot = new(224, 72, 64);

    private readonly IInputManager _input;
    private float _anchorX;
    private bool _hovered;

    /// <summary>Told how many columns to move, and answers how many it did.</summary>
    public Func<int, int>? Moved { get; set; }

    public bool Dragging { get; private set; }

    public CFDeckDivider()
    {
        _input = IoCManager.Resolve<IInputManager>();
        MinSize = new Vector2(8f, 0f);
        MouseFilter = MouseFilterMode.Stop;
        DefaultCursorShape = CursorShape.HResize;
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);
        if (args.Function != EngineKeyFunctions.UIClick)
            return;

        Dragging = true;
        _anchorX = _input.MouseScreenPosition.Position.X;
        args.Handle();
    }

    protected override void MouseEntered()
    {
        base.MouseEntered();
        _hovered = true;
    }

    protected override void MouseExited()
    {
        base.MouseExited();
        _hovered = false;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (!Dragging)
            return;

        if (!_input.IsKeyDown(Keyboard.Key.MouseLeft))
        {
            Dragging = false;
            return;
        }

        var column = (CFDeckLayout.Slot + CFDeckLayout.Separation) * UIScale;
        var steps = (int) MathF.Round((_input.MouseScreenPosition.Position.X - _anchorX) / column);
        if (steps == 0 || Moved == null)
            return;

        // Only what really moved: past a section's minimum, the handle waits for the mouse to come back.
        _anchorX += Moved(steps) * column;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        var width = BarWidth * UIScale;
        var left = (PixelSize.X - width) / 2f;
        handle.DrawRect(new UIBox2(left, 0f, left + width, PixelSize.Y), Dragging || _hovered ? Hot : Idle);
    }
}
