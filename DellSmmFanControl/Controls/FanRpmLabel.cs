using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace DellSmmFanControl.Controls;

public sealed class FanRpmLabel : Label
{
    private readonly System.Windows.Forms.Timer _animation = new() { Interval = 16 };
    private readonly GraphicsPath _blade = CreateBlade();
    private int? _rpm;
    private int _rotationDegreesPerSecond = 360;
    private double _angle;
    private long _lastTick;

    [DefaultValue(360)]
    public int RotationDegreesPerSecond
    {
        get => _rotationDegreesPerSecond;
        set
        {
            if (_rotationDegreesPerSecond == value)
                return;
            AdvanceAnimation();
            _rotationDegreesPerSecond = Math.Max(0, value);
            UpdateAnimation();
        }
    }

    [DefaultValue(null)]
    public int? Rpm
    {
        get => _rpm;
        set
        {
            if (_rpm == value)
                return;
            AdvanceAnimation();
            _rpm = value;
            Text = value is int rpm ? $"{rpm:N0} RPM" : "— RPM";
            UpdateAnimation();
            Invalidate();
        }
    }

    public FanRpmLabel()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        _animation.Tick += (_, _) =>
        {
            AdvanceAnimation();
            Invalidate(new Rectangle(0, 0, (int)Math.Ceiling(26 * DeviceDpi / 96F), Height));
        };
    }

    private static GraphicsPath CreateBlade()
    {
        // Coordinates are relative to the shaft at (0, 0), in a 64-unit square.
        // All four blades use this exact path, rather than separate outlines.
        var blade = new GraphicsPath();
        blade.AddBezier(-5F, -7F, -7F, -9F, -7F, -16F, -7F, -20F);
        blade.AddBezier(-7F, -20F, -7F, -26F, 0F, -28F, 7F, -25F);
        blade.AddBezier(7F, -25F, 12F, -23F, 18F, -19F, 15F, -16F);
        blade.AddBezier(15F, -16F, 12F, -13F, 5F, -12F, 3F, -7F);
        blade.AddBezier(3F, -7F, 1F, -5F, -2F, -8F, -5F, -7F);
        blade.CloseFigure();
        return blade;
    }

    private void AdvanceAnimation()
    {
        long now = Stopwatch.GetTimestamp();
        if (_animation.Enabled && _lastTick != 0 && _rpm is > 0)
        {
            _angle = (_angle + _rotationDegreesPerSecond * Stopwatch.GetElapsedTime(_lastTick, now).TotalSeconds) % 360;
        }
        _lastTick = now;
    }

    private void UpdateAnimation()
    {
        bool animate = Visible && _rpm is > 0 && _rotationDegreesPerSecond > 0;
        if (_animation.Enabled == animate)
            return;
        _lastTick = Stopwatch.GetTimestamp();
        _animation.Enabled = animate;
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        UpdateAnimation();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        float scale = DeviceDpi / 96F;
        Graphics graphics = e.Graphics;
        GraphicsState state = graphics.Save();
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TranslateTransform(12 * scale, ClientSize.Height / 2F);
        float iconScale = Math.Max(0.01F, Math.Min(scale, (ClientSize.Height - 2 * scale) / 24F));
        graphics.ScaleTransform(iconScale * 24F / 64F, iconScale * 24F / 64F);
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        for (int bladeIndex = 0; bladeIndex < 4; bladeIndex++)
        {
            GraphicsState bladeState = graphics.Save();
            graphics.RotateTransform((float)_angle + bladeIndex * 90F);
            graphics.FillPath(Brushes.Black, _blade);
            graphics.Restore(bladeState);
        }
        // A stationary circle shares the blades' origin exactly.
        graphics.FillEllipse(Brushes.Black, -5.25F, -5.25F, 10.5F, 10.5F);
        graphics.Restore(state);

        int textLeft = (int)Math.Ceiling(28 * scale);
        var bounds = new Rectangle(textLeft, 0, Math.Max(0, ClientSize.Width - textLeft), ClientSize.Height);
        const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine |
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left;
        Size measured = TextRenderer.MeasureText(Text, Font, Size.Empty, flags);
        float ratio = Math.Min(1F, Math.Min(bounds.Width / (float)Math.Max(1, measured.Width),
            bounds.Height / (float)Math.Max(1, measured.Height)));
        if (ratio > 0 && ratio < 1)
        {
            using var fitted = new Font(Font.FontFamily, Font.Size * ratio, Font.Style, Font.Unit);
            TextRenderer.DrawText(graphics, Text, fitted, bounds, ForeColor, flags);
        }
        else if (ratio >= 1)
            TextRenderer.DrawText(graphics, Text, Font, bounds, ForeColor, flags);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _animation.Dispose();
            _blade.Dispose();
        }
        base.Dispose(disposing);
    }
}
