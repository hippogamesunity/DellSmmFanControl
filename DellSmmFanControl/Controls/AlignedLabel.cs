namespace DellSmmFanControl.Controls;

// Label's default glyph-overhang padding varies with the font size.
// Draw headings from the same content edge regardless of their font.
public sealed class AlignedLabel : Label
{
    public AlignedLabel()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Rectangle bounds = ClientRectangle;
        bounds.X += Padding.Left;
        bounds.Y += Padding.Top;
        bounds.Width = Math.Max(0, bounds.Width - Padding.Horizontal);
        bounds.Height = Math.Max(0, bounds.Height - Padding.Vertical);

        TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
        flags |= TextAlign switch
        {
            ContentAlignment.TopCenter => TextFormatFlags.HorizontalCenter,
            ContentAlignment.TopRight => TextFormatFlags.Right,
            ContentAlignment.MiddleLeft => TextFormatFlags.VerticalCenter,
            ContentAlignment.MiddleCenter => TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter,
            ContentAlignment.MiddleRight => TextFormatFlags.VerticalCenter | TextFormatFlags.Right,
            ContentAlignment.BottomLeft => TextFormatFlags.Bottom,
            ContentAlignment.BottomCenter => TextFormatFlags.Bottom | TextFormatFlags.HorizontalCenter,
            ContentAlignment.BottomRight => TextFormatFlags.Bottom | TextFormatFlags.Right,
            _ => TextFormatFlags.Left
        };
        if (AutoEllipsis)
            flags |= TextFormatFlags.EndEllipsis;
        if (!UseMnemonic)
            flags |= TextFormatFlags.NoPrefix;
        if (RightToLeft == RightToLeft.Yes)
            flags |= TextFormatFlags.RightToLeft;

        TextRenderer.DrawText(e.Graphics, Text, Font, bounds,
            Enabled ? ForeColor : SystemColors.GrayText, flags);
    }
}
