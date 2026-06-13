using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;

namespace PaperTodo;

// Visual slot tag for an expanded paper that still owns a deep-capsule slot.
// Unlike the collapsed capsule, this tag stays mostly visible and can be clicked to
// bring the expanded paper back to front; it keeps the slot identity without acting
// like a second full window surface.
public sealed class DeepCapsuleSlotWindow : Window
{
    private const double LeftPadding = 6;
    private const double IconGap = 4;
    private const double RightPadding = 2;
    private const double CloseWidth = 30;
    private const double IconFontSize = 13;
    private const double LabelFontSize = 11;
    private const double ExpandedVisibleTrim = 4;

    private readonly AppController _controller;
    private readonly PaperData _paper;

    private Border _pill = null!;
    private Border _hoverOverlay = null!;
    private Border _leftArea = null!;
    private Border _closeArea = null!;
    private TextBlock _closeGlyph = null!;
    private TextBlock _label = null!;
    private bool _suppressGeometrySave = true;

    private static readonly DependencyProperty AnimatedLeftProperty =
        DependencyProperty.Register(
            nameof(AnimatedLeft),
            typeof(double),
            typeof(DeepCapsuleSlotWindow),
            new PropertyMetadata(double.NaN, OnAnimatedLeftChanged));

    private static readonly DependencyProperty AnimatedTopProperty =
        DependencyProperty.Register(
            nameof(AnimatedTop),
            typeof(double),
            typeof(DeepCapsuleSlotWindow),
            new PropertyMetadata(double.NaN, OnAnimatedTopChanged));

    private double AnimatedLeft
    {
        get => (double)GetValue(AnimatedLeftProperty);
        set => SetValue(AnimatedLeftProperty, value);
    }

    private double AnimatedTop
    {
        get => (double)GetValue(AnimatedTopProperty);
        set => SetValue(AnimatedTopProperty, value);
    }

    public DeepCapsuleSlotWindow(AppController controller, PaperData paper)
    {
        _controller = controller;
        _paper = paper;
        ConfigureWindow();
        BuildContent();
        UpdateToolTipSetting();
        SourceInitialized += (_, _) => ApplyNoActivateStyle();
    }

    private void ConfigureWindow()
    {
        ShowInTaskbar = false;
        ShowActivated = false;
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
            Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 2, Opacity = 0.18 }
        };

        var content = new Grid();

        _hoverOverlay = new Border
        {
            CornerRadius = new CornerRadius(DeepCapsuleLayout.CornerRadius),
            Background = Brushes.Transparent,
            SnapsToDevicePixels = true
        };
        content.Children.Add(_hoverOverlay);

        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _leftArea = new Border
        {
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(DeepCapsuleLayout.CornerRadius, 0, 0, DeepCapsuleLayout.CornerRadius),
            Cursor = System.Windows.Input.Cursors.Hand
        };

        var leftStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(LeftPadding, 0, 0, 0)
        };

        var iconText = new TextBlock
        {
            Text = _paper.Type == PaperTypes.Note ? "✎" : "✓",
            Foreground = Theme.TextBrush,
            FontFamily = NoteTypography.FontFamily,
            FontSize = IconFontSize,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        leftStack.Children.Add(iconText);

        _label = new TextBlock
        {
            Text = _controller.PaperCapsuleTitle(_paper),
            Foreground = Theme.WeakTextBrush,
            FontFamily = NoteTypography.FontFamily,
            FontSize = LabelFontSize,
            Margin = new Thickness(IconGap, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        leftStack.Children.Add(_label);

        _leftArea.Child = leftStack;
        Grid.SetColumn(_leftArea, 0);
        root.Children.Add(_leftArea);

        _closeGlyph = new TextBlock
        {
            Text = "×",
            Foreground = Theme.WeakTextBrush,
            FontSize = 21,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.86
        };

        _closeArea = new Border
        {
            Width = CloseWidth,
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(0, DeepCapsuleLayout.CornerRadius, DeepCapsuleLayout.CornerRadius, 0),
            Cursor = System.Windows.Input.Cursors.Hand,
            Child = new Grid
            {
                Margin = new Thickness(-5, 0, 0, 0),
                Children =
                {
                    _closeGlyph
                }
            }
        };
        Grid.SetColumn(_closeArea, 1);
        root.Children.Add(_closeArea);

        content.Children.Add(root);
        _pill.Child = content;
        host.Children.Add(_pill);
        Content = host;

        _pill.MouseEnter += (_, _) =>
        {
            _hoverOverlay.Background = Theme.HoverBrush;
        };
        _pill.MouseLeave += (_, _) =>
        {
            _hoverOverlay.Background = Brushes.Transparent;
            _closeArea.Opacity = 1.0;
            _closeGlyph.Foreground = Theme.WeakTextBrush;
        };
        _leftArea.MouseEnter += (_, _) => _leftArea.Background = Theme.HoverBrush;
        _leftArea.MouseLeave += (_, _) => _leftArea.Background = Brushes.Transparent;
        _leftArea.MouseLeftButtonUp += (_, e) =>
        {
            _controller.BringPaperToFront(_paper);
            e.Handled = true;
        };
        _closeArea.MouseEnter += (_, _) =>
        {
            _closeArea.Background = Theme.HoverBrush;
            _closeGlyph.Foreground = Theme.TextBrush;
        };
        _closeArea.MouseLeave += (_, _) =>
        {
            _closeArea.Background = Brushes.Transparent;
            _closeArea.Opacity = 1.0;
            _closeGlyph.Foreground = Theme.WeakTextBrush;
        };
        _closeArea.MouseLeftButtonDown += (_, e) =>
        {
            _closeArea.Opacity = 0.72;
            e.Handled = true;
        };
        _closeArea.MouseLeftButtonUp += (_, e) =>
        {
            _closeArea.Opacity = 1.0;
            _controller.HidePaper(_paper);
            e.Handled = true;
        };
    }

    public void RefreshEffectiveTopmost()
    {
        Topmost = !_controller.SuppressTopmostForFullscreenForeground;
    }

    public void UpdateTheme()
    {
        _pill.Background = Theme.PaperBrush;
        _pill.BorderBrush = Theme.PaperBorderBrush;
        _hoverOverlay.Background = Brushes.Transparent;
        _leftArea.Background = Brushes.Transparent;
        _closeArea.Background = Brushes.Transparent;
        _label.Foreground = Theme.WeakTextBrush;
        _closeGlyph.Foreground = Theme.WeakTextBrush;
    }

    public void UpdateToolTipSetting()
    {
        ToolTipPreferences.Apply(this, _controller.State.EnableToolTips);
    }

    public void UpdateTitle()
    {
        _label.Text = _controller.PaperCapsuleTitle(_paper);
    }

    public double RestingVisibleWidth => Math.Clamp(CapsuleWindowWidth() - ExpandedVisibleTrim, 72, CapsuleWindowWidth());

    public void ShowPlaced(int index, int visualOffset)
    {
        Width = CapsuleWindowWidth();
        Height = PaperLayoutDefaults.CapsuleHeight;
        MoveToSlot(index, visualOffset, animate: false);
        Show();
        RefreshEffectiveTopmost();

        var fadeIn = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(140),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        fadeIn.Completed += (_, _) =>
        {
            BeginAnimation(OpacityProperty, null);
            Opacity = 1;
        };
        BeginAnimation(OpacityProperty, fadeIn);
    }

    public void UpdatePlacement(int index, int visualOffset, bool animate)
    {
        Width = CapsuleWindowWidth();
        Height = PaperLayoutDefaults.CapsuleHeight;
        UpdateTitle();
        MoveToSlot(index, visualOffset, animate);
        RefreshEffectiveTopmost();
        if (Math.Abs(Opacity - 1.0) > 0.001)
        {
            BeginAnimation(OpacityProperty, null);
            Opacity = 1;
        }
    }

    private void MoveToSlot(int index, int visualOffset, bool animate)
    {
        var area = DeepCapsuleLayout.WorkArea;
        var width = CapsuleWindowWidth();
        var visibleWidth = RestingVisibleWidth;
        var targetLeft = RoundX(area.Right - visibleWidth);
        var targetTop = RoundY(DeepCapsuleLayout.TopForIndex(index + visualOffset));

        MoveWithoutSave(() =>
        {
            Width = width;
            Height = PaperLayoutDefaults.CapsuleHeight;
        });

        if (!animate)
        {
            BeginAnimation(AnimatedLeftProperty, null);
            BeginAnimation(AnimatedTopProperty, null);
            MoveWithoutSave(() =>
            {
                Left = targetLeft;
                Top = targetTop;
            });
            return;
        }

        AnimateTo(targetLeft, targetTop);
    }

    private void AnimateTo(double targetLeft, double targetTop)
    {
        var currentLeft = double.IsNaN(Left) || double.IsInfinity(Left) ? targetLeft : RoundX(Left);
        var currentTop = double.IsNaN(Top) || double.IsInfinity(Top) ? targetTop : RoundY(Top);

        var easeOut = new CubicEase { EasingMode = EasingMode.EaseOut };

        if (Math.Abs(currentLeft - targetLeft) >= 0.5)
        {
            var leftAnim = new DoubleAnimation
            {
                From = currentLeft,
                To = targetLeft,
                Duration = TimeSpan.FromMilliseconds(DeepCapsuleLayout.SlotMoveMilliseconds),
                EasingFunction = easeOut
            };
            leftAnim.Completed += (_, _) =>
            {
                BeginAnimation(AnimatedLeftProperty, null);
                MoveWithoutSave(() => Left = targetLeft);
            };
            BeginAnimation(AnimatedLeftProperty, leftAnim, HandoffBehavior.SnapshotAndReplace);
        }
        else
        {
            BeginAnimation(AnimatedLeftProperty, null);
            MoveWithoutSave(() => Left = targetLeft);
        }

        if (Math.Abs(currentTop - targetTop) >= 0.5)
        {
            var topAnim = new DoubleAnimation
            {
                From = currentTop,
                To = targetTop,
                Duration = TimeSpan.FromMilliseconds(DeepCapsuleLayout.SlotMoveMilliseconds),
                EasingFunction = easeOut
            };
            topAnim.Completed += (_, _) =>
            {
                BeginAnimation(AnimatedTopProperty, null);
                MoveWithoutSave(() => Top = targetTop);
            };
            BeginAnimation(AnimatedTopProperty, topAnim, HandoffBehavior.SnapshotAndReplace);
        }
        else
        {
            BeginAnimation(AnimatedTopProperty, null);
            MoveWithoutSave(() => Top = targetTop);
        }
    }

    private double CapsuleWindowWidth()
    {
        var iconWidth = MeasureText(_paper.Type == PaperTypes.Note ? "✎" : "✓", IconFontSize, FontWeights.SemiBold);
        var textWidth = MeasureText(_controller.PaperCapsuleTitle(_paper), LabelFontSize, FontWeights.Normal);
        var shellWidth = Math.Ceiling(LeftPadding + iconWidth + IconGap + textWidth + CloseWidth + RightPadding);
        return Math.Max(PaperLayoutDefaults.CapsuleWidth, shellWidth + 16);
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
                new Typeface(NoteTypography.FontFamily, FontStyles.Normal, weight, FontStretches.Normal),
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

    public void CloseForReal()
    {
        BeginAnimation(AnimatedLeftProperty, null);
        BeginAnimation(AnimatedTopProperty, null);
        Close();
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
        return Round(value, VisualTreeHelper.GetDpi(this).DpiScaleX);
    }

    private double RoundY(double value)
    {
        return Round(value, VisualTreeHelper.GetDpi(this).DpiScaleY);
    }

    private static double Round(double value, double scale)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || scale <= 0)
        {
            return value;
        }

        return Math.Round(value * scale, MidpointRounding.AwayFromZero) / scale;
    }

    private static void OnAnimatedLeftChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DeepCapsuleSlotWindow w || e.NewValue is not double left || double.IsNaN(left) || double.IsInfinity(left))
        {
            return;
        }

        w.MoveWithoutSave(() => w.Left = w.RoundX(left));
    }

    private static void OnAnimatedTopChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DeepCapsuleSlotWindow w || e.NewValue is not double top || double.IsNaN(top) || double.IsInfinity(top))
        {
            return;
        }

        w.MoveWithoutSave(() => w.Top = w.RoundY(top));
    }

    private const int GwlExStyle = -20;
    private const int WsExNoActivate = 0x08000000;
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
