using Avalonia;
using HunterPie.Core.Game.Enums;

namespace HunterPie.Linux.Overlay.Views;

/// <summary>An element icon from Assets/ElementIcons.axaml, see <see cref="VectorIcon"/></summary>
public sealed class ElementIcon : VectorIcon
{
    public static readonly StyledProperty<Element> ElementProperty =
        AvaloniaProperty.Register<ElementIcon, Element>(nameof(Element));

    static ElementIcon()
    {
        AffectsRender<ElementIcon>(ElementProperty);
    }

    public Element Element
    {
        get => GetValue(ElementProperty);
        set => SetValue(ElementProperty, value);
    }

    protected override string? ResourceKey => Enum.IsDefined(Element) ? $"Element.{Element}" : null;
}
