using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace DellSmmFanControl.Controls;

public sealed class ToggleSwitch : Control
{
    private bool _checked;
    private bool _spacePressed;
    private readonly System.Windows.Forms.Timer _animation = new() { Interval = 16 };
    private float _position;
    private float _animationFrom;
    private long _animationStarted;
    private const double AnimationDurationSeconds = 0.18;

    [DefaultValue(false)]
    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value)
                return;
            AdvanceAnimation();
            _checked = value;
            _animationFrom = _position;
            _animationStarted = Stopwatch.GetTimestamp();
            if (Visible && IsHandleCreated && !DesignMode)
                _animation.Start();
            else
                FinishAnimation();
            Invalidate();
            AccessibilityNotifyClients(AccessibleEvents.StateChange, -1);
        }
    }

    public ToggleSwitch()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
            ControlStyles.Selectable | ControlStyles.StandardClick, true);
        AutoSize = true;
        TabStop = true;
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.CheckButton;
        _animation.Tick += (_, _) =>
        {
            AdvanceAnimation();
            Invalidate();
        };
    }

    private void AdvanceAnimation()
    {
        if (!_animation.Enabled)
            return;
        double elapsed = Stopwatch.GetElapsedTime(_animationStarted).TotalSeconds / AnimationDurationSeconds;
        float t = (float)Math.Clamp(elapsed, 0, 1);
        float eased = t * t * (3 - 2 * t);
        _position = _animationFrom + ((_checked ? 1F : 0F) - _animationFrom) * eased;
        if (t >= 1)
            FinishAnimation();
    }

    private void FinishAnimation()
    {
        _animation.Stop();
        _position = _checked ? 1F : 0F;
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (!Visible && !Disposing && !IsDisposed)
            FinishAnimation();
    }

    private static Color Blend(Color from, Color to, float amount) => Color.FromArgb(
        (int)Math.Round(from.R + (to.R - from.R) * amount),
        (int)Math.Round(from.G + (to.G - from.G) * amount),
        (int)Math.Round(from.B + (to.B - from.B) * amount));

    public override Size GetPreferredSize(Size proposedSize)
    {
        float scale = DeviceDpi / 96F;
        Size label = TextRenderer.MeasureText(Text, Font, Size.Empty,
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        return new Size(label.Width + (int)Math.Ceiling(61 * scale),
            Math.Max(label.Height, (int)Math.Ceiling(35 * scale)));
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        float scale = DeviceDpi / 96F;
        float width = 51 * scale;
        float height = 31 * scale;
        var track = new RectangleF(ClientSize.Width - width - scale,
            (ClientSize.Height - height) / 2F, width, height);
        Graphics graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        using var outline = new GraphicsPath();
        outline.AddArc(track.Left, track.Top, track.Height, track.Height, 90, 180);
        outline.AddArc(track.Right - track.Height, track.Top, track.Height, track.Height, 270, 180);
        outline.CloseFigure();
        Color trackColor = Blend(Color.FromArgb(233, 233, 234), Color.FromArgb(52, 199, 89), _position);
        if (!Enabled)
            trackColor = Blend(trackColor, BackColor, 0.45F);
        using var trackBrush = new SolidBrush(trackColor);
        graphics.FillPath(trackBrush, outline);

        float diameter = 27 * scale;
        float knobLeft = track.Left + 2 * scale + (width - height) * _position;
        var knob = new RectangleF(knobLeft, track.Top + (track.Height - diameter) / 2, diameter, diameter);
        using var shadowPath = new GraphicsPath();
        shadowPath.AddEllipse(knob.X - 3 * scale, knob.Y - 2 * scale,
            knob.Width + 6 * scale, knob.Height + 6 * scale);
        using var shadow = new PathGradientBrush(shadowPath)
        {
            CenterColor = Color.FromArgb(38, Color.Black),
            SurroundColors = [Color.FromArgb(0, Color.Black)],
            FocusScales = new PointF(0.65F, 0.65F)
        };
        graphics.FillPath(shadow, shadowPath);
        graphics.FillEllipse(Brushes.White, knob);
        using var knobBorder = new Pen(Color.FromArgb(18, Color.Black), 0.5F * scale);
        graphics.DrawEllipse(knobBorder, knob);

        var labelBounds = new Rectangle(0, 0, Math.Max(0, (int)(track.Left - 8 * scale)), ClientSize.Height);
        TextRenderer.DrawText(graphics, Text, Font, labelBounds,
            Enabled ? ForeColor : SystemColors.GrayText,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
            TextFormatFlags.NoPadding | (ShowKeyboardCues ? 0 : TextFormatFlags.HidePrefix));
        if (Focused && ShowFocusCues)
            ControlPaint.DrawFocusRectangle(graphics, Rectangle.Inflate(ClientRectangle, -1, -1));
    }

    protected override void OnClick(EventArgs e)
    {
        if (!Enabled)
            return;
        Focus();
        Checked = !Checked;
        base.OnClick(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Space)
        {
            _spacePressed = true;
            e.Handled = e.SuppressKeyPress = true;
        }
        base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Space && _spacePressed)
        {
            _spacePressed = false;
            e.Handled = e.SuppressKeyPress = true;
            OnClick(EventArgs.Empty);
        }
        base.OnKeyUp(e);
    }

    protected override AccessibleObject CreateAccessibilityInstance() => new SwitchAccessibleObject(this);

    private sealed class SwitchAccessibleObject(ToggleSwitch owner) : ControlAccessibleObject(owner)
    {
        public override AccessibleStates State => base.State | (owner.Checked ? AccessibleStates.Checked : 0);
        public override string? DefaultAction => owner.Checked ? "Turn off" : "Turn on";
        public override void DoDefaultAction() => owner.OnClick(EventArgs.Empty);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Invalidate();
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        _spacePressed = false;
        base.OnLostFocus(e);
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _animation.Dispose();
        base.Dispose(disposing);
    }
}
