using System.Numerics;
using Content.Client.UserInterface.Systems.Actions.Controls;
using Content.Shared.CF.Character;
using Content.Shared.CF.Spells;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;

namespace Content.Client.CF.Hud;

/// <summary>
/// Drawn over an action button. A spell that cannot be cast is darkened, and a prepared spell shows how many charges are
/// left (x2) with one node per charge prepared.
/// </summary>
public sealed class CFActionOverlay : Control
{
    private const string FontPath = "/Fonts/NotoSans/NotoSans-Bold.ttf";
    private const float Node = 7f;
    private const float NodeGap = 2f;

    private static readonly Color Dim = new(0, 0, 0, 170);
    private static readonly Color Ready = new(96, 176, 232);
    private static readonly Color Spent = new(64, 64, 70);

    private static Font? _font;

    private bool _castable = true;
    private int _ready;
    private int _total;

    private CFActionOverlay()
    {
        MouseFilter = MouseFilterMode.Ignore;
    }

    /// <summary>Brings a button's overlay up to date, adding one if it has none.</summary>
    public static void Refresh(ActionButton button, IEntityManager entities, EntityUid? caster)
    {
        CFActionOverlay? overlay = null;
        foreach (var child in button.Children)
        {
            if (child is not CFActionOverlay found)
                continue;

            overlay = found;
            break;
        }

        if (overlay == null)
        {
            overlay = new CFActionOverlay();
            button.AddChild(overlay);
        }

        overlay._castable = true;
        overlay._ready = 0;
        overlay._total = 0;
        if (button.Action is not { } action
            || caster is not { } user
            || !entities.TryGetComponent(action, out CFSpellComponent? spell))
            return;

        overlay._castable = entities.System<CFSpellSystem>().HasSlot(user, spell);
        if (!spell.Focus && entities.TryGetComponent(user, out CFSpellbookComponent? attuned) && CFApparitions.UsesSlot(attuned, spell.Spell, spell.Rank))
        {
            overlay._ready = CFApparitions.Left(attuned, spell.Rank);
            overlay._total = CFApparitions.Total(attuned, spell.Rank);
            return;
        }

        if (spell.Focus || !entities.TryGetComponent(user, out CFSpellbookComponent? book) || !CFPreparedSpells.UsesCharges(book, spell.Spell))
            return;

        (overlay._ready, overlay._total) = CFPreparedSpells.Charges(book, spell.Spell);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        var box = PixelSizeBox;
        if (!_castable)
            handle.DrawRect(box, Dim);

        if (_total <= 0)
            return;

        var size = Node * UIScale;
        var gap = NodeGap * UIScale;
        var y = box.Height - size - 3f * UIScale;
        for (var i = 0; i < _total; i++)
        {
            var x = 3f * UIScale + i * (size + gap);
            handle.DrawRect(UIBox2.FromDimensions(x, y, size, size), i < _ready ? Ready : Spent);
        }

        _font ??= new VectorFont(IoCManager.Resolve<IResourceCache>().GetResource<FontResource>(FontPath), 11);
        var text = $"x{_ready}";
        var dimensions = handle.GetDimensions(_font, text, UIScale);
        handle.DrawString(_font, new Vector2(box.Width - dimensions.X - 3f * UIScale, 1f * UIScale), text, UIScale, Color.White);
    }
}
