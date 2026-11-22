using System.Numerics;
using Robust.Client.UserInterface;

namespace Content.Client.CF.Hud;

/// <summary>Shows the bottom part of its child, so the deck slides out of the top of the bar.</summary>
public sealed class CFDeckClip : Control
{
    private float _openness;

    public CFDeckClip()
    {
        RectClipContent = true;
    }

    /// <summary>0 is fully closed and 1 fully open.</summary>
    public float Openness
    {
        get => _openness;
        set
        {
            _openness = Math.Clamp(value, 0f, 1f);
            InvalidateMeasure();
        }
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        var size = Vector2.Zero;
        foreach (var child in Children)
        {
            child.Measure(new Vector2(float.PositiveInfinity, float.PositiveInfinity));
            size = Vector2.Max(size, child.DesiredSize);
        }

        return new Vector2(size.X, size.Y * _openness);
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        foreach (var child in Children)
        {
            var height = child.DesiredSize.Y;
            child.Arrange(UIBox2.FromDimensions(0f, finalSize.Y - height, finalSize.X, height));
        }

        return finalSize;
    }
}
