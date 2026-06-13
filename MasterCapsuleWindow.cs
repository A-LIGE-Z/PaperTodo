using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace PaperTodo;

// Standalone "collapse-all" master capsule. It is permanently pinned at deep-capsule
// slot 0 (real capsules shift down to slot 1..N). Clicking it toggles whether the
// real capsules are retracted behind it. It owns only its own pill chrome and the
// peek/slide animation that mirrors the real capsules; the controller drives the
// retract/release of the real capsule windows.
public sealed class MasterCapsuleWindow : Window
{
    private const double ShellHeight = 30;

    // Compact internal metrics controlling how tightly the glyph + label sit inside the pill.
    // The label is always shown in full; only the right padding is tucked past the screen edge.
    private const double MasterLeftPadding = 5;
    private const double MasterGlyphGap = 4;
    private const double MasterRightPadding = 10;
    private const double MasterGlyphFontSize = 12;
    private const double MasterLabelFontSize = 11;
    // Gap shown after the label before the screen edge cuts off the trailing padding.
    private const double MasterPeekRightGap = 2;

    private readonly AppController _controller;

    private Border _pill = null!;
    private Border _hoverOverlay = null!;
    private TextBlock _glyph = null!;
    private TextBlock _label = null!;

    private bool _isHovering;
    private bool _suppressGeometrySave = true; // master capsule position is always derived, never persisted
    private int _count;
    private bool _active;

    private static readonly DependencyProperty AnimatedLeftProperty =
        DependencyProperty.Register(
            nameof(AnimatedLeft),
            typeof(double),
            typeof(MasterCapsuleWindow),
            new PropertyMetadata(double.NaN, OnAnimatedLeftChanged));

    private double AnimatedLeft
    {
        get => (double)GetValue(AnimatedLeftProperty);
        set => SetValue(AnimatedLeftProperty, value);
    }

    public MasterCapsuleWindow(AppController controller)
    {
        _controller = controller;
        ConfigureWindow();
        BuildContent();
        UpdateToolTipSetting();
        // Clicking the pill must never pull foreground focus: activating this window would
        // deactivate whatever app was in front, forcing it to repaint — the click "flash".
        // WS_EX_NOACTIVATE makes the window unable to become the active/foreground window,
        // so the click toggles collapse-all without disturbing the current foreground app.
        SourceInitialized += (_, _) => ApplyNoActivateStyle();
    }

    private void ApplyNoActivateStyle()
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var exStyle = GetWindowLong(handle, GwlExStyle);
        SetWindowLong(handle, GwlExStyle, exStyle | WsExNoActivate);
    }

    private void ConfigureWindow()
    {
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        FontFamily = new FontFamily("Segoe UI");
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        Width = PaperLayoutDefaults.CapsuleWidth;
        Height = PaperLayoutDefaults.CapsuleHeight;
        // Don't steal foreground when first shown — activating would force every other
        // paper window to repaint, which reads as a whole-app flash.
        ShowActivated = false;
        // Start invisible; ShowPlaced() positions us first, then fades in, so we never
        // flash for one frame at the top-left (the default NaN → 0,0 position).
        Opacity = 0;
        RefreshEffectiveTopmost();
    }

    private void BuildContent()
    {
        var host = new Grid { Background = Brushes.Transparent, ClipToBounds = false };

        _pill = new Border
        {
            Margin = new Thickness(8),
            CornerRadius = new CornerRadius(DeepCapsuleLayout.CornerRadius),
            BorderThickness = new Thickness(1),
            Background = Theme.PaperBrush,
            BorderBrush = Theme.PaperBorderBrush,
            SnapsToDevicePixels = true,
            Cursor = System.Windows.Input.Cursors.Hand,
            Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 2, Opacity = 0.18 }
        };

        // The pill background stays opaque (PaperBrush) at all times. Hover tint is a separate
        // overlay layered on top — the same shape as the pill — so the (semi-transparent)
        // HoverBrush never replaces the only opaque layer and let the desktop show through.
        var content = new Grid();

        _hoverOverlay = new Border
        {
            CornerRadius = new CornerRadius(DeepCapsuleLayout.CornerRadius),
            Background = Brushes.Transparent,
            SnapsToDevicePixels = true
        };
        content.Children.Add(_hoverOverlay);

        var stack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            // Hug the left edge; the master pill is never truncated, so content sits flush left.
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(MasterLeftPadding, 0, MasterRightPadding, 0)
        };

        _glyph = new TextBlock
        {
            Text = "▾",
            Foreground = Theme.TextBrush,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        stack.Children.Add(_glyph);

        _label = new TextBlock
        {
            Text = Strings.Get("CapsuleCollapseAllLabel"),
            Foreground = Theme.WeakTextBrush,
            FontSize = 11,
            Margin = new Thickness(MasterGlyphGap, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        stack.Children.Add(_label);
        content.Children.Add(stack);

        _pill.Child = content;
        host.Children.Add(_pill);
        Content = host;

        _pill.MouseEnter += (_, _) =>
        {
            _hoverOverlay.Background = Theme.HoverBrush;
            SetHover(true);
        };
        _pill.MouseLeave += (_, _) =>
        {
            _hoverOverlay.Background = Brushes.Transparent;
            SetHover(false);
        };
        _pill.MouseLeftButtonUp += (_, e) =>
        {
            _controller.ToggleCapsuleCollapseAllActive();
            e.Handled = true;
        };
    }

    public void UpdateTheme()
    {
        // Pill background is always the opaque PaperBrush; the hover tint lives on the overlay.
        _pill.Background = Theme.PaperBrush;
        _pill.BorderBrush = Theme.PaperBorderBrush;
        _hoverOverlay.Background = _isHovering ? Theme.HoverBrush : Brushes.Transparent;
        _glyph.Foreground = Theme.TextBrush;
        _label.Foreground = Theme.WeakTextBrush;
    }

    public void UpdateToolTipSetting()
    {
        ToolTipPreferences.Apply(this, _controller.State.EnableToolTips);
    }

    public void RefreshEffectiveTopmost()
    {
        Topmost = !_controller.SuppressTopmostForFullscreenForeground;
    }

    // count = number of real capsules behind the master; active = whether they are retracted.
    public void UpdateState(int count, bool active, bool animate)
    {
        _count = count;
        _active = active;
        ApplyStateVisuals();

        Width = CapsuleWindowWidth();
        Height = PaperLayoutDefaults.CapsuleHeight;

        MoveToTarget(animate);
        RefreshEffectiveTopmost();
    }

    private void ApplyStateVisuals()
    {
        _glyph.Text = _active ? "▸" : "▾";
        _label.Text = _active
            ? string.Format(CultureInfo.CurrentUICulture, Strings.Get("CapsuleCollapseAllCountFormat"), _count)
            : Strings.Get("CapsuleCollapseAllLabel");
        _pill.ToolTip = _active
            ? Strings.Get("CapsuleCollapseAllCollapsedTip")
            : Strings.Get("CapsuleCollapseAllExpandedTip");
    }

    private void SetHover(bool hovering)
    {
        // Hover only changes the pill background (handled in the MouseEnter/Leave handlers);
        // the master pill does not move, so there is nothing to reposition here.
        _isHovering = hovering;
    }

    private double CapsuleWindowWidth()
    {
        // glyph + gap + label + left/right paddings + chrome margins. Both pieces are
        // measured the same way so the pill hugs the actual rendered content.
        var glyphWidth = MeasureText(_glyph.Text, MasterGlyphFontSize, FontWeights.SemiBold);
        var textWidth = MeasureText(_label.Text, MasterLabelFontSize, FontWeights.Normal);
        var shellWidth = Math.Ceiling(MasterLeftPadding + glyphWidth + MasterGlyphGap + textWidth + MasterRightPadding);
        return shellWidth + 16;
    }

    private double MeasureText(string text, double fontSize, FontWeight weight)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        try
        {
            var formatted = new FormattedText(
                text,
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, weight, FontStretches.Normal),
                fontSize,
                Theme.WeakTextBrush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            return formatted.WidthIncludingTrailingWhitespace;
        }
        catch
        {
            return text.Length * fontSize;
        }
    }

    private void MoveToTarget(bool animate)
    {
        var area = DeepCapsuleLayout.WorkArea;
        var width = CapsuleWindowWidth();
        // Master pill: the LABEL is never clipped, but the right padding + right chrome margin
        // are tucked past the screen edge so it reads as "docked" like the real capsules. The
        // screen edge cuts just after the text (+ a hair of breathing room), hiding only the
        // trailing padding — never any glyph.
        var visibleWidth = 8 + MasterLeftPadding
            + MeasureText(_glyph.Text, MasterGlyphFontSize, FontWeights.SemiBold)
            + MasterGlyphGap
            + MeasureText(_label.Text, MasterLabelFontSize, FontWeights.Normal)
            + MasterPeekRightGap;
        var targetLeft = RoundX(area.Right - visibleWidth);
        var targetTop = RoundY(DeepCapsuleLayout.TopForIndex(0));

        MoveWithoutSave(() =>
        {
            Width = width;
            Height = PaperLayoutDefaults.CapsuleHeight;
            Top = targetTop;
        });

        if (!animate)
        {
            BeginAnimation(AnimatedLeftProperty, null);
            MoveWithoutSave(() => Left = targetLeft);
            return;
        }

        var currentLeft = double.IsNaN(Left) || double.IsInfinity(Left) ? targetLeft : RoundX(Left);
        if (Math.Abs(currentLeft - targetLeft) < 0.5)
        {
            BeginAnimation(AnimatedLeftProperty, null);
            MoveWithoutSave(() => Left = targetLeft);
            return;
        }

        var anim = new DoubleAnimation
        {
            From = currentLeft,
            To = targetLeft,
            Duration = TimeSpan.FromMilliseconds(_isHovering ? DeepCapsuleLayout.SlideOutMilliseconds : DeepCapsuleLayout.SlideInMilliseconds),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        anim.Completed += (_, _) =>
        {
            BeginAnimation(AnimatedLeftProperty, null);
            MoveWithoutSave(() => Left = targetLeft);
        };
        BeginAnimation(AnimatedLeftProperty, anim, HandoffBehavior.SnapshotAndReplace);
    }

    // The resting Top of the master, used as the retract/release anchor for real capsules.
    public double AnchorTop => RoundY(DeepCapsuleLayout.TopForIndex(0));

    private static void OnAnimatedLeftChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not MasterCapsuleWindow w || e.NewValue is not double left || double.IsNaN(left) || double.IsInfinity(left))
        {
            return;
        }

        w.MoveWithoutSave(() => w.Left = w.RoundX(left));
    }

    private void MoveWithoutSave(Action move)
    {
        var was = _suppressGeometrySave;
        _suppressGeometrySave = true;
        try
        {
            move();
        }
        finally
        {
            _suppressGeometrySave = was;
        }
    }

    private double RoundX(double value)
    {
        var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        return Round(value, scale);
    }

    private double RoundY(double value)
    {
        var scale = VisualTreeHelper.GetDpi(this).DpiScaleY;
        return Round(value, scale);
    }

    private static double Round(double value, double scale)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || scale <= 0)
        {
            return value;
        }

        return Math.Round(value * scale, MidpointRounding.AwayFromZero) / scale;
    }

    // First-time show: position at the final edge-aligned spot BEFORE becoming visible,
    // then fade in. This avoids both the top-left flash and the slide-in from the wrong place.
    public void ShowPlaced(int count, bool active)
    {
        _count = count;
        _active = active;
        ApplyStateVisuals();

        Width = CapsuleWindowWidth();
        Height = PaperLayoutDefaults.CapsuleHeight;
        MoveToTarget(animate: false);

        Show();
        RefreshEffectiveTopmost();

        var fadeIn = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(160),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        fadeIn.Completed += (_, _) =>
        {
            BeginAnimation(OpacityProperty, null);
            Opacity = 1;
        };
        BeginAnimation(OpacityProperty, fadeIn);
    }

    public void CloseForReal()
    {
        BeginAnimation(AnimatedLeftProperty, null);
        Close();
    }

    private const int GwlExStyle = -20;
    private const int WsExNoActivate = 0x08000000;

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
