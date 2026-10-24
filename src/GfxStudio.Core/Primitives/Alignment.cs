namespace GfxStudio.Core.Primitives;

public enum HorizontalAlign
{
    Left,
    Center,
    Right
}

public enum VerticalAlign
{
    Top,
    Center,
    Bottom
}

public readonly record struct Alignment(HorizontalAlign Horizontal, VerticalAlign Vertical);
