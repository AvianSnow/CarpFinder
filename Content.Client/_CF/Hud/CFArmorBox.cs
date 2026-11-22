using System.Numerics;
using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;

namespace Content.Client.CF.Hud;

/// <summary>
/// Armor Class. What a raised shield or a spell adds right now shows beside it in green or red. While off-guard, the
/// normal value is struck through and the lower one drawn in red.
/// </summary>
public sealed class CFArmorBox : Control
{
    private const string FontPath = "/Fonts/NotoSans/NotoSans-Bold.ttf";
    private static readonly Color Back = new(12, 12, 16, 230);
    private static readonly Color Ink = new(232, 206, 140);
    private static readonly Color Struck = new(196, 196, 196);
    private static readonly Color OffGuard = new(220, 48, 48);

    private readonly Font _font;
    private int _armorClass = 10;
    private int _modifier;
    private bool _offGuard;

    public CFArmorBox()
    {
        _font = new VectorFont(IoCManager.Resolve<IResourceCache>().GetResource<FontResource>(FontPath), 14);
        MinSize = new Vector2(100f, 26f);
        MouseFilter = MouseFilterMode.Stop;
    }

    public void Set(int armorClass, int modifier, bool offGuard)
    {
        _armorClass = armorClass;
        _modifier = modifier;
        _offGuard = offGuard;

        var total = armorClass + modifier;
        ToolTip = offGuard
            ? Loc.GetString("cf-hud-ac-tooltip-off-guard", ("ac", total), ("reduced", total - CFFlanking.OffGuardAcPenalty))
            : Loc.GetString("cf-hud-ac-tooltip", ("ac", total));
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        var box = PixelSizeBox;
        handle.DrawRect(box, Back);
        handle.DrawRect(box, Ink, filled: false);

        var label = Loc.GetString("cf-hud-ac");
        var labelSize = handle.GetDimensions(_font, label, UIScale);
        handle.DrawString(_font, new Vector2(6f * UIScale, (box.Height - labelSize.Y) / 2f), label, UIScale, Ink);

        var gap = 6f * UIScale;
        var x = box.Width - 6f * UIScale;
        var total = _armorClass + _modifier;
        if (_offGuard)
        {
            x = DrawRight(handle, x, box, (total - CFFlanking.OffGuardAcPenalty).ToString(), OffGuard, false) - gap;
            DrawRight(handle, x, box, total.ToString(), Struck, true);
            return;
        }

        if (_modifier != 0)
            x = DrawRight(handle, x, box, CFSkills.FormatSigned(_modifier), CFHudColors.ForBonus(_modifier) ?? Color.White, false) - gap;

        DrawRight(handle, x, box, _armorClass.ToString(), Color.White, false);
    }

    /// <returns>The left edge of what was drawn.</returns>
    private float DrawRight(DrawingHandleScreen handle, float right, UIBox2 box, string text, Color color, bool struck)
    {
        var size = handle.GetDimensions(_font, text, UIScale);
        var at = new Vector2(right - size.X, (box.Height - size.Y) / 2f);
        handle.DrawString(_font, at, text, UIScale, color);
        if (struck)
        {
            var y = at.Y + size.Y * 0.55f;
            handle.DrawLine(new Vector2(at.X, y), new Vector2(right, y), color);
        }

        return at.X;
    }
}
