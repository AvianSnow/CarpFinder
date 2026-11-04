using System.Numerics;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Combat;
using Content.Shared.CF.DamageTypes;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Client.CF.Combat;

/// <summary>
/// Floats Hit / Miss / Crit Hit / Crit Miss labels (then damage) above the target.
/// </summary>
public sealed partial class CFDamageNumberSystem : EntitySystem
{
    private const float RisePixels = 58f;
    private const float HeadClearance = 26f;

    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IResourceCache _cache = default!;
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IUserInterfaceManager _ui = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private readonly List<FloatingNumber> _numbers = new();
    private Font _font = default!;

    public override void Initialize()
    {
        base.Initialize();
        _font = new VectorFont(_cache.GetResource<FontResource>("/Fonts/NotoSans/NotoSans-Bold.ttf"), 16);
        _overlays.AddOverlay(new CFDamageNumberOverlay(this));
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlays.RemoveOverlay<CFDamageNumberOverlay>();
    }

    public override void FrameUpdate(float frameTime)
    {
        for (var i = _numbers.Count - 1; i >= 0; i--)
        {
            var number = _numbers[i];
            number.Age += frameTime;
            if (number.Age >= number.Lifetime)
                _numbers.RemoveAt(i);
        }
    }

    [SubscribeNetworkEvent]
    private void OnNumber(CFCombatNumberEvent ev)
    {
        var target = GetEntity(ev.Target);
        if (!TryComp(target, out TransformComponent? transform))
            return;

        var origin = _transform.GetMapCoordinates(target, transform);
        var critical = ev.Degree is CFDegree.CriticalSuccess or CFDegree.CriticalFailure;
        var horizontal = _random.Next(-16, 17);

        _numbers.Add(new FloatingNumber
        {
            Map = origin,
            Text = DegreeLabel(ev.Degree),
            Color = DegreeColor(ev.Degree),
            Critical = critical,
            Scale = critical ? 1.35f : 1.05f,
            Lifetime = 0.85f + _random.Next(0, 41) / 100f,
            Horizontal = horizontal,
            Stack = 0,
        });

        if (ev.Degree is CFDegree.Success or CFDegree.CriticalSuccess)
        {
            var blocked = ev.Amount <= 0;
            var criticalHit = ev.Degree == CFDegree.CriticalSuccess;
            _numbers.Add(new FloatingNumber
            {
                Map = origin,
                Text = blocked ? "0" : ev.Amount.ToString(),
                Color = blocked ? new Color(255, 40, 40) : ColorForDamage(ev.DamageType),
                Critical = criticalHit,
                Scale = FontScale(blocked ? 1 : ev.Amount, criticalHit),
                Lifetime = 0.9f + _random.Next(0, 41) / 100f,
                Horizontal = horizontal + _random.Next(-4, 5),
                Stack = 1,
            });
        }
    }

    public void Draw(DrawingHandleScreen handle, OverlayDrawArgs args, float uiScale)
    {
        if (_numbers.Count == 0 || args.ViewportControl == null)
            return;

        var matrix = args.ViewportControl.GetWorldToScreenMatrix();
        foreach (var number in _numbers)
        {
            if (number.Map.MapId != args.MapId || !args.WorldBounds.Contains(number.Map.Position))
                continue;

            var t = Math.Clamp(number.Age / number.Lifetime, 0f, 1f);
            var alpha = 1f - t * t;
            var rise = (1f - (1f - t) * (1f - t)) * RisePixels * uiScale;
            var screen = Vector2.Transform(number.Map.Position, matrix);
            screen.X += number.Horizontal * uiScale;
            screen.Y -= (HeadClearance + number.Stack * 20f) * uiScale + rise;

            var scale = number.Scale * uiScale;
            var size = handle.GetDimensions(_font, number.Text, scale);
            var position = screen - size / 2f;
            var fill = number.Color.WithAlpha(alpha);
            var outline = number.Critical
                ? new TextOutline(2f, new Color(255, 90, 20).WithAlpha(alpha))
                : new TextOutline(1f, Color.Black.WithAlpha(alpha * 0.82f));
            handle.DrawString(_font, position, number.Text, scale, fill, outline);
        }
    }

    public float UiScale()
    {
        var scale = _config.GetCVar(CVars.DisplayUIScale);
        return scale == 0f ? _ui.DefaultUIScale : scale;
    }

    private string DegreeLabel(CFDegree degree)
    {
        return degree switch
        {
            CFDegree.CriticalSuccess => Loc.GetString("cf-combat-number-crit-hit"),
            CFDegree.Success => Loc.GetString("cf-combat-number-hit"),
            CFDegree.Failure => Loc.GetString("cf-combat-number-miss"),
            CFDegree.CriticalFailure => Loc.GetString("cf-combat-number-crit-miss"),
            _ => Loc.GetString("cf-combat-number-miss"),
        };
    }

    private static Color DegreeColor(CFDegree degree)
    {
        return degree switch
        {
            CFDegree.CriticalSuccess => new Color(255, 196, 64),
            CFDegree.Success => new Color(255, 236, 160),
            CFDegree.Failure => new Color(180, 190, 200),
            CFDegree.CriticalFailure => new Color(140, 150, 165),
            _ => Color.White,
        };
    }

    private static float FontScale(int amount, bool critical)
    {
        var shown = Math.Max(amount, 1);
        var scale = 0.9f + shown * 0.04f;
        if (critical)
            scale += 0.45f;

        return Math.Clamp(scale, 0.9f, 2.6f);
    }

    private Color ColorForDamage(string type)
    {
        return CFDamageTypes.TryResolve(_prototypes, type, out var damageType)
            ? damageType.Color
            : new Color(255, 236, 160);
    }

    private sealed class FloatingNumber
    {
        public MapCoordinates Map;
        public string Text = string.Empty;
        public Color Color;
        public bool Critical;
        public float Scale = 1f;
        public float Lifetime = 1f;
        public float Age;
        public float Horizontal;
        public int Stack;
    }
}
