using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Text.RegularExpressions;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using Color = System.Windows.Media.Color;
using ContextMenu = System.Windows.Controls.ContextMenu;
using Control = System.Windows.Controls.Control;
using Cursor = System.Windows.Input.Cursor;
using Cursors = System.Windows.Input.Cursors;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MenuItem = System.Windows.Controls.MenuItem;
using MessageBox = System.Windows.MessageBox;
using Orientation = System.Windows.Controls.Orientation;
using Point = System.Windows.Point;
using Separator = System.Windows.Controls.Separator;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using TextBox = System.Windows.Controls.TextBox;
using VerticalAlignment = System.Windows.VerticalAlignment;
using WpfMenuItem = System.Windows.Controls.MenuItem;

namespace PaperTodo;

public sealed partial class PaperWindow : Window
{
    [GeneratedRegex(@"^\s*[-*+]\s+\[(?: |x|X)\]\s*")]
    private static partial Regex TodoCheckboxCleanRegex();

    [GeneratedRegex(@"^\s*[-*+]\s+")]
    private static partial Regex TodoBulletCleanRegex();

    [GeneratedRegex(@"^\s*\d+[\.)、．]\s*")]
    private static partial Regex TodoNumberCleanRegex();

    [GeneratedRegex(@"^\s*[☐☑✓✔]\s*")]
    private static partial Regex TodoGlyphCleanRegex();
    private readonly PaperData _paper;
    private readonly AppController _controller;

    private Border _paperChrome = null!;
    private readonly Grid _containerGrid = new();
    private readonly Grid _shell = new();
    private readonly ScaleTransform _shellScale = new(1.0, 1.0);
    private Canvas? _dragLayer;
    private StackPanel? _todoPanel;
    private Button? _paperIconButton;
    private Button? _newTodoButton;
    private Button? _newNoteButton;
    private Button? _openMarkdownButton;
    private TextBlock? _titleText;
    private TextBox? _titleEditBox;
    private TextBlock? _textZoomIndicator;
    private UIElement? _noteBodyElement;
    private Border? _capsuleLeftArea;
    private Border? _activeDropRow;
    private Border? _dropIndicatorLine;
    private Border? _appendArea;
    private bool _closeForReal;
    private string? _pendingFocusItemId;
    private readonly Dictionary<string, TodoTextBox> _todoEditors = new();
    private readonly List<Border> _todoRows = new();
    private TodoDragState? _todoDrag;
    private MarkdownTextBox? _noteBox;
    private Action? _showNotePreview;
    private readonly List<List<PaperItem>> _undoStack = new();
    private readonly List<List<PaperItem>> _redoStack = new();
    private const int MaxUndoDepth = 100;
    private string? _activeOriginalItemId;
    private string? _activeOriginalText;
    private bool _isApplyingCollapsedState;
    private Button? _closeButton;
    private Grid _capsuleShell = null!;
    private TextBlock _capsuleLabelText = null!;
    private bool _isMaybeDragging;
    private Point _mouseDownScreenPos;
    private bool _suppressGeometrySave;
    private bool _isDeepCapsulePlaced;
    private bool _isDeepCapsuleHovering;
    private int _deepCapsuleIndex = -1;
    private double _startTransitionWidth;
    private double _startTransitionHeight;
    private double _targetTransitionWidth;
    private double _targetTransitionHeight;
    private double _transitionBaseWidth;
    private double _transitionBaseHeight;
    private bool _isTransitionVisualsActive;
    private bool _isEditingTitle;
    private bool _isCoercingTitleEditText;
    private const double DeepCapsuleHoverOutsideOffset = 8;
    private const double DeepCapsuleTopMargin = 8;
    private const double DeepCapsuleStartTopMargin = 48;
    private const double DeepCapsuleGap = 4;
    private const double WindowChromeMargin = 8;
    private const double WindowChromeInset = WindowChromeMargin * 2;
    private const double TitleBarHeight = 23.5;
    private const int CollapseShellFadeMilliseconds = 70;
    private const int CollapseResizeMilliseconds = 150;
    private const int ExpandAnimationMilliseconds = 220;
    private const double ExpandedChromeCornerRadius = 14;
    private const double CapsuleChromeCornerRadius = 18;
    private static readonly object NoteRenderTraceLock = new();

    public bool IsDeepCapsulePlaced => _isDeepCapsulePlaced;
    public bool SuppressGeometrySave => _suppressGeometrySave || _isDeepCapsulePlaced;
    public double DesiredCapsuleWindowWidth => CapsuleWindowWidth();

    private sealed class TodoDragState
    {
        public TodoDragState(string itemId, Border sourceRow, FrameworkElement handle, Point startPoint)
        {
            ItemId = itemId;
            SourceRow = sourceRow;
            Handle = handle;
            StartPoint = startPoint;
        }

        public string ItemId { get; }
        public Border SourceRow { get; }
        public FrameworkElement Handle { get; }
        public Point StartPoint { get; }
        public bool IsDragging { get; set; }
        public string? TargetId { get; set; }
        public DropPlacement TargetPlacement { get; set; } = DropPlacement.After;
        public bool DropAtEnd { get; set; }

        public Border? Ghost { get; set; }
        public Point MouseOffsetInRow { get; set; }
    }

    private enum DropPlacement
    {
        Before,
        After
    }

    private static Brush PaperBrush => Theme.PaperBrush;
    private static Brush PaperBorderBrush => Theme.PaperBorderBrush;
    private static Brush TextBrush => Theme.TextBrush;
    private static Brush WeakTextBrush => Theme.WeakTextBrush;
    private static Brush HoverBrush => Theme.HoverBrush;
    private static Brush MenuHoverBrush => Theme.HoverBrush;

    private static readonly Brush LightDropIndicatorBgBrush = FrozenBrush(Color.FromArgb(12, 126, 84, 34));
    private static readonly Brush DarkDropIndicatorBgBrush = FrozenBrush(Color.FromArgb(12, 166, 140, 104));
    private static Brush DropIndicatorBgBrush => Theme.IsDark ? DarkDropIndicatorBgBrush : LightDropIndicatorBgBrush;

    private static readonly Brush LightDropIndicatorBrush = FrozenBrush(Color.FromArgb(180, 126, 84, 34));
    private static readonly Brush DarkDropIndicatorBrush = FrozenBrush(Color.FromArgb(180, 166, 140, 104));
    private static Brush DropIndicatorBrush => Theme.IsDark ? DarkDropIndicatorBrush : LightDropIndicatorBrush;

    private static readonly Brush LightAppendDropBrush = FrozenBrush(Color.FromArgb(34, 126, 84, 34));
    private static readonly Brush DarkAppendDropBrush = FrozenBrush(Color.FromArgb(34, 166, 140, 104));
    private static Brush AppendDropBrush => Theme.IsDark ? DarkAppendDropBrush : LightAppendDropBrush;

    private static readonly Brush LightAppendBorderBrush = FrozenBrush(Color.FromArgb(45, 120, 92, 48));
    private static readonly Brush DarkAppendBorderBrush = FrozenBrush(Color.FromArgb(45, 230, 223, 211));
    private static Brush AppendBorderBrush => Theme.IsDark ? DarkAppendBorderBrush : LightAppendBorderBrush;

    private static readonly Brush LightAppendBgBrush = FrozenBrush(Color.FromArgb(12, 120, 92, 48));
    private static readonly Brush DarkAppendBgBrush = FrozenBrush(Color.FromArgb(12, 230, 223, 211));
    private static Brush AppendBgBrush => Theme.IsDark ? DarkAppendBgBrush : LightAppendBgBrush;

    private static readonly Brush LightAppendHoverBgBrush = FrozenBrush(Color.FromArgb(26, 120, 92, 48));
    private static readonly Brush DarkAppendHoverBgBrush = FrozenBrush(Color.FromArgb(26, 230, 223, 211));
    private static Brush AppendHoverBgBrush => Theme.IsDark ? DarkAppendHoverBgBrush : LightAppendHoverBgBrush;

    private static readonly Brush LightCheckBoxBorderBrush = FrozenBrush(Color.FromRgb(180, 160, 120));
    private static readonly Brush DarkCheckBoxBorderBrush = FrozenBrush(Color.FromRgb(110, 100, 85));
    private static Brush CheckBoxBorderBrush => Theme.IsDark ? DarkCheckBoxBorderBrush : LightCheckBoxBorderBrush;

    private static readonly Brush LightTrashBgBrush = FrozenBrush(Color.FromArgb(12, 176, 90, 70));
    private static readonly Brush DarkTrashBgBrush = FrozenBrush(Color.FromArgb(16, 230, 110, 90));
    private static Brush TrashBgBrush => Theme.IsDark ? DarkTrashBgBrush : LightTrashBgBrush;

    private static readonly Brush LightTrashBorderBrush = FrozenBrush(Color.FromArgb(50, 176, 90, 70));
    private static readonly Brush DarkTrashBorderBrush = FrozenBrush(Color.FromArgb(50, 230, 110, 90));
    private static Brush TrashBorderBrush => Theme.IsDark ? DarkTrashBorderBrush : LightTrashBorderBrush;

    private static readonly Brush LightTrashTextBrush = FrozenBrush(Color.FromRgb(176, 90, 70));
    private static readonly Brush DarkTrashTextBrush = FrozenBrush(Color.FromRgb(230, 110, 90));
    private static Brush TrashTextBrush => Theme.IsDark ? DarkTrashTextBrush : LightTrashTextBrush;

    private static readonly Brush LightTrashHoverBgBrush = FrozenBrush(Color.FromArgb(26, 176, 90, 70));
    private static readonly Brush DarkTrashHoverBgBrush = FrozenBrush(Color.FromArgb(32, 230, 110, 90));
    private static Brush TrashHoverBgBrush => Theme.IsDark ? DarkTrashHoverBgBrush : LightTrashHoverBgBrush;

    private static readonly Brush LightTrashHoverBorderBrush = FrozenBrush(Color.FromRgb(176, 90, 70));
    private static readonly Brush DarkTrashHoverBorderBrush = FrozenBrush(Color.FromRgb(230, 110, 90));
    private static Brush TrashHoverBorderBrush => Theme.IsDark ? DarkTrashHoverBorderBrush : LightTrashHoverBorderBrush;
    private static readonly Brush LightTitleBarBrush = FrozenBrush(Color.FromArgb(12, 120, 92, 48));
    private static readonly Brush DarkTitleBarBrush = FrozenBrush(Color.FromArgb(18, 230, 223, 211));
    private static Brush TitleBarBrush => Theme.IsDark ? DarkTitleBarBrush : LightTitleBarBrush;

    private static readonly Brush LightTitleBarDividerBrush = FrozenBrush(Color.FromArgb(28, 120, 92, 48));
    private static readonly Brush DarkTitleBarDividerBrush = FrozenBrush(Color.FromArgb(34, 230, 223, 211));
    private static Brush TitleBarDividerBrush => Theme.IsDark ? DarkTitleBarDividerBrush : LightTitleBarDividerBrush;
    private const int TodoMoveAnimationMilliseconds = 150;

    private static SolidColorBrush FrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static readonly ControlTemplate SharedContextMenuTemplate = BuildContextMenuTemplate();
    private static readonly Style SharedCompactMenuItemStyle = BuildCompactMenuItemStyle();
    private static readonly Style SharedIconButtonStyle = BuildIconButtonStyle();
    private static readonly Style SharedCheckBoxStyle = BuildCustomCheckBoxStyle();

    private static ControlTemplate BuildContextMenuTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));

        var presenter = new FrameworkElementFactory(typeof(ItemsPresenter));
        border.AppendChild(presenter);

        return new ControlTemplate(typeof(ContextMenu))
        {
            VisualTree = border
        };
    }

    private static Style BuildCompactMenuItemStyle()
    {
        var style = new Style(typeof(MenuItem));

        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 4, 10, 4)));
        style.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("TextBrushKey")));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, 0.0));

        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "Bd";
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));

        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(ContentPresenter.ContentSourceProperty, "Header");
        content.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
        content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(content);

        var template = new ControlTemplate(typeof(MenuItem))
        {
            VisualTree = border
        };

        var hover = new Trigger
        {
            Property = WpfMenuItem.IsHighlightedProperty,
            Value = true
        };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("HoverBrushKey"), "Bd"));

        var disabled = new Trigger
        {
            Property = UIElement.IsEnabledProperty,
            Value = false
        };
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.72));

        template.Triggers.Add(hover);
        template.Triggers.Add(disabled);

        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        return style;
    }

    private static Style BuildIconButtonStyle()
    {
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("WeakTextBrushKey")));
        style.Setters.Add(new Setter(Control.CursorProperty, Cursors.Hand));
        style.Setters.Add(new Setter(Control.FontSizeProperty, 13.0));
        style.Setters.Add(new Setter(Control.FocusableProperty, false));

        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "Bd";
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));

        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ContentControl.ContentProperty));
        content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(content);

        var template = new ControlTemplate(typeof(Button))
        {
            VisualTree = border
        };

        var mouseOver = new Trigger
        {
            Property = UIElement.IsMouseOverProperty,
            Value = true
        };
        mouseOver.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("HoverBrushKey")));
        mouseOver.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("TextBrushKey")));

        var pressed = new Trigger
        {
            Property = ButtonBase.IsPressedProperty,
            Value = true
        };
        pressed.Setters.Add(new Setter(UIElement.OpacityProperty, 0.7));

        template.Triggers.Add(mouseOver);
        template.Triggers.Add(pressed);
        style.Setters.Add(new Setter(Control.TemplateProperty, template));

        return style;
    }

    private static Style BuildCustomCheckBoxStyle()
    {
        var style = new Style(typeof(CheckBox));

        style.Setters.Add(new Setter(FrameworkElement.WidthProperty, 16.0));
        style.Setters.Add(new Setter(FrameworkElement.HeightProperty, 16.0));
        style.Setters.Add(new Setter(Control.CursorProperty, Cursors.Hand));
        style.Setters.Add(new Setter(UIElement.FocusableProperty, false));
        style.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
        style.Setters.Add(new Setter(UIElement.SnapsToDevicePixelsProperty, true));
        style.Setters.Add(new Setter(FrameworkElement.UseLayoutRoundingProperty, true));

        var grid = new FrameworkElementFactory(typeof(Grid));

        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "CheckBorder";
        border.SetValue(FrameworkElement.WidthProperty, 16.0);
        border.SetValue(FrameworkElement.HeightProperty, 16.0);
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1.5));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        border.SetValue(Border.BorderBrushProperty, new DynamicResourceExtension("CheckBoxBorderBrushKey"));
        border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        border.SetValue(UIElement.SnapsToDevicePixelsProperty, true);
        grid.AppendChild(border);

        var path = new FrameworkElementFactory(typeof(System.Windows.Shapes.Path));
        path.Name = "CheckMark";
        path.SetValue(System.Windows.Shapes.Path.DataProperty, Geometry.Parse("M 3,7.5 L 6.5,11 L 13,4"));
        path.SetValue(System.Windows.Shapes.Path.StrokeProperty, new DynamicResourceExtension("PaperBrushKey"));
        path.SetValue(System.Windows.Shapes.Path.StrokeThicknessProperty, 2.0);
        path.SetValue(System.Windows.Shapes.Path.StrokeStartLineCapProperty, PenLineCap.Round);
        path.SetValue(System.Windows.Shapes.Path.StrokeEndLineCapProperty, PenLineCap.Round);
        path.SetValue(System.Windows.Shapes.Path.StrokeLineJoinProperty, PenLineJoin.Round);
        path.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
        path.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        path.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        path.SetValue(UIElement.SnapsToDevicePixelsProperty, true);
        grid.AppendChild(path);

        var template = new ControlTemplate(typeof(CheckBox))
        {
            VisualTree = grid
        };

        var checkedTrigger = new Trigger
        {
            Property = ToggleButton.IsCheckedProperty,
            Value = true
        };
        checkedTrigger.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("CheckBoxActiveBrushKey"), "CheckBorder"));
        checkedTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, Brushes.Transparent, "CheckBorder"));
        checkedTrigger.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(0), "CheckBorder"));
        checkedTrigger.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "CheckMark"));

        var hoverTrigger = new MultiTrigger();
        hoverTrigger.Conditions.Add(new Condition { Property = UIElement.IsMouseOverProperty, Value = true });
        hoverTrigger.Conditions.Add(new Condition { Property = ToggleButton.IsCheckedProperty, Value = false });
        hoverTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, new DynamicResourceExtension("CheckBoxUncheckedHoverBorderBrushKey"), "CheckBorder"));
        hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("CheckBoxUncheckedHoverBgKey"), "CheckBorder"));

        var hoverCheckedTrigger = new MultiTrigger();
        hoverCheckedTrigger.Conditions.Add(new Condition { Property = UIElement.IsMouseOverProperty, Value = true });
        hoverCheckedTrigger.Conditions.Add(new Condition { Property = ToggleButton.IsCheckedProperty, Value = true });
        hoverCheckedTrigger.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("CheckBoxActiveHoverBrushKey"), "CheckBorder"));
        hoverCheckedTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, Brushes.Transparent, "CheckBorder"));
        hoverCheckedTrigger.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(0), "CheckBorder"));

        template.Triggers.Add(checkedTrigger);
        template.Triggers.Add(hoverTrigger);
        template.Triggers.Add(hoverCheckedTrigger);

        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        return style;
    }

    public PaperWindow(PaperData paper, AppController controller)
    {
        _paper = paper;
        _controller = controller;

        ConfigureWindow();
        BuildShell();

        Loaded += (_, _) => SaveGeometryIfAllowed();
        LocationChanged += (_, _) => SaveGeometryIfAllowed();
        SizeChanged += (_, _) => SaveGeometryIfAllowed();
        PreviewMouseMove += OnWindowPreviewMouseMove;
        PreviewMouseWheel += OnWindowPreviewMouseWheel;
        PreviewMouseLeftButtonUp += OnWindowPreviewMouseLeftButtonUp;
        LostMouseCapture += OnLostMouseCapture;
        PreviewKeyDown += OnWindowPreviewKeyDown;
        Deactivated += (_, _) =>
        {
            if (_todoDrag != null)
            {
                EndTodoMouseDrag(commit: false);
            }
        };
        Closing += OnClosing;

        if (_paper.Type == PaperTypes.Note)
        {
            PreviewMouseLeftButtonDown += (s, e) =>
            {
                if (_noteBox != null && _noteBox.IsFocused)
                {
                    var clicked = e.OriginalSource as DependencyObject;
                    if (!IsDescendantOf(clicked, _noteBox))
                    {
                        ExitNoteEditor();
                    }
                }
            };
        }
    }

    public void CloseForReal()
    {
        _closeForReal = true;
        Close();
    }

    private void ConfigureWindow()
    {
        InitializeThemeResources();
        Title = _controller.PaperTitleText(_paper);
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.Manual;

        Left = _paper.X;
        Top = _paper.Y;

        if (_paper.IsCollapsed && _controller.State.UseCapsuleMode)
        {
            Width = CapsuleWindowWidth();
            Height = PaperLayoutDefaults.CapsuleHeight;
            MinWidth = CapsuleWindowWidth();
            MinHeight = PaperLayoutDefaults.CapsuleHeight;
            ResizeMode = ResizeMode.NoResize;
        }
        else
        {
            Width = _paper.Width;
            Height = _paper.Height;
            MinWidth = PaperLayoutDefaults.MinWidth;
            MinHeight = PaperLayoutDefaults.MinHeight;
            ResizeMode = ResizeMode.CanResizeWithGrip;
        }

        RefreshEffectiveTopmost();
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        FontFamily = new FontFamily("Segoe UI");
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
    }

    private void InitializeThemeResources()
    {
        Resources["PaperBrushKey"] = PaperBrush;
        Resources["PaperBorderBrushKey"] = PaperBorderBrush;
        Resources["TextBrushKey"] = TextBrush;
        Resources["WeakTextBrushKey"] = WeakTextBrush;
        Resources["HoverBrushKey"] = HoverBrush;
        Resources["DropIndicatorBrushKey"] = DropIndicatorBrush;
        Resources["AppendDropBrushKey"] = AppendDropBrush;
        Resources["MenuHoverBrushKey"] = MenuHoverBrush;
        Resources["TitleBarBrushKey"] = TitleBarBrush;
        Resources["TitleBarDividerBrushKey"] = TitleBarDividerBrush;

        Resources["CheckBoxBorderBrushKey"] = CheckBoxBorderBrush;
        Resources["CheckBoxActiveBrushKey"] = Theme.ActiveBrush;
        Resources["CheckBoxUncheckedHoverBorderBrushKey"] = Theme.IsDark ? FrozenBrush(Color.FromRgb(180, 160, 130)) : FrozenBrush(Color.FromRgb(120, 95, 60));
        Resources["CheckBoxUncheckedHoverBgKey"] = Theme.IsDark ? FrozenBrush(Color.FromArgb(20, 230, 223, 211)) : FrozenBrush(Color.FromArgb(20, 120, 92, 48));
        Resources["CheckBoxActiveHoverBrushKey"] = Theme.IsDark ? FrozenBrush(Color.FromRgb(146, 120, 84)) : FrozenBrush(Color.FromRgb(115, 90, 58));
    }

    public void UpdateTheme()
    {
        InitializeThemeResources();

        RefreshPaperTitle();
        RefreshPaperIconButton();
        UpdateTextZoom();

        if (_paper.Type == PaperTypes.Note)
        {
            if (_noteBox != null)
            {
                _noteBox.RefreshVisualStyle();
            }

        }
        else
        {
            RebuildTodoRows(CurrentFocusedTodoItemId());
        }
    }

    public void UpdateMarkdownRenderMode()
    {
        if (_paper.Type == PaperTypes.Note && _noteBox != null)
        {
            var mode = _controller.State.MarkdownRenderMode;
            TraceNoteRender($"UpdateMarkdownRenderMode rebuild mode={mode}");
            RebuildNoteBodyForMarkdownMode();
        }
    }

    private void TraceNoteRender(string message)
    {
#if DEBUG
        try
        {
            var path = System.IO.Path.Combine(AppContext.BaseDirectory, "md-render-trace.log");
            var line = $"{DateTime.Now:HH:mm:ss.fff} paper={_paper.Id[..Math.Min(6, _paper.Id.Length)]} {message}{Environment.NewLine}";
            lock (NoteRenderTraceLock)
            {
                System.IO.File.AppendAllText(path, line);
            }
        }
        catch
        {
            // Test-only diagnostics must never affect note interaction.
        }
#endif
    }

    private void RebuildNoteBodyForMarkdownMode()
    {
        if (_paper.Type != PaperTypes.Note)
        {
            return;
        }

        var oldBox = _noteBox;
        var text = oldBox?.Text ?? _paper.Content ?? "";
        var caret = oldBox?.CaretIndex ?? 0;
        var verticalOffset = oldBox?.VerticalOffset ?? 0;
        var horizontalOffset = oldBox?.HorizontalOffset ?? 0;
        _paper.Content = text;

        TraceNoteRender($"RebuildNoteBody start textLength={text.Length} caret={caret} v={verticalOffset:F1} h={horizontalOffset:F1}");

        var oldBodies = new List<UIElement>();
        if (_noteBodyElement != null)
        {
            oldBodies.Add(_noteBodyElement);
        }
        else
        {
            var zoomHost = _textZoomIndicator?.Parent as UIElement;
            foreach (UIElement child in _shell.Children)
            {
                if (Grid.GetRow(child) == 1 && !ReferenceEquals(child, zoomHost))
                {
                    oldBodies.Add(child);
                }
            }
        }

        _noteBox = null;
        _showNotePreview = null;

        var body = BuildNoteBody();
        body.Opacity = 0;
        body.IsHitTestVisible = false;
        Grid.SetRow(body, 1);
        Panel.SetZIndex(body, 1);
        _noteBodyElement = body;
        _shell.Children.Add(body);

        if (_noteBox == null)
        {
            TraceNoteRender("RebuildNoteBody end: no note box");
            return;
        }

        _noteBox.CaretIndex = Math.Clamp(caret, 0, _noteBox.Text.Length);
        _showNotePreview?.Invoke();
        body.UpdateLayout();

        Dispatcher.BeginInvoke(
            (Action)(() =>
            {
                if (_noteBox == null)
                {
                    return;
                }

                foreach (var oldBody in oldBodies)
                {
                    _shell.Children.Remove(oldBody);
                }

                body.Opacity = 1;
                body.IsHitTestVisible = true;
                _noteBox.ScrollToHorizontalOffset(horizontalOffset);
                _noteBox.ScrollToVerticalOffset(verticalOffset);
                _showNotePreview?.Invoke();
                TraceNoteRender($"RebuildNoteBody restored caret={_noteBox.CaretIndex} v={verticalOffset:F1} h={horizontalOffset:F1}");
            }),
            System.Windows.Threading.DispatcherPriority.Render);
    }

    private void ExitNoteEditor()
    {
        if (_paper.Type != PaperTypes.Note || _noteBox == null)
        {
            return;
        }

        if (_noteBox.ContextMenu?.IsOpen == true)
        {
            return;
        }

        Keyboard.ClearFocus();
        _showNotePreview?.Invoke();
    }

    private void BuildShell()
    {
        var windowHost = new Grid
        {
            Background = Brushes.Transparent,
            ClipToBounds = false
        };
        Content = windowHost;

        _paperChrome = new Border
        {
            Margin = new Thickness(WindowChromeMargin),
            CornerRadius = PaperChromeCornerRadiusForState(_paper.IsCollapsed && _controller.State.UseCapsuleMode),
            BorderThickness = new Thickness(1),
            SnapsToDevicePixels = true,
            Effect = new DropShadowEffect
            {
                BlurRadius = 14,
                ShadowDepth = 2,
                Opacity = 0.18
            }
        };
        _paperChrome.SetResourceReference(Border.BackgroundProperty, "PaperBrushKey");
        _paperChrome.SetResourceReference(Border.BorderBrushProperty, "PaperBorderBrushKey");

        windowHost.Children.Add(_paperChrome);

        _containerGrid.Background = Brushes.Transparent;
        _containerGrid.ClipToBounds = false;
        _containerGrid.RenderTransform = _shellScale;
        _containerGrid.RenderTransformOrigin = new Point(0, 0);
        _paperChrome.Child = _containerGrid;

        _shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _containerGrid.Children.Add(_shell);

        BuildTopBar();
        BuildBody();
        BuildDragLayer();

        BuildCapsuleShell();
        _capsuleShell.Margin = new Thickness(WindowChromeMargin);
        _capsuleShell.HorizontalAlignment = HorizontalAlignment.Left;
        _capsuleShell.VerticalAlignment = VerticalAlignment.Top;
        Panel.SetZIndex(_capsuleShell, 10);
        windowHost.Children.Add(_capsuleShell);

        if (_paper.IsCollapsed && _controller.State.UseCapsuleMode)
        {
            _shell.Visibility = Visibility.Collapsed;
            _shell.Opacity = 0;
            _capsuleShell.Visibility = Visibility.Visible;
            _capsuleShell.Opacity = 1;
        }
        else
        {
            _shell.Visibility = Visibility.Visible;
            _shell.Opacity = 1;
            _capsuleShell.Visibility = Visibility.Collapsed;
            _capsuleShell.Opacity = 0;
        }

        _paperChrome.ContextMenu = BuildPaperContextMenu();
        UpdateTextZoom();
    }

    private void BuildDragLayer()
    {
        _dragLayer = new Canvas
        {
            IsHitTestVisible = false,
            Background = Brushes.Transparent,
            ClipToBounds = false
        };

        Grid.SetRowSpan(_dragLayer, 3);
        Panel.SetZIndex(_dragLayer, 1000);
        _shell.Children.Add(_dragLayer);
    }

    private void BuildTopBar()
    {
        var top = new Grid
        {
            Height = TitleBarHeight,
            Margin = new Thickness(3, 3, 6, 0),
            Background = Brushes.Transparent
        };

        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        top.PreviewMouseLeftButtonDown += (_, _) => ExitNoteEditor();
        top.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Left && e.ClickCount == 1)
            {
                try { DragMove(); } catch { }
            }
        };

        var titleArea = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        titleArea.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        titleArea.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _paperIconButton = IconButton(_paper.Type == PaperTypes.Note ? "✎" : "☑", _paper.AlwaysOnTop ? Strings.Get("Unpin") : Strings.Get("Pin"));
        _paperIconButton.Width = 23;
        _paperIconButton.FontSize = _paper.Type == PaperTypes.Note ? 15 : 13;
        _paperIconButton.HorizontalAlignment = HorizontalAlignment.Left;
        _paperIconButton.VerticalAlignment = VerticalAlignment.Center;
        _paperIconButton.Click += (_, _) => ToggleTopmost();
        _paperIconButton.MouseEnter += (_, _) => _paperIconButton.Opacity = 1.0;
        _paperIconButton.MouseLeave += (_, _) => RefreshPaperIconButton();
        RefreshPaperIconButton();

        Grid.SetColumn(_paperIconButton, 0);
        titleArea.Children.Add(_paperIconButton);

        var titleHost = new Border
        {
            Margin = new Thickness(0, 0, 8, 0),
            Padding = new Thickness(4, 1, 5, 1),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Background = Brushes.Transparent,
            Cursor = Cursors.IBeam,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 38,
            MaxWidth = 86,
            ToolTip = Strings.Get("ToolTipEditTitle")
        };
        titleHost.SetResourceReference(Border.BorderBrushProperty, "TitleBarDividerBrushKey");

        var titleEditLayer = new Grid
        {
            MinWidth = 30,
            MaxWidth = 76
        };

        _titleText = new TextBlock
        {
            Foreground = TextBrush,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.IBeam
        };

        _titleEditBox = new TextBox
        {
            Visibility = Visibility.Collapsed,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = TextBrush,
            CaretBrush = TextBrush,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            MaxLength = PaperTitles.MaxTitleLength,
            Padding = new Thickness(0),
            VerticalContentAlignment = VerticalAlignment.Center,
            FocusVisualStyle = null
        };
        _titleEditBox.PreviewMouseLeftButtonDown += (_, e) => e.Handled = false;
        _titleEditBox.TextChanged += (_, _) => CoerceTitleEditText();
        _titleEditBox.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                CommitTitleEdit();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                EndTitleEdit(commit: false);
                e.Handled = true;
            }
        };
        _titleEditBox.LostKeyboardFocus += (_, _) =>
        {
            if (_isEditingTitle)
            {
                CommitTitleEdit();
            }
        };

        titleEditLayer.Children.Add(_titleText);
        titleEditLayer.Children.Add(_titleEditBox);
        titleHost.Child = titleEditLayer;
        titleHost.MouseEnter += (_, _) => titleHost.Background = HoverBrush;
        titleHost.MouseLeave += (_, _) => titleHost.Background = Brushes.Transparent;
        titleHost.MouseLeftButtonDown += (_, e) =>
        {
            BeginTitleEdit();
            e.Handled = true;
        };

        Grid.SetColumn(titleHost, 1);
        titleArea.Children.Add(titleHost);

        RefreshPaperTitle();

        Grid.SetColumn(titleArea, 0);
        top.Children.Add(titleArea);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };

        _newTodoButton = IconButton("＋✓", Strings.Get("ToolTipNewTodoPaper"));
        _newTodoButton.Click += (_, _) => _controller.CreatePaper(PaperTypes.Todo, show: true, _paper);

        _newNoteButton = IconButton("＋✎", Strings.Get("ToolTipNewNotePaper"));
        _newNoteButton.Click += (_, _) => _controller.CreatePaper(PaperTypes.Note, show: true, _paper);

        if (_paper.Type == PaperTypes.Note)
        {
            _openMarkdownButton = IconButton("MD", OpenMarkdownEditorToolTip());
            _openMarkdownButton.FontSize = 10.5;
            _openMarkdownButton.Click += (_, _) => OpenMarkdownInDefaultEditor();
            buttons.Children.Add(_openMarkdownButton);
        }

        _closeButton = IconButton("×", Strings.Get("ToolTipHideThisPaper"));
        _closeButton.FontSize = 16;
        _closeButton.Click += (_, _) =>
        {
            if (_controller.State.UseCapsuleMode)
            {
                SetCollapsedState(true);
            }
            else
            {
                _controller.HidePaper(_paper);
            }
        };
        RefreshCloseButton();

        buttons.Children.Add(_newTodoButton);
        buttons.Children.Add(_newNoteButton);
        buttons.Children.Add(_closeButton);
        UpdateTopBarNewPaperButtons();

        Grid.SetColumn(buttons, 1);
        top.Children.Add(buttons);

        var topHost = new Border
        {
            Margin = new Thickness(0, 0, 0, 1.5),
            BorderThickness = new Thickness(0, 0, 0, 1),
            CornerRadius = new CornerRadius(14, 14, 0, 0),
            Child = top
        };
        topHost.SetResourceReference(Border.BackgroundProperty, "TitleBarBrushKey");
        topHost.SetResourceReference(Border.BorderBrushProperty, "TitleBarDividerBrushKey");

        Grid.SetRow(topHost, 0);
        _shell.Children.Add(topHost);
    }

    private void BuildBody()
    {
        UIElement body = _paper.Type == PaperTypes.Note ? BuildNoteBody() : BuildTodoBody();
        Grid.SetRow(body, 1);
        if (_paper.Type == PaperTypes.Note)
        {
            _noteBodyElement = body;
        }
        _shell.Children.Add(body);

        if (_paper.Type == PaperTypes.Note)
        {
            BuildTextZoomOverlay();
        }
    }

    private void BuildTextZoomOverlay()
    {
        var zoomHost = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 12, 7),
            Padding = new Thickness(6, 1, 6, 1),
            CornerRadius = new CornerRadius(6),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            ToolTip = Strings.Get("ToolTipResetTextZoom"),
            Visibility = Visibility.Collapsed
        };

        _textZoomIndicator = new TextBlock
        {
            Foreground = WeakTextBrush,
            FontSize = 10.5,
            FontWeight = FontWeights.SemiBold,
            Opacity = 0.55,
            VerticalAlignment = VerticalAlignment.Center
        };

        zoomHost.Child = _textZoomIndicator;
        zoomHost.MouseEnter += (_, _) =>
        {
            zoomHost.Background = HoverBrush;
            _textZoomIndicator.Foreground = TextBrush;
            _textZoomIndicator.Opacity = 1.0;
        };
        zoomHost.MouseLeave += (_, _) =>
        {
            zoomHost.Background = Brushes.Transparent;
            _textZoomIndicator.Foreground = WeakTextBrush;
            _textZoomIndicator.Opacity = 0.55;
        };
        zoomHost.MouseLeftButtonUp += (_, e) =>
        {
            _controller.SetPaperTextZoom(_paper, 1.0);
            e.Handled = true;
        };

        Grid.SetRow(zoomHost, 1);
        Panel.SetZIndex(zoomHost, 20);
        _shell.Children.Add(zoomHost);
    }

    private UIElement BuildTodoBody()
    {
        if (_paper.Items.Count == 0)
        {
            _paper.Items.Add(new PaperItem { Order = 0 });
        }

        _todoPanel = new StackPanel
        {
            Margin = new Thickness(8, 4, 7, 4)
        };

        RebuildTodoRows();

        return new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _todoPanel,
            FocusVisualStyle = null
        };
    }

    private UIElement BuildNoteBody()
    {
        var host = new Grid();

        _noteBox = new MarkdownTextBox
        {
            Text = _paper.Content ?? "",
            MaxLength = 100000,
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = TextBrush,
            CaretBrush = TextBrush,
            FontFamily = NoteTypography.FontFamily,
            FontSize = NoteTypography.FontSize,
            FontStyle = NoteTypography.FontStyle,
            FontWeight = NoteTypography.FontWeight,
            FontStretch = NoteTypography.FontStretch,
            Language = NoteTypography.Language,
            Margin = NoteTypography.ContentPadding,
            FocusVisualStyle = null
        };
        NoteTypography.ApplyTextRendering(_noteBox);
        var box = _noteBox;
        box.SetMarkdownRenderMode(_controller.State.MarkdownRenderMode);
        box.SetTextZoom(CurrentTextZoom());

        host.Children.Add(box);
        var editorMenu = CreateContextMenu();
        editorMenu.Items.Add(MenuHeader(Strings.Get("MenuFormat")));
        editorMenu.Items.Add(MenuItem(Strings.Get("MenuBold"), (_, _) => box.WrapSelection("**", "**")));
        editorMenu.Items.Add(MenuItem(Strings.Get("MenuItalic"), (_, _) => box.WrapSelection("*", "*")));
        editorMenu.Items.Add(MenuItem(Strings.Get("MenuStrikethrough"), (_, _) => box.WrapSelection("~~", "~~")));
        editorMenu.Items.Add(MenuItem(Strings.Get("MenuHeading"), (_, _) => box.InsertLinePrefix("# ")));
        editorMenu.Items.Add(MenuItem(Strings.Get("MenuQuote"), (_, _) => box.InsertLinePrefix("> ")));
        editorMenu.Items.Add(MenuItem(Strings.Get("MenuList"), (_, _) => box.InsertLinePrefix("- ")));
        editorMenu.Items.Add(MenuItem(Strings.Get("MenuCodeBlock"), (_, _) => box.WrapSelection("```\n", "\n```")));
        editorMenu.Items.Add(MenuItem(Strings.Get("MenuInsertLink"), (_, _) => box.InsertMarkdownLink()));
        editorMenu.Items.Add(MenuSeparator());
        editorMenu.Items.Add(MenuHeader(Strings.Get("MenuText")));
        editorMenu.Items.Add(MenuItem(Strings.Get("MenuCopy"), (_, _) => box.Copy()));
        editorMenu.Items.Add(MenuItem(Strings.Get("MenuPaste"), (_, _) => box.Paste()));
        editorMenu.Items.Add(MenuItem(Strings.Get("MenuSelectAll"), (_, _) => box.SelectAll()));

        var previewMenu = BuildPaperContextMenu();
        var isPreviewing = false;
        var isEnteringEditorFromPreview = false;

        void ShowPreview()
        {
            TraceNoteRender($"ShowPreview before isPreviewing={isPreviewing} boxPreview={box.IsPreviewMode}");
            box.SelectionLength = 0;
            box.SetPreviewMode(true);
            box.ContextMenu = previewMenu;
            isPreviewing = true;
            TraceNoteRender($"ShowPreview after isPreviewing={isPreviewing} boxPreview={box.IsPreviewMode}");
        }

        _showNotePreview = ShowPreview;

        void ShowEditor(bool focus = true)
        {
            TraceNoteRender($"ShowEditor before focus={focus} isPreviewing={isPreviewing} boxPreview={box.IsPreviewMode}");
            box.SetPreviewMode(false);
            box.ContextMenu = editorMenu;
            isPreviewing = false;

            if (focus && !box.IsKeyboardFocusWithin)
            {
                box.Focus();
            }
            TraceNoteRender($"ShowEditor after focus={focus} isPreviewing={isPreviewing} boxPreview={box.IsPreviewMode} focused={box.IsKeyboardFocusWithin}");
        }

        void ShowEditorAtPreviewPoint(Point previewPoint)
        {
            TraceNoteRender($"ShowEditorAtPreviewPoint x={previewPoint.X:F1} y={previewPoint.Y:F1}");
            var hasPreviewCaret = box.TryGetCharacterIndexFromPoint(previewPoint, out var caretIndex);

            isEnteringEditorFromPreview = true;
            ShowEditor(focus: false);

            if (!box.IsKeyboardFocusWithin)
            {
                box.Focus();
            }

            if (hasPreviewCaret)
            {
                box.CaretIndex = Math.Clamp(caretIndex, 0, box.Text.Length);
                box.SelectionLength = 0;
            }
            TraceNoteRender($"ShowEditorAtPreviewPoint after hasCaret={hasPreviewCaret} caret={box.CaretIndex}");
            Dispatcher.BeginInvoke(
                (Action)(() =>
                {
                    isEnteringEditorFromPreview = false;
                    TraceNoteRender($"ShowEditorAtPreviewPoint release focused={box.IsKeyboardFocusWithin} isPreviewing={isPreviewing} boxPreview={box.IsPreviewMode}");
                }),
                System.Windows.Threading.DispatcherPriority.ContextIdle);
        }

        static void OpenMarkdownLink(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url)
                {
                    UseShellExecute = true
                });
            }
            catch
            {
                // Link opening is optional; the note should never crash because a URL handler failed.
            }
        }

        box.TextChanged += (_, _) =>
        {
            _paper.Content = box.Text;
            _controller.MarkDirty();
        };

        box.PreviewKeyDown += (_, e) =>
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control)
            {
                return;
            }

            if (e.Key == Key.B)
            {
                box.WrapSelection("**", "**");
                e.Handled = true;
            }
            else if (e.Key == Key.I)
            {
                box.WrapSelection("*", "*");
                e.Handled = true;
            }
            else if (e.Key == Key.K)
            {
                box.InsertMarkdownLink();
                e.Handled = true;
            }
        };

        box.GotKeyboardFocus += (_, _) =>
        {
            TraceNoteRender($"GotKeyboardFocus isPreviewing={isPreviewing} boxPreview={box.IsPreviewMode}");
        };

        box.LostKeyboardFocus += (_, _) =>
        {
            if (box.ContextMenu != null && box.ContextMenu.IsOpen)
            {
                TraceNoteRender("LostKeyboardFocus ignored: context menu open");
                return;
            }
            if (isEnteringEditorFromPreview)
            {
                TraceNoteRender($"LostKeyboardFocus ignored: entering editor isPreviewing={isPreviewing} boxPreview={box.IsPreviewMode}");
                return;
            }
            TraceNoteRender($"LostKeyboardFocus isPreviewing={isPreviewing} boxPreview={box.IsPreviewMode}");
            ShowPreview();
        };

        MouseButtonEventHandler noteMouseDown = (_, e) =>
        {
            if (IsScrollBarInteractionSource(e.OriginalSource as DependencyObject, box))
            {
                TraceNoteRender($"PreviewMouseLeftButtonDown ignored: scrollbar isPreviewing={isPreviewing} boxPreview={box.IsPreviewMode}");
                return;
            }

            var textViewPoint = e.GetPosition(box.TextArea.TextView);
            TraceNoteRender($"PreviewMouseLeftButtonDown isPreviewing={isPreviewing} boxPreview={box.IsPreviewMode} handled={e.Handled}");
            if (!isPreviewing)
            {
                if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control &&
                    box.TryGetMarkdownLinkFromTextViewPoint(textViewPoint, out var editUrl))
                {
                    OpenMarkdownLink(editUrl);
                    e.Handled = true;
                }
                return;
            }

            var point = e.GetPosition(box);
            if (box.TryGetMarkdownLinkFromTextViewPoint(textViewPoint, out var url))
            {
                OpenMarkdownLink(url);
                e.Handled = true;
                return;
            }

            ShowEditorAtPreviewPoint(point);
            e.Handled = true;
        };
        box.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent, noteMouseDown, true);

        box.MouseMove += (sender, e) =>
        {
            var isOverLink = box.TryGetMarkdownLinkFromTextViewPoint(e.GetPosition(box.TextArea.TextView), out _);
            if (isPreviewing)
            {
                box.SetInteractionCursor(isOverLink ? Cursors.Hand : Cursors.Arrow);
            }
            else
            {
                box.SetInteractionCursor(isOverLink && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control
                    ? Cursors.Hand
                    : Cursors.IBeam);
            }
        };

        box.MouseLeave += (_, _) =>
        {
            box.SetInteractionCursor(isPreviewing ? Cursors.Arrow : Cursors.IBeam);
        };

        editorMenu.Closed += (_, _) =>
        {
            if (!isPreviewing && !box.IsFocused && !box.IsKeyboardFocusWithin)
            {
                ShowPreview();
            }
        };

        if (box.IsFocused || string.IsNullOrEmpty(box.Text))
        {
            ShowEditor();
        }
        else
        {
            ShowPreview();
        }

        return host;
    }

    private void RebuildTodoRows(string? focusItemId = null)
    {
        if (_todoPanel == null)
        {
            return;
        }

        var targetFocus = focusItemId ?? _pendingFocusItemId;
        _pendingFocusItemId = null;

        NormalizeTodoItems();
        NormalizeOrders();

        _todoPanel.Children.Clear();
        _todoEditors.Clear();
        _todoRows.Clear();

        foreach (var item in OrderedItems())
        {
            _todoPanel.Children.Add(BuildTodoRow(item));
        }

        _todoPanel.Children.Add(BuildTodoAppendArea());

        if (!string.IsNullOrWhiteSpace(targetFocus))
        {
            FocusTodoItem(targetFocus);
        }
    }

    private void FocusTodoItem(string? itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId))
        {
            return;
        }

        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_todoEditors.TryGetValue(itemId, out var box))
            {
                box.Focus();
                box.CaretIndex = box.Text.Length;
            }
        }), System.Windows.Threading.DispatcherPriority.Input);
    }

    private UIElement BuildTodoAppendArea()
    {
        var area = new Border
        {
            Margin = new Thickness(0, 6, 0, 2),
            Padding = new Thickness(0, 4, 0, 4),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = AppendBorderBrush,
            Background = AppendBgBrush,
            MinHeight = 30,
            Cursor = Cursors.IBeam,
            AllowDrop = true,
            ToolTip = Strings.Get("AppendAreaToolTip")
        };

        _appendArea = area;

        var plus = new TextBlock
        {
            Text = "＋",
            Foreground = WeakTextBrush,
            Opacity = 0.42,
            FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        area.Child = plus;

        area.MouseEnter += (_, _) =>
        {
            area.Background = AppendHoverBgBrush;
            plus.Opacity = 0.7;
        };

        area.MouseLeave += (_, _) =>
        {
            ResetAppendAreaDropState();
        };

        area.MouseLeftButtonDown += (_, e) =>
        {
            var newItem = AddItemAfter(OrderedItems().LastOrDefault(), "");
            _pendingFocusItemId = newItem.Id;
            RebuildTodoRows(newItem.Id);
            e.Handled = true;
        };

        return area;
    }

    private void ShowAppendAreaAsTrashBin(bool active, bool hovered = false)
    {
        if (_appendArea == null)
        {
            return;
        }

        if (active)
        {
            if (hovered)
            {
                _appendArea.Background = TrashHoverBgBrush;
                _appendArea.BorderBrush = TrashHoverBorderBrush;
                _appendArea.BorderThickness = new Thickness(1.5);
            }
            else
            {
                _appendArea.Background = TrashBgBrush;
                _appendArea.BorderBrush = TrashBorderBrush;
                _appendArea.BorderThickness = new Thickness(1);
            }

            if (_appendArea.Child is TextBlock text)
            {
                text.Text = "🗑";
                text.Foreground = TrashTextBrush;
                text.Opacity = hovered ? 1.0 : 0.65;
                text.FontSize = 13;
            }
        }
        else
        {
            _appendArea.Background = AppendBgBrush;
            _appendArea.BorderBrush = AppendBorderBrush;
            _appendArea.BorderThickness = new Thickness(1);

            if (_appendArea.Child is TextBlock text)
            {
                text.Text = "＋";
                text.Foreground = WeakTextBrush;
                text.Opacity = 0.42;
                text.FontSize = 14;
            }
        }
    }

    private void ResetAppendAreaDropState()
    {
        ShowAppendAreaAsTrashBin(active: false);
    }

    private UIElement BuildTodoRow(PaperItem item)
    {
        var row = new Border
        {
            Margin = new Thickness(0, 2, 0, 2),
            Padding = new Thickness(2),
            CornerRadius = new CornerRadius(8),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0, 2, 0, 2),
            AllowDrop = true,
            Tag = item.Id,
            RenderTransform = new TranslateTransform()
        };

        row.MouseEnter += (_, _) =>
        {
            if (!Equals(_activeDropRow, row))
            {
                row.Background = HoverBrush;
            }
        };

        row.MouseLeave += (_, _) =>
        {
            if (!Equals(_activeDropRow, row))
            {
                row.Background = Brushes.Transparent;
            }
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });

        var check = new CheckBox
        {
            IsChecked = item.Done,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Cursor = Cursors.Hand,
            Focusable = false,
            FocusVisualStyle = null,
            Style = SharedCheckBoxStyle
        };

        Grid.SetColumn(check, 0);
        grid.Children.Add(check);

        var text = new TodoTextBox
        {
            Text = item.Text,
            IsDone = item.Done,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = item.Done ? WeakTextBrush : TextBrush,
            CaretBrush = TextBrush,
            FontSize = 13,
            Padding = new Thickness(2, 3, 2, 3),
            VerticalContentAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = false,
            MaxLength = 5000
        };

        _todoEditors[item.Id] = text;

        text.TextChanged += (_, _) =>
        {
            item.Text = text.Text;
            _controller.MarkDirty();
        };

        text.PreviewKeyDown += (_, e) => HandleTodoKeyDown(e, item, text);
        DataObject.AddPastingHandler(text, (sender, e) => HandleTodoPaste(e, item, text));

        text.GotFocus += (_, _) =>
        {
            _activeOriginalItemId = item.Id;
            _activeOriginalText = text.Text;
        };

        text.LostFocus += (_, _) =>
        {
            if (_activeOriginalItemId == item.Id && _activeOriginalText != null && text.Text != _activeOriginalText)
            {
                var oldText = item.Text;
                item.Text = _activeOriginalText;

                _undoStack.Add(CloneItems(_paper.Items));
                if (_undoStack.Count > MaxUndoDepth)
                {
                    _undoStack.RemoveAt(0);
                }
                _redoStack.Clear();

                item.Text = oldText;
                _activeOriginalText = oldText;
            }
        };

        check.Checked += (_, _) =>
        {
            PushUndoSnapshot();
            item.Done = true;
            text.IsDone = true;
            text.Foreground = WeakTextBrush;
            _controller.MarkDirty();
        };

        check.Unchecked += (_, _) =>
        {
            PushUndoSnapshot();
            item.Done = false;
            text.IsDone = false;
            text.Foreground = TextBrush;
            _controller.MarkDirty();
        };

        var itemMenu = CreateContextMenu();
        itemMenu.Items.Add(MenuHeader(Strings.Get("MenuTodoItem")));
        itemMenu.Items.Add(MenuItem(Strings.Get("MenuDeleteItem"), (_, _) => RemoveItem(item)));
        itemMenu.Items.Add(MenuItem(Strings.Get("MenuClearDone"), (_, _) => ClearDoneItems()));

        itemMenu.Opened += (_, _) => row.Background = HoverBrush;
        itemMenu.Closed += (_, _) =>
        {
            if (!row.IsMouseOver)
            {
                row.Background = Brushes.Transparent;
            }
        };

        text.ContextMenu = itemMenu;
        text.PreviewMouseRightButtonDown += (_, _) => text.Focus();

        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var handleGlyph = new TextBlock
        {
            Text = "≡",
            Foreground = WeakTextBrush,
            Opacity = 0.48,
            FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var handle = new Border
        {
            Background = Brushes.Transparent,
            Cursor = Cursors.SizeAll,
            Child = handleGlyph,
            ToolTip = Strings.Get("DragSortToolTip")
        };

        handle.MouseEnter += (_, _) => handleGlyph.Opacity = 0.78;
        handle.MouseLeave += (_, _) =>
        {
            if (_todoDrag?.ItemId != item.Id)
            {
                handleGlyph.Opacity = 0.48;
            }
        };

        handle.PreviewMouseLeftButtonDown += (_, e) =>
        {
            _todoDrag = new TodoDragState(item.Id, row, handle, e.GetPosition(this));
            CaptureMouse();
            e.Handled = true;
        };

        Grid.SetColumn(handle, 2);
        grid.Children.Add(handle);

        row.Child = grid;
        _todoRows.Add(row);
        return row;
    }

    private void HandleTodoKeyDown(KeyEventArgs e, PaperItem item, TodoTextBox box)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            var newItem = AddItemAfter(item, "");
            _pendingFocusItemId = newItem.Id;
            RebuildTodoRows(newItem.Id);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Back && string.IsNullOrEmpty(box.Text) && _paper.Items.Count > 1)
        {
            var previous = PreviousItem(item);
            var next = NextItem(item);
            RemoveItem(item, rebuild: false);
            RebuildTodoRows(previous?.Id ?? next?.Id);
            e.Handled = true;
        }
    }

    private void HandleTodoPaste(DataObjectPastingEventArgs e, PaperItem item, TodoTextBox box)
    {
        if (!ClipboardHelper.TryGetText(out var raw) || string.IsNullOrEmpty(raw))
        {
            return;
        }

        var lines = raw
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Split('\n')
            .Select(CleanPastedTodoLine)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => line.Length > 5000 ? line[..5000] : line)
            .ToList();

        if (lines.Count > 200)
        {
            lines = lines.Take(200).ToList();
        }

        if (lines.Count <= 1)
        {
            return;
        }

        e.CancelCommand();

        PushUndoSnapshot();
        ReplaceSelection(box, lines[0]);
        item.Text = box.Text;

        var last = item;
        foreach (var line in lines.Skip(1))
        {
            last = AddItemAfter(last, line, pushUndo: false);
        }

        _pendingFocusItemId = last.Id;
        RebuildTodoRows(last.Id);
        _controller.MarkDirty();
    }

    private static string CleanPastedTodoLine(string line)
    {
        var cleaned = line.Trim();

        cleaned = TodoCheckboxCleanRegex().Replace(cleaned, "");
        cleaned = TodoBulletCleanRegex().Replace(cleaned, "");
        cleaned = TodoNumberCleanRegex().Replace(cleaned, "");
        cleaned = TodoGlyphCleanRegex().Replace(cleaned, "");

        return cleaned.Trim();
    }

    private ContextMenu BuildPaperContextMenu()
    {
        var menu = CreateContextMenu();

        menu.Items.Add(MenuHeader(Strings.Get("MenuNew")));
        menu.Items.Add(MenuItem(Strings.Get("MenuNewTodoPaper"), (_, _) => _controller.CreatePaper(PaperTypes.Todo, show: true, _paper)));
        menu.Items.Add(MenuItem(Strings.Get("MenuNewNotePaper"), (_, _) => _controller.CreatePaper(PaperTypes.Note, show: true, _paper)));

        if (_paper.Type == PaperTypes.Todo)
        {
            menu.Items.Add(MenuSeparator());
            menu.Items.Add(MenuHeader(Strings.Get("MenuTodo")));
            menu.Items.Add(MenuItem(Strings.Get("MenuClearDone"), (_, _) => ClearDoneItems()));
        }

        menu.Items.Add(MenuSeparator());
        menu.Items.Add(MenuHeader(Strings.Get("MenuThisPaper")));
        menu.Items.Add(MenuHeader(_controller.PaperCapsuleTitle(_paper)));

        if (_controller.State.UseCapsuleMode)
        {
            if (_paper.IsCollapsed)
            {
                menu.Items.Add(MenuItem(Strings.Get("MenuRestoreWindow"), (_, _) => SetCollapsedState(false)));
            }
            else
            {
                menu.Items.Add(MenuItem(Strings.Get("MenuCollapseToCapsule"), (_, _) => SetCollapsedState(true)));
            }
        }

        menu.Items.Add(MenuItem(Strings.Get("MenuHide"), (_, _) => _controller.HidePaper(_paper)));
        menu.Items.Add(MenuItem(Strings.Get("MenuDelete"), (_, _) => ConfirmAndDeletePaper()));

        return menu;
    }

    private void ToggleTopmost()
    {
        _paper.AlwaysOnTop = !_paper.AlwaysOnTop;
        RefreshEffectiveTopmost();
        RefreshPaperIconButton();
        _controller.MarkDirty();
    }

    internal void RefreshEffectiveTopmost()
    {
        var shouldBeTopmost = _paper.AlwaysOnTop || (_controller.State.UseCapsuleMode && _paper.IsCollapsed);
        Topmost = shouldBeTopmost && !_controller.SuppressTopmostForFullscreenForeground;
    }

    private void RefreshPaperIconButton()
    {
        if (_paperIconButton == null)
        {
            return;
        }

        _paperIconButton.ToolTip = _paper.AlwaysOnTop ? Strings.Get("Unpin") : Strings.Get("Pin");
        _paperIconButton.Opacity = _paper.AlwaysOnTop ? 1.0 : 0.58;
        _paperIconButton.Foreground = _paper.AlwaysOnTop ? TextBrush : WeakTextBrush;
        _paperIconButton.FontWeight = _paper.AlwaysOnTop ? FontWeights.SemiBold : FontWeights.Normal;
    }

    public void RefreshPaperTitle()
    {
        var title = _controller.PaperTitleText(_paper);
        Title = title;

        if (_titleText != null)
        {
            _titleText.Text = title;
            _titleText.ToolTip = Strings.Get("ToolTipEditTitle");
            _titleText.Foreground = TextBrush;
        }

        if (_titleEditBox != null)
        {
            _titleEditBox.Foreground = TextBrush;
            _titleEditBox.CaretBrush = TextBrush;
        }

        RefreshCapsuleLabel();
        if (_capsuleLeftArea != null)
        {
            _capsuleLeftArea.ContextMenu = BuildPaperContextMenu();
        }
        if (_paperChrome != null)
        {
            _paperChrome.ContextMenu = BuildPaperContextMenu();
        }
    }

    private void RequestTitleEdit()
    {
        if (_paper.IsCollapsed && _controller.State.UseCapsuleMode)
        {
            SetCollapsedState(false);
        }

        Dispatcher.BeginInvoke((Action)BeginTitleEdit, System.Windows.Threading.DispatcherPriority.Input);
    }

    private void BeginTitleEdit()
    {
        if (_titleText == null || _titleEditBox == null)
        {
            return;
        }

        ExitNoteEditor();
        _isEditingTitle = true;
        _titleEditBox.Text = _controller.PaperTitleText(_paper);
        _titleText.Visibility = Visibility.Collapsed;
        _titleEditBox.Visibility = Visibility.Visible;
        _titleEditBox.Focus();
        _titleEditBox.SelectAll();
    }

    private void CommitTitleEdit()
    {
        EndTitleEdit(commit: true);
    }

    private void EndTitleEdit(bool commit)
    {
        if (_titleText == null || _titleEditBox == null)
        {
            return;
        }

        if (commit)
        {
            _controller.UpdatePaperTitle(_paper, _titleEditBox.Text);
        }

        _isEditingTitle = false;
        _titleEditBox.Visibility = Visibility.Collapsed;
        _titleText.Visibility = Visibility.Visible;
        RefreshPaperTitle();
    }

    private void CoerceTitleEditText()
    {
        if (_titleEditBox == null || _isCoercingTitleEditText)
        {
            return;
        }

        var cleaned = PaperTitles.CleanCustomTitle(_titleEditBox.Text);
        if (_titleEditBox.Text == cleaned)
        {
            return;
        }

        _isCoercingTitleEditText = true;
        var caret = Math.Min(cleaned.Length, _titleEditBox.CaretIndex);
        _titleEditBox.Text = cleaned;
        _titleEditBox.CaretIndex = caret;
        _isCoercingTitleEditText = false;
    }

    public void UpdateTextZoom()
    {
        if (_paper.Type != PaperTypes.Note)
        {
            return;
        }

        var zoom = CurrentTextZoom();
        if (_noteBox != null)
        {
            var expectedFontSize = Math.Round(NoteTypography.FontSize * zoom, 1);
            if (IsLoaded && Math.Abs(_noteBox.FontSize - expectedFontSize) > 0.001)
            {
                RebuildNoteBodyForMarkdownMode();
            }
            else
            {
                _noteBox.SetTextZoom(zoom);
            }
        }

        if (_textZoomIndicator != null)
        {
            _textZoomIndicator.Text = $"{(int)Math.Round(zoom * 100)}%";
            _textZoomIndicator.Foreground = WeakTextBrush;
            _textZoomIndicator.Opacity = 0.55;
            if (_textZoomIndicator.Parent is UIElement host)
            {
                host.Visibility = Math.Abs(zoom - 1.0) < 0.001 ? Visibility.Collapsed : Visibility.Visible;
            }
        }
    }

    private double CurrentTextZoom()
    {
        return Math.Clamp(_paper.TextZoom, 0.5, 1.5);
    }

    private void OnWindowPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_paper.Type != PaperTypes.Note)
        {
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control)
        {
            return;
        }

        var step = e.Delta > 0 ? 0.1 : -0.1;
        _controller.SetPaperTextZoom(_paper, _paper.TextZoom + step);
        e.Handled = true;
    }

    private void OpenMarkdownInDefaultEditor()
    {
        if (_paper.Type != PaperTypes.Note)
        {
            return;
        }

        try
        {
            var path = WriteExternalMarkdownFile();
            Process.Start(new ProcessStartInfo(path)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                Strings.Format("OpenMarkdownFailureMessage", CurrentExternalMarkdownExtension(), ex.Message),
                Strings.Get("OpenMarkdownFailureTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    public void UpdateExternalMarkdownExtension()
    {
        if (_openMarkdownButton != null)
        {
            _openMarkdownButton.ToolTip = OpenMarkdownEditorToolTip();
            _openMarkdownButton.Visibility = _controller.State.ShowTopBarExternalOpenButton ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    public void UpdateTopBarNewPaperButtons()
    {
        if (_newTodoButton != null)
        {
            _newTodoButton.Visibility = _controller.State.ShowTopBarNewTodoButton ? Visibility.Visible : Visibility.Collapsed;
        }
        if (_newNoteButton != null)
        {
            _newNoteButton.Visibility = _controller.State.ShowTopBarNewNoteButton ? Visibility.Visible : Visibility.Collapsed;
        }
        if (_openMarkdownButton != null)
        {
            _openMarkdownButton.Visibility = _controller.State.ShowTopBarExternalOpenButton ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private string OpenMarkdownEditorToolTip()
    {
        return Strings.Format("ToolTipOpenMarkdownEditor", CurrentExternalMarkdownExtension());
    }

    private string CurrentExternalMarkdownExtension()
    {
        return ExternalMarkdownFileExtensions.Normalize(_controller.State.ExternalMarkdownExtension);
    }

    private string WriteExternalMarkdownFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PaperTodo");
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, $"paper-{_paper.Id}{CurrentExternalMarkdownExtension()}");
        var text = _noteBox?.Text ?? _paper.Content ?? "";
        File.WriteAllText(path, text, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    private void ConfirmAndDeletePaper()
    {
        if (ShowDeletePaperDialog())
        {
            _controller.DeletePaper(_paper);
        }
    }

    private bool ShowDeletePaperDialog()
    {
        var dialog = new Window
        {
            Owner = this,
            Title = Strings.Get("DeletePaperTitle"),
            Width = 300,
            Height = 178,
            MinWidth = 300,
            MinHeight = 178,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            Topmost = Topmost
        };

        var root = new Border
        {
            CornerRadius = new CornerRadius(16),
            BorderBrush = PaperBorderBrush,
            BorderThickness = new Thickness(1),
            Background = PaperBrush,
            Padding = new Thickness(18),
            Effect = new DropShadowEffect
            {
                BlurRadius = 18,
                ShadowDepth = 2,
                Opacity = 0.22
            }
        };

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = new TextBlock
        {
            Text = Strings.Get("DeletePaperQuestion"),
            Foreground = TextBrush,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        };

        var message = new TextBlock
        {
            Text = Strings.Get("DeletePaperBody"),
            Foreground = WeakTextBrush,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 20,
            Margin = new Thickness(0, 2, 0, 0)
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };

        var delete = DialogButton(Strings.Get("MenuDelete"), isDanger: true);
        delete.Click += (_, _) => dialog.DialogResult = true;

        buttons.Children.Add(delete);

        var cancel = DialogButton(Strings.Get("CommonCancel"), isDanger: false);
        cancel.IsCancel = true;
        cancel.Margin = new Thickness(8, 0, 0, 0);
        cancel.Click += (_, _) => dialog.DialogResult = false;

        buttons.Children.Add(cancel);

        Grid.SetRow(title, 0);
        Grid.SetRow(message, 1);
        Grid.SetRow(buttons, 2);

        layout.Children.Add(title);
        layout.Children.Add(message);
        layout.Children.Add(buttons);

        root.Child = layout;
        dialog.Content = root;

        return dialog.ShowDialog() == true;
    }

    private static Button DialogButton(string text, bool isDanger)
    {
        var background = isDanger
            ? FrozenBrush(Color.FromRgb(126, 70, 48))
            : FrozenBrush(Color.FromArgb(28, 120, 92, 48));

        var foreground = isDanger ? PaperBrush : TextBrush;
        var hover = isDanger
            ? FrozenBrush(Color.FromRgb(148, 78, 52))
            : FrozenBrush(Color.FromArgb(46, 120, 92, 48));

        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(16, 7, 16, 7)));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        style.Setters.Add(new Setter(Control.BackgroundProperty, background));
        style.Setters.Add(new Setter(Control.ForegroundProperty, foreground));
        style.Setters.Add(new Setter(Control.FontSizeProperty, 13.0));
        style.Setters.Add(new Setter(Control.CursorProperty, Cursors.Hand));
        style.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, 72.0));

        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "Bd";
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));

        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ContentControl.ContentProperty));
        content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(content);

        var template = new ControlTemplate(typeof(Button))
        {
            VisualTree = border
        };

        var mouseOver = new Trigger
        {
            Property = UIElement.IsMouseOverProperty,
            Value = true
        };
        mouseOver.Setters.Add(new Setter(Control.BackgroundProperty, hover));

        var pressed = new Trigger
        {
            Property = ButtonBase.IsPressedProperty,
            Value = true
        };
        pressed.Setters.Add(new Setter(UIElement.OpacityProperty, 0.82));

        template.Triggers.Add(mouseOver);
        template.Triggers.Add(pressed);
        style.Setters.Add(new Setter(Control.TemplateProperty, template));

        return new Button
        {
            Content = text,
            Style = style
        };
    }

    private PaperItem AddItemAfter(PaperItem? after, string text, bool pushUndo = true)
    {
        if (pushUndo) PushUndoSnapshot();
        var ordered = OrderedItems().ToList();
        var index = after == null ? ordered.Count : ordered.FindIndex(i => i.Id == after.Id) + 1;
        if (index < 0) index = ordered.Count;

        var newItem = new PaperItem
        {
            Text = text,
            Done = false
        };

        ordered.Insert(index, newItem);
        _paper.Items = ordered;
        NormalizeTodoItems();
        NormalizeOrders();
        _controller.MarkDirty();

        return newItem;
    }

    private void RemoveItem(PaperItem item, bool rebuild = true, string? focusItemId = null)
    {
        PushUndoSnapshot();
        var fallbackFocus = focusItemId ?? PreviousItem(item)?.Id ?? NextItem(item)?.Id;

        _paper.Items.RemoveAll(i => i.Id == item.Id);

        if (_paper.Items.Count == 0)
        {
            var replacement = new PaperItem();
            _paper.Items.Add(replacement);
            fallbackFocus = replacement.Id;
        }

        NormalizeTodoItems();
        NormalizeOrders();
        _controller.MarkDirty();

        if (rebuild)
        {
            RebuildTodoRows(fallbackFocus);
        }
    }

    private void ClearDoneItems()
    {
        if (_paper.Type != PaperTypes.Todo)
        {
            return;
        }

        var focusedId = CurrentFocusedTodoItemId();
        var ordered = OrderedItems().ToList();
        if (!ordered.Any(i => i.Done))
        {
            return;
        }

        PushUndoSnapshot();
        ordered.RemoveAll(i => i.Done);

        if (ordered.Count == 0)
        {
            ordered.Add(new PaperItem());
        }

        _paper.Items = ordered;
        NormalizeTodoItems();
        NormalizeOrders();

        var focus = ordered.FirstOrDefault(i => i.Id == focusedId)?.Id
            ?? ordered.FirstOrDefault(i => !IsBlank(i))?.Id
            ?? ordered.FirstOrDefault()?.Id;

        _controller.MarkDirty();
        RebuildTodoRows(focus);
    }







    private PaperItem? PreviousItem(PaperItem item)
    {
        var ordered = OrderedItems().ToList();
        var index = ordered.FindIndex(i => i.Id == item.Id);
        return index > 0 ? ordered[index - 1] : null;
    }

    private PaperItem? NextItem(PaperItem item)
    {
        var ordered = OrderedItems().ToList();
        var index = ordered.FindIndex(i => i.Id == item.Id);
        return index >= 0 && index < ordered.Count - 1 ? ordered[index + 1] : null;
    }

    private void BeginTodoMouseDrag()
    {
        if (_todoDrag == null)
        {
            return;
        }

        _todoDrag.IsDragging = true;

        var rowOrigin = _todoDrag.SourceRow.TranslatePoint(new Point(0, 0), this);
        _todoDrag.MouseOffsetInRow = new Point(
            Math.Max(0, _todoDrag.StartPoint.X - rowOrigin.X),
            Math.Max(0, _todoDrag.StartPoint.Y - rowOrigin.Y));

        _todoDrag.SourceRow.Opacity = 0.25;
        _todoDrag.SourceRow.Background = HoverBrush;
        _todoDrag.Handle.Opacity = 0.9;
        Mouse.OverrideCursor = Cursors.SizeAll;

        _todoDrag.Ghost = CreateTodoDragGhost(_todoDrag);
        _dragLayer?.Children.Add(_todoDrag.Ghost);
        UpdateTodoDragGhost(_todoDrag, _todoDrag.StartPoint);

        ShowAppendAreaAsTrashBin(active: true);
    }

    private void OnWindowPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_todoDrag == null)
        {
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            EndTodoMouseDrag(commit: _todoDrag.IsDragging);
            e.Handled = true;
            return;
        }

        var current = e.GetPosition(this);

        if (!_todoDrag.IsDragging)
        {
            var movedEnough =
                Math.Abs(current.X - _todoDrag.StartPoint.X) >= SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(current.Y - _todoDrag.StartPoint.Y) >= SystemParameters.MinimumVerticalDragDistance;

            if (!movedEnough)
            {
                return;
            }

            BeginTodoMouseDrag();
        }

        var panelPoint = _todoPanel != null ? e.GetPosition(_todoPanel) : current;
        UpdateTodoMouseDrag(panelPoint, current);
        e.Handled = true;
    }

    private void OnWindowPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_todoDrag == null)
        {
            return;
        }

        EndTodoMouseDrag(commit: _todoDrag.IsDragging);
        e.Handled = true;
    }

    private void UpdateTodoMouseDrag(Point pointOnPanel, Point pointOnWindow)
    {
        if (_todoDrag == null || _todoPanel == null)
        {
            return;
        }

        UpdateTodoDragGhost(_todoDrag, pointOnWindow);
        ClearActiveDropIndicator();

        bool overTrash = false;
        if (_appendArea != null && _appendArea.IsVisible)
        {
            try
            {
                var transform = this.TransformToVisual(_appendArea);
                Point posInAppend = transform.Transform(pointOnWindow);
                if (posInAppend.X >= 0 && posInAppend.X <= _appendArea.ActualWidth &&
                    posInAppend.Y >= 0 && posInAppend.Y <= _appendArea.ActualHeight)
                {
                    overTrash = true;
                }
            }
            catch
            {
                // Fallback in case layout is not fully updated
            }
        }

        if (overTrash)
        {
            _todoDrag.TargetId = null;
            _todoDrag.DropAtEnd = true;
            ShowAppendAreaAsTrashBin(active: true, hovered: true);
            return;
        }

        ShowAppendAreaAsTrashBin(active: true, hovered: false);

        var candidates = _todoRows
            .Where(row => row.Tag is string id && id != _todoDrag.ItemId)
            .ToList();

        if (candidates.Count == 0)
        {
            _todoDrag.TargetId = null;
            _todoDrag.DropAtEnd = false;
            return;
        }

        double bestDist = double.MaxValue;
        Border? bestRow = null;
        var bestPlacement = DropPlacement.After;

        foreach (var row in candidates)
        {
            double top = row.TranslatePoint(new Point(0, 0), _todoPanel).Y;
            ConsiderDropBoundary(row, DropPlacement.Before, top);
            ConsiderDropBoundary(row, DropPlacement.After, top + row.ActualHeight);
        }

        if (bestRow == null)
        {
            _todoDrag.TargetId = null;
            _todoDrag.DropAtEnd = false;
            return;
        }

        ShowDropIndicator(bestRow, bestPlacement);
        _todoDrag.TargetId = bestRow.Tag as string;
        _todoDrag.TargetPlacement = bestPlacement;
        _todoDrag.DropAtEnd = false;

        void ConsiderDropBoundary(Border row, DropPlacement placement, double y)
        {
            double dist = Math.Abs(pointOnPanel.Y - y);
            if (dist >= bestDist)
            {
                return;
            }

            bestDist = dist;
            bestRow = row;
            bestPlacement = placement;
        }
    }

    private Border CreateTodoDragGhost(TodoDragState state)
    {
        var item = _paper.Items.FirstOrDefault(i => i.Id == state.ItemId);
        var text = item?.Text ?? "";
        var done = item?.Done == true;

        var ghost = new Border
        {
            Width = Math.Max(state.SourceRow.ActualWidth, 160),
            MinHeight = Math.Max(state.SourceRow.ActualHeight, 30),
            Padding = new Thickness(2),
            CornerRadius = new CornerRadius(9),
            Background = FrozenBrush(Color.FromRgb(255, 250, 238)),
            BorderBrush = FrozenBrush(Color.FromArgb(150, 126, 84, 34)),
            BorderThickness = new Thickness(1),
            Opacity = 0.65,
            IsHitTestVisible = false,
            Effect = new DropShadowEffect
            {
                BlurRadius = 18,
                ShadowDepth = 3,
                Opacity = 0.24
            }
        };

        var grid = new Grid
        {
            IsHitTestVisible = false
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });

        var check = new TextBlock
        {
            Text = done ? "☑" : "☐",
            Foreground = done ? WeakTextBrush : TextBrush,
            FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.78
        };
        Grid.SetColumn(check, 0);
        grid.Children.Add(check);

        var content = new TextBlock
        {
            Text = text,
            Foreground = done ? WeakTextBrush : TextBrush,
            FontSize = 14,
            Padding = new Thickness(2, 3, 2, 3),
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };

        if (done)
        {
            content.TextDecorations = TextDecorations.Strikethrough;
        }

        Grid.SetColumn(content, 1);
        grid.Children.Add(content);

        var handle = new TextBlock
        {
            Text = "≡",
            Foreground = WeakTextBrush,
            Opacity = 0.58,
            FontSize = 15,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(handle, 2);
        grid.Children.Add(handle);

        ghost.Child = grid;
        return ghost;
    }

    private void CloseTodoDragGhost(TodoDragState state)
    {
        if (state.Ghost == null)
        {
            return;
        }

        _dragLayer?.Children.Remove(state.Ghost);
        state.Ghost = null;
    }

    private static void UpdateTodoDragGhost(TodoDragState state, Point pointOnWindow)
    {
        if (state.Ghost == null)
        {
            return;
        }

        Canvas.SetLeft(state.Ghost, pointOnWindow.X - state.MouseOffsetInRow.X);
        Canvas.SetTop(state.Ghost, pointOnWindow.Y - state.MouseOffsetInRow.Y);
    }

    private void EndTodoMouseDrag(bool commit)
    {
        var state = _todoDrag;
        if (state == null)
        {
            return;
        }

        _todoDrag = null;

        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }
        Mouse.OverrideCursor = null;

        CloseTodoDragGhost(state);

        state.SourceRow.Opacity = 1.0;
        state.SourceRow.Background = Brushes.Transparent;
        state.Handle.Opacity = 1.0;

        ClearActiveDropIndicator();
        ShowAppendAreaAsTrashBin(active: false);

        if (!commit)
        {
            RebuildTodoRows(state.ItemId);
            return;
        }

        if (state.DropAtEnd)
        {
            var item = _paper.Items.FirstOrDefault(i => i.Id == state.ItemId);
            if (item != null)
            {
                RemoveItem(item, rebuild: true);
            }
            return;
        }

        if (!string.IsNullOrWhiteSpace(state.TargetId))
        {
            MoveItem(state.ItemId, state.TargetId, state.TargetPlacement, focusDragged: true);
            return;
        }

        RebuildTodoRows(state.ItemId);
    }

    private void MoveItemBefore(string draggedId, string targetId, bool focusDragged = true)
    {
        MoveItem(draggedId, targetId, DropPlacement.Before, focusDragged);
    }

    private void MoveItemAfter(string draggedId, string targetId, bool focusDragged = true)
    {
        MoveItem(draggedId, targetId, DropPlacement.After, focusDragged);
    }

    private void MoveItem(string draggedId, string targetId, DropPlacement placement, bool focusDragged)
    {
        if (draggedId == targetId)
        {
            return;
        }

        var ordered = OrderedItems().ToList();
        var originalOrder = ordered.Select(i => i.Id).ToList();

        var dragged = ordered.FirstOrDefault(i => i.Id == draggedId);
        var target = ordered.FirstOrDefault(i => i.Id == targetId);

        if (dragged == null || target == null)
        {
            return;
        }

        ordered.Remove(dragged);

        var targetIndex = ordered.IndexOf(target);
        if (targetIndex < 0)
        {
            return;
        }

        if (placement == DropPlacement.After)
        {
            targetIndex++;
        }

        targetIndex = Math.Clamp(targetIndex, 0, ordered.Count);
        ordered.Insert(targetIndex, dragged);

        if (originalOrder.SequenceEqual(ordered.Select(i => i.Id)))
        {
            return;
        }

        PushUndoSnapshot();
        _paper.Items = ordered;
        NormalizeTodoItems();
        NormalizeOrders();
        _controller.MarkDirty();

        RebuildTodoRows(focusDragged ? dragged.Id : null);
    }

    private void MoveItemToEnd(string draggedId, bool focusDragged = true)
    {
        var ordered = OrderedItems().ToList();
        var dragged = ordered.FirstOrDefault(i => i.Id == draggedId);
        if (dragged == null)
        {
            return;
        }

        var oldIndex = ordered.IndexOf(dragged);
        if (oldIndex == ordered.Count - 1)
        {
            return;
        }

        PushUndoSnapshot();
        ordered.Remove(dragged);
        ordered.Add(dragged);

        _paper.Items = ordered;
        NormalizeTodoItems();
        NormalizeOrders();
        _controller.MarkDirty();
        RebuildTodoRows(focusDragged ? dragged.Id : null);
    }

    private IEnumerable<PaperItem> OrderedItems()
    {
        return _paper.Items.OrderBy(i => i.Order).ToList();
    }

    private void NormalizeTodoItems()
    {
        if (_paper.Type != PaperTypes.Todo)
        {
            return;
        }

        var ordered = _paper.Items.ToList();
        if (ordered.Count == 0)
        {
            ordered.Add(new PaperItem());
        }

        _paper.Items = ordered;
    }

    private static bool IsBlank(PaperItem item)
    {
        return string.IsNullOrWhiteSpace(item.Text);
    }

    private string? CurrentFocusedTodoItemId()
    {
        var focused = FocusManager.GetFocusedElement(this);

        if (focused is TodoTextBox box)
        {
            foreach (var pair in _todoEditors)
            {
                if (ReferenceEquals(pair.Value, box))
                {
                    return pair.Key;
                }
            }
        }

        return null;
    }

    private void NormalizeOrders()
    {
        // Preserve the current list order. Sorting here would undo freshly inserted
        // or dragged rows because new items start with Order = 0 until we renumber them.
        for (var i = 0; i < _paper.Items.Count; i++)
        {
            _paper.Items[i].Order = i;
        }
    }

    private void ShowDropIndicator(Border row, DropPlacement placement)
    {
        if (!Equals(_activeDropRow, row))
        {
            ClearActiveDropIndicator();
            _activeDropRow = row;
        }

        if (_dragLayer == null)
        {
            return;
        }

        if (_dropIndicatorLine == null)
        {
            _dropIndicatorLine = new Border
            {
                Height = 3,
                CornerRadius = new CornerRadius(1.5),
                Background = DropIndicatorBrush,
                IsHitTestVisible = false
            };
            Panel.SetZIndex(_dropIndicatorLine, 1001);
            _dragLayer.Children.Add(_dropIndicatorLine);
        }

        _dropIndicatorLine.Background = DropIndicatorBrush;
        var rowOrigin = row.TranslatePoint(new Point(0, 0), _dragLayer);
        var y = placement == DropPlacement.Before
            ? rowOrigin.Y
            : rowOrigin.Y + row.ActualHeight;
        var width = Math.Max(24, row.ActualWidth - 8);

        _dropIndicatorLine.Width = width;
        Canvas.SetLeft(_dropIndicatorLine, rowOrigin.X + 4);
        Canvas.SetTop(_dropIndicatorLine, y - (_dropIndicatorLine.Height / 2));
    }

    private void ClearDropIndicator(Border row)
    {
        if (Equals(_activeDropRow, row))
        {
            _activeDropRow = null;
        }

        row.BorderThickness = new Thickness(0, 2, 0, 2);
        row.BorderBrush = Brushes.Transparent;
        row.Padding = new Thickness(2);

        if (_dropIndicatorLine != null)
        {
            _dragLayer?.Children.Remove(_dropIndicatorLine);
            _dropIndicatorLine = null;
        }
    }

    private void ClearActiveDropIndicator()
    {
        if (_activeDropRow != null)
        {
            ClearDropIndicator(_activeDropRow);
            _activeDropRow = null;
        }
    }

    private static void ReplaceSelection(TextBox box, string replacement)
    {
        box.SelectedText = replacement;
        box.SelectionStart = box.SelectionStart + replacement.Length;
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_closeForReal)
        {
            return;
        }

        e.Cancel = true;
        _controller.HidePaper(_paper);
    }

    private void OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_todoDrag != null && Mouse.LeftButton != MouseButtonState.Pressed)
        {
            EndTodoMouseDrag(commit: false);
        }
    }

    private static DependencyObject? GetSafeParent(DependencyObject current)
    {
        if (current is Visual || current is System.Windows.Media.Media3D.Visual3D)
        {
            return VisualTreeHelper.GetParent(current);
        }

        if (current is FrameworkContentElement fce)
        {
            return fce.Parent;
        }

        if (current is ContentElement ce)
        {
            return ContentOperations.GetParent(ce);
        }

        return null;
    }

    private static Button IconButton(string text, string tooltip)
    {
        return new Button
        {
            Content = text,
            ToolTip = tooltip,
            Width = 28,
            Height = 24,
            Margin = new Thickness(1, 0, 1, 0),
            Style = SharedIconButtonStyle
        };
    }

    private static ContextMenu CreateContextMenu()
    {
        var menu = new ContextMenu
        {
            Padding = new Thickness(4, 4, 4, 4),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 13,
            HasDropShadow = true,
            Template = SharedContextMenuTemplate
        };
        menu.SetResourceReference(Control.BackgroundProperty, "PaperBrushKey");
        menu.SetResourceReference(Control.BorderBrushProperty, "PaperBorderBrushKey");
        menu.SetResourceReference(Control.ForegroundProperty, "TextBrushKey");

        menu.Resources.Add(typeof(MenuItem), SharedCompactMenuItemStyle);
        return menu;
    }

    private static Separator MenuSeparator()
    {
        return new Separator
        {
            Margin = new Thickness(8, 3, 8, 3),
            Opacity = 0.38
        };
    }

    private static MenuItem MenuHeader(string header)
    {
        var item = new MenuItem
        {
            Header = header,
            IsEnabled = false,
            Padding = new Thickness(8, 2, 10, 2),
            Background = Brushes.Transparent,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold
        };
        item.SetResourceReference(Control.ForegroundProperty, "WeakTextBrushKey");
        return item;
    }

    private static MenuItem MenuItem(string header, RoutedEventHandler click)
    {
        var item = new MenuItem
        {
            Header = header,
            Padding = new Thickness(8, 4, 10, 4),
            Background = Brushes.Transparent
        };
        item.SetResourceReference(Control.ForegroundProperty, "TextBrushKey");
        item.Click += click;
        return item;
    }

    private static List<PaperItem> CloneItems(List<PaperItem> items)
    {
        return items.Select(i => new PaperItem
        {
            Id = i.Id,
            Text = i.Text,
            Done = i.Done,
            Order = i.Order
        }).ToList();
    }

    private void PushUndoSnapshot()
    {
        CommitFocusedTextIfNeeded();

        _undoStack.Add(CloneItems(_paper.Items));
        if (_undoStack.Count > MaxUndoDepth)
        {
            _undoStack.RemoveAt(0);
        }
        _redoStack.Clear();
    }

    private void CommitFocusedTextIfNeeded()
    {
        var focusedId = CurrentFocusedTodoItemId();
        if (focusedId != null && _todoEditors.TryGetValue(focusedId, out var box))
        {
            if (_activeOriginalItemId == focusedId && _activeOriginalText != null && box.Text != _activeOriginalText)
            {
                var item = _paper.Items.FirstOrDefault(i => i.Id == focusedId);
                if (item != null)
                {
                    var oldText = item.Text;
                    item.Text = _activeOriginalText;

                    var oldSnapshot = CloneItems(_paper.Items);
                    _undoStack.Add(oldSnapshot);
                    if (_undoStack.Count > MaxUndoDepth)
                    {
                        _undoStack.RemoveAt(0);
                    }

                    item.Text = oldText;
                    _activeOriginalText = oldText;
                }
            }
        }
    }

    private void Undo()
    {
        if (_undoStack.Count == 0)
        {
            return;
        }

        var focusedId = CurrentFocusedTodoItemId();

        var currentItems = CloneItems(_paper.Items);
        _redoStack.Add(currentItems);

        var previousItems = _undoStack[^1];
        _undoStack.RemoveAt(_undoStack.Count - 1);

        _paper.Items = previousItems;
        NormalizeTodoItems();
        NormalizeOrders();
        _controller.MarkDirty();

        RebuildTodoRows(focusedId);
    }

    private void Redo()
    {
        if (_redoStack.Count == 0)
        {
            return;
        }

        var focusedId = CurrentFocusedTodoItemId();

        var currentItems = CloneItems(_paper.Items);
        _undoStack.Add(currentItems);

        var nextItems = _redoStack[^1];
        _redoStack.RemoveAt(_redoStack.Count - 1);

        _paper.Items = nextItems;
        NormalizeTodoItems();
        NormalizeOrders();
        _controller.MarkDirty();

        RebuildTodoRows(focusedId);
    }

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_paper.Type == PaperTypes.Note)
        {
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (e.Key == Key.Z)
            {
                var focusedId = CurrentFocusedTodoItemId();
                if (focusedId != null && _todoEditors.TryGetValue(focusedId, out var box))
                {
                    if (box.CanUndo)
                    {
                        return;
                    }
                }

                Undo();
                e.Handled = true;
            }
            else if (e.Key == Key.Y)
            {
                var focusedId = CurrentFocusedTodoItemId();
                if (focusedId != null && _todoEditors.TryGetValue(focusedId, out var box))
                {
                    if (box.CanRedo)
                    {
                        return;
                    }
                }

                Redo();
                e.Handled = true;
            }
        }
    }

    public static readonly DependencyProperty TransitionProgressProperty =
        DependencyProperty.Register(
            nameof(TransitionProgress),
            typeof(double),
            typeof(PaperWindow),
            new PropertyMetadata(0.0, OnTransitionProgressChanged));

    public double TransitionProgress
    {
        get => (double)GetValue(TransitionProgressProperty);
        set => SetValue(TransitionProgressProperty, value);
    }

    private static readonly DependencyProperty DeepCapsuleAnimatedLeftProperty =
        DependencyProperty.Register(
            nameof(DeepCapsuleAnimatedLeft),
            typeof(double),
            typeof(PaperWindow),
            new PropertyMetadata(double.NaN, OnDeepCapsuleAnimatedLeftChanged));

    private double DeepCapsuleAnimatedLeft
    {
        get => (double)GetValue(DeepCapsuleAnimatedLeftProperty);
        set => SetValue(DeepCapsuleAnimatedLeftProperty, value);
    }

    private static void OnTransitionProgressChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PaperWindow window)
        {
            window.UpdateTransitionVisuals((double)e.NewValue);
        }
    }

    private static void OnDeepCapsuleAnimatedLeftChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PaperWindow window || e.NewValue is not double left || double.IsNaN(left) || double.IsInfinity(left))
        {
            return;
        }

        window.MoveWindowWithoutGeometrySave(() => window.Left = window.RoundToDevicePixelX(left));
    }

    private void UpdateTransitionVisuals(double progress)
    {
        if (!_isTransitionVisualsActive)
        {
            return;
        }

        var currentProgress = double.IsNaN(progress) || double.IsInfinity(progress)
            ? 0.0
            : Math.Clamp(progress, 0.0, 1.0);

        var visualWidth = _startTransitionWidth + (_targetTransitionWidth - _startTransitionWidth) * currentProgress;
        var visualHeight = _startTransitionHeight + (_targetTransitionHeight - _startTransitionHeight) * currentProgress;
        var visualChromeWidth = Math.Max(1.0, visualWidth - WindowChromeInset);
        var visualChromeHeight = Math.Max(1.0, visualHeight - WindowChromeInset);
        var baseChromeWidth = Math.Max(1.0, _transitionBaseWidth - WindowChromeInset);
        var baseChromeHeight = Math.Max(1.0, _transitionBaseHeight - WindowChromeInset);

        _paperChrome.HorizontalAlignment = HorizontalAlignment.Left;
        _paperChrome.VerticalAlignment = VerticalAlignment.Top;
        _paperChrome.Width = visualChromeWidth;
        _paperChrome.Height = visualChromeHeight;
        _shellScale.ScaleX = Math.Max(0.01, visualChromeWidth / baseChromeWidth);
        _shellScale.ScaleY = Math.Max(0.01, visualChromeHeight / baseChromeHeight);
        UpdateTransitionCornerRadius(visualChromeWidth, visualChromeHeight, baseChromeWidth, baseChromeHeight);
    }

    private void ResetTransitionVisuals()
    {
        _isTransitionVisualsActive = false;
        _paperChrome.Width = double.NaN;
        _paperChrome.Height = double.NaN;
        _paperChrome.HorizontalAlignment = HorizontalAlignment.Stretch;
        _paperChrome.VerticalAlignment = VerticalAlignment.Stretch;
        _shellScale.ScaleX = 1.0;
        _shellScale.ScaleY = 1.0;
        _paperChrome.CornerRadius = PaperChromeCornerRadiusForState(_paper.IsCollapsed && _controller.State.UseCapsuleMode);
    }

    private void UpdateTransitionCornerRadius(
        double visualChromeWidth,
        double visualChromeHeight,
        double baseChromeWidth,
        double baseChromeHeight)
    {
        var visualChromeMin = Math.Min(visualChromeWidth, visualChromeHeight);
        var expandedChromeMin = Math.Max(1.0, Math.Min(baseChromeWidth, baseChromeHeight));
        var capsuleChromeMin = Math.Max(
            1.0,
            Math.Min(
                PaperLayoutDefaults.CapsuleWidth - WindowChromeInset,
                PaperLayoutDefaults.CapsuleHeight - WindowChromeInset));
        var compactRange = Math.Max(1.0, expandedChromeMin - capsuleChromeMin);
        var compactness = Math.Clamp((expandedChromeMin - visualChromeMin) / compactRange, 0.0, 1.0);
        var compactVisualRadius = Math.Min(CapsuleChromeCornerRadius, visualChromeMin / 2.0);
        var desiredVisualRadius = ExpandedChromeCornerRadius + (compactVisualRadius - ExpandedChromeCornerRadius) * compactness;

        _paperChrome.CornerRadius = new CornerRadius(desiredVisualRadius);
    }

    private static CornerRadius PaperChromeCornerRadiusForState(bool collapsed)
    {
        return new CornerRadius(collapsed ? CapsuleChromeCornerRadius : ExpandedChromeCornerRadius);
    }

    private double CapsuleWindowWidth()
    {
        return Math.Max(PaperLayoutDefaults.CapsuleWidth, CapsuleShellWidth() + WindowChromeInset);
    }

    private double CapsuleShellWidth()
    {
        const double iconWidth = 14;
        const double iconGap = 6;
        const double leftPadding = 10;
        const double closeWidth = 26;
        const double rightPadding = 4;

        return Math.Ceiling(leftPadding + iconWidth + iconGap + MeasureCapsuleTitleWidth() + closeWidth + rightPadding);
    }

    private double MeasureCapsuleTitleWidth()
    {
        var text = _controller.PaperCapsuleTitle(_paper);
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
                new Typeface(NoteTypography.FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                11,
                WeakTextBrush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            return formatted.WidthIncludingTrailingWhitespace;
        }
        catch
        {
            return text.Length * 12;
        }
    }

    private int CapsuleTitleLength()
    {
        var text = _controller.PaperCapsuleTitle(_paper);
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        try
        {
            return StringInfo.ParseCombiningCharacters(text).Length;
        }
        catch
        {
            return text.Length;
        }
    }

    private double RoundToDevicePixelX(double value)
    {
        return RoundToDevicePixel(value, VisualTreeHelper.GetDpi(this).DpiScaleX);
    }

    private double RoundToDevicePixelY(double value)
    {
        return RoundToDevicePixel(value, VisualTreeHelper.GetDpi(this).DpiScaleY);
    }

    private static double RoundToDevicePixel(double value, double scale)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || scale <= 0)
        {
            return value;
        }

        return Math.Round(value * scale, MidpointRounding.AwayFromZero) / scale;
    }

    private void SaveGeometryIfAllowed()
    {
        if (_isApplyingCollapsedState || SuppressGeometrySave)
        {
            return;
        }

        _controller.UpdateGeometry(_paper, this);
    }

    private void MoveWindowWithoutGeometrySave(Action move)
    {
        var wasSuppressing = _suppressGeometrySave;
        _suppressGeometrySave = true;
        try
        {
            move();
        }
        finally
        {
            _suppressGeometrySave = wasSuppressing;
        }
    }

    private void ClearDeepCapsulePositionAnimation()
    {
        BeginAnimation(DeepCapsuleAnimatedLeftProperty, null);
        BeginAnimation(Window.LeftProperty, null);
    }

    private static Rect DeepCapsuleWorkArea()
    {
        return SystemParameters.WorkArea;
    }

    private double DeepCapsuleTopForIndex(int index)
    {
        var area = DeepCapsuleWorkArea();
        var desiredTop = area.Top + DeepCapsuleStartTopMargin + Math.Max(0, index) * (PaperLayoutDefaults.CapsuleHeight + DeepCapsuleGap);
        var maxTop = Math.Max(area.Top + DeepCapsuleTopMargin, area.Bottom - PaperLayoutDefaults.CapsuleHeight - DeepCapsuleTopMargin);
        return Math.Min(desiredTop, maxTop);
    }

    private void MoveDeepCapsuleToCurrentTarget(bool animate = false)
    {
        if (!_isDeepCapsulePlaced)
        {
            return;
        }

        var area = DeepCapsuleWorkArea();
        var capsuleWidth = CapsuleWindowWidth();
        var deepCapsuleVisibleWidth = Math.Clamp(42 + CapsuleTitleLength() * 8, 52, capsuleWidth - 12);
        var targetLeft = RoundToDevicePixelX(_isDeepCapsuleHovering
            ? area.Right - capsuleWidth + DeepCapsuleHoverOutsideOffset
            : area.Right - deepCapsuleVisibleWidth);
        var targetTop = RoundToDevicePixelY(DeepCapsuleTopForIndex(_deepCapsuleIndex));

        MoveWindowWithoutGeometrySave(() =>
        {
            Top = targetTop;
            Width = capsuleWidth;
            Height = PaperLayoutDefaults.CapsuleHeight;
        });

        if (!animate)
        {
            ClearDeepCapsulePositionAnimation();
            MoveWindowWithoutGeometrySave(() => Left = targetLeft);
            return;
        }

        var currentLeft = double.IsNaN(Left) || double.IsInfinity(Left) ? targetLeft : RoundToDevicePixelX(Left);
        if (Math.Abs(currentLeft - targetLeft) < 0.5)
        {
            ClearDeepCapsulePositionAnimation();
            MoveWindowWithoutGeometrySave(() => Left = targetLeft);
            return;
        }

        var expectedHovering = _isDeepCapsuleHovering;
        var leftAnimation = new System.Windows.Media.Animation.DoubleAnimation
        {
            From = currentLeft,
            To = targetLeft,
            Duration = TimeSpan.FromMilliseconds(expectedHovering ? 160 : 130),
            EasingFunction = new System.Windows.Media.Animation.CubicEase
            {
                EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut
            }
        };
        leftAnimation.Completed += (_, _) =>
        {
            if (!_isDeepCapsulePlaced || _isDeepCapsuleHovering != expectedHovering)
            {
                return;
            }

            MoveWindowWithoutGeometrySave(() =>
            {
                ClearDeepCapsulePositionAnimation();
                Left = targetLeft;
            });
        };

        BeginAnimation(DeepCapsuleAnimatedLeftProperty, leftAnimation, System.Windows.Media.Animation.HandoffBehavior.SnapshotAndReplace);
    }

    private void SetDeepCapsuleHover(bool hovering)
    {
        if (!_isDeepCapsulePlaced || !_paper.IsCollapsed || !_controller.State.UseDeepCapsuleMode)
        {
            return;
        }

        _isDeepCapsuleHovering = hovering;
        MoveDeepCapsuleToCurrentTarget(animate: true);
    }

    public void ApplyDeepCapsulePlacement(int index)
    {
        if (!_paper.IsCollapsed || !_controller.State.UseCapsuleMode || !_controller.State.UseDeepCapsuleMode)
        {
            ClearDeepCapsulePlacement();
            return;
        }

        _isDeepCapsulePlaced = true;
        _deepCapsuleIndex = Math.Max(0, index);
        RefreshCapsuleLabel();
        MoveDeepCapsuleToCurrentTarget();
        RefreshEffectiveTopmost();
    }

    public void ClearDeepCapsulePlacement(bool restoreCollapsedPosition = true)
    {
        var wasPlaced = _isDeepCapsulePlaced;
        ClearDeepCapsulePositionAnimation();
        _isDeepCapsulePlaced = false;
        _isDeepCapsuleHovering = false;
        _deepCapsuleIndex = -1;

        if (wasPlaced && restoreCollapsedPosition && _paper.IsCollapsed && IsVisible)
        {
            MoveWindowWithoutGeometrySave(() =>
            {
                Left = RoundToDevicePixelX(_paper.X);
                Top = RoundToDevicePixelY(_paper.Y);
                Width = CapsuleWindowWidth();
                Height = PaperLayoutDefaults.CapsuleHeight;
            });
        }
    }

    public void UpdateDeepCapsuleMode()
    {
        if (!_controller.State.UseCapsuleMode || !_controller.State.UseDeepCapsuleMode || !_paper.IsCollapsed)
        {
            ClearDeepCapsulePlacement();
        }
        else
        {
            MoveDeepCapsuleToCurrentTarget();
        }

        RefreshEffectiveTopmost();
    }

    private void AlignExpandedToRightEdge(double targetWidth, double targetHeight)
    {
        var area = DeepCapsuleWorkArea();
        var width = Math.Max(targetWidth, PaperLayoutDefaults.MinWidth);
        var height = Math.Max(targetHeight, PaperLayoutDefaults.MinHeight);
        var targetTop = Math.Clamp(Top, area.Top + DeepCapsuleTopMargin, Math.Max(area.Top + DeepCapsuleTopMargin, area.Bottom - height - DeepCapsuleTopMargin));

        Left = RoundToDevicePixelX(area.Right - width);
        Top = RoundToDevicePixelY(targetTop);
    }

    private void RegisterNameSafe(string name, object scopedElement)
    {
        try
        {
            UnregisterName(name);
        }
        catch
        {
            // Name may not exist yet.
        }

        try
        {
            RegisterName(name, scopedElement);
        }
        catch
        {
            // Duplicate names are non-fatal for this small UI.
        }
    }

    private static bool IsDescendantOf(DependencyObject? current, DependencyObject target)
    {
        while (current != null)
        {
            if (ReferenceEquals(current, target))
            {
                return true;
            }
            current = GetSafeParent(current);
        }
        return false;
    }

    private static bool IsScrollBarInteractionSource(DependencyObject? current, DependencyObject scope)
    {
        while (current != null)
        {
            if (current is ScrollBar or Thumb or Track or RepeatButton)
            {
                return true;
            }

            if (ReferenceEquals(current, scope))
            {
                return false;
            }

            current = GetSafeParent(current);
        }

        return false;
    }

    private void RefreshCloseButton()
    {
        if (_closeButton == null)
        {
            return;
        }

        if (_controller.State.UseCapsuleMode)
        {
            _closeButton.Content = "─";
            _closeButton.ToolTip = Strings.Get("ToolTipCollapseToCapsule");
        }
        else
        {
            _closeButton.Content = "×";
            _closeButton.ToolTip = Strings.Get("ToolTipHideThisPaper");
        }
    }

    public void UpdateCapsuleMode()
    {
        RefreshCloseButton();
        if (!_controller.State.UseCapsuleMode && _paper.IsCollapsed)
        {
            if (_isDeepCapsulePlaced)
            {
                ClearDeepCapsulePlacement();
            }
            SetCollapsedState(false, animate: true);
        }
        else
        {
            RefreshEffectiveTopmost();
        }

        _paperChrome.ContextMenu = BuildPaperContextMenu();
        UpdateTextZoom();
    }

    private void RefreshCapsuleLabel()
    {
        if (_capsuleLabelText == null)
        {
            return;
        }

        _capsuleLabelText.Text = _controller.PaperCapsuleTitle(_paper);
        _capsuleLabelText.ToolTip = _controller.PaperTitleText(_paper);
        if (_capsuleShell != null)
        {
            _capsuleShell.Width = CapsuleShellWidth();
        }
    }

    private void BuildCapsuleShell()
    {
        _capsuleShell = new Grid
        {
            Width = CapsuleShellWidth(),
            Height = 30,
            Background = Brushes.Transparent
        };
        _capsuleShell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _capsuleShell.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var leftArea = new Border
        {
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(10, 0, 0, 10),
            Cursor = Cursors.Hand
        };

        var leftStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var iconText = new TextBlock
        {
            Text = _paper.Type == PaperTypes.Note ? "✎" : "✓",
            Foreground = TextBrush,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        leftStack.Children.Add(iconText);

        _capsuleLabelText = new TextBlock
        {
            Foreground = WeakTextBrush,
            FontSize = 11,
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        RefreshCapsuleLabel();
        leftStack.Children.Add(_capsuleLabelText);

        leftArea.Child = leftStack;

        _capsuleShell.MouseEnter += (_, _) => SetDeepCapsuleHover(true);
        _capsuleShell.MouseLeave += (_, _) => SetDeepCapsuleHover(false);

        leftArea.MouseEnter += (_, _) => leftArea.Background = HoverBrush;
        leftArea.MouseLeave += (_, _) => leftArea.Background = Brushes.Transparent;

        leftArea.PreviewMouseLeftButtonDown += (s, e) =>
        {
            _mouseDownScreenPos = PointToScreen(e.GetPosition(this));
            _isMaybeDragging = true;
            leftArea.CaptureMouse();
            e.Handled = true;
        };

        leftArea.PreviewMouseMove += (s, e) =>
        {
            if (!_isMaybeDragging) return;
            if (_isDeepCapsulePlaced) return;

            Point currentScreenPos = PointToScreen(e.GetPosition(this));
            double deltaX = Math.Abs(currentScreenPos.X - _mouseDownScreenPos.X);
            double deltaY = Math.Abs(currentScreenPos.Y - _mouseDownScreenPos.Y);

            if (deltaX >= SystemParameters.MinimumHorizontalDragDistance ||
                deltaY >= SystemParameters.MinimumVerticalDragDistance)
            {
                _isMaybeDragging = false;
                leftArea.ReleaseMouseCapture();
                leftArea.Background = Brushes.Transparent;
                leftArea.Cursor = Cursors.SizeAll;

                try
                {
                    DragMove();
                }
                catch (InvalidOperationException)
                {
                    // Ignore if mouse state changed unexpectedly
                }
                finally
                {
                    leftArea.Cursor = Cursors.Hand;
                }

                e.Handled = true;
            }
        };

        leftArea.PreviewMouseLeftButtonUp += (s, e) =>
        {
            if (_isMaybeDragging)
            {
                _isMaybeDragging = false;
                leftArea.ReleaseMouseCapture();

                SetCollapsedState(false, alignExpandedToRight: _isDeepCapsulePlaced);
                e.Handled = true;
            }
        };

        leftArea.LostMouseCapture += (s, e) =>
        {
            _isMaybeDragging = false;
        };

        leftArea.ContextMenu = BuildPaperContextMenu();
        _capsuleLeftArea = leftArea;

        Grid.SetColumn(leftArea, 0);
        _capsuleShell.Children.Add(leftArea);

        var closeGlyph = new TextBlock
        {
            Text = "×",
            Foreground = WeakTextBrush,
            FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var capsuleClose = new Border
        {
            Width = 24,
            Margin = new Thickness(0, 0, 2, 0),
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(0, 10, 10, 0),
            Cursor = Cursors.Hand,
            ToolTip = Strings.Get("ToolTipHideThisPaper"),
            Child = closeGlyph
        };
        capsuleClose.MouseEnter += (_, _) =>
        {
            capsuleClose.Background = HoverBrush;
            closeGlyph.Foreground = TextBrush;
        };
        capsuleClose.MouseLeave += (_, _) =>
        {
            capsuleClose.Background = Brushes.Transparent;
            closeGlyph.Foreground = WeakTextBrush;
            capsuleClose.Opacity = 1.0;
        };
        capsuleClose.MouseLeftButtonDown += (_, e) =>
        {
            capsuleClose.Opacity = 0.72;
            e.Handled = true;
        };
        capsuleClose.MouseLeftButtonUp += (_, e) =>
        {
            capsuleClose.Opacity = 1.0;
            _controller.HidePaper(_paper);
            e.Handled = true;
        };

        Grid.SetColumn(capsuleClose, 1);
        _capsuleShell.Children.Add(capsuleClose);
    }

    public void SetCollapsedState(bool collapsed, bool animate = true, bool saveGeometry = true, bool alignExpandedToRight = false)
    {
        if (_paper.IsCollapsed == collapsed)
        {
            return;
        }

        if (_isApplyingCollapsedState)
        {
            // Capture current animated values to prevent snapping
            double currentWidth = Width;
            double currentHeight = Height;
            double currentShellOpacity = _shell.Opacity;
            double currentCapsuleOpacity = _capsuleShell.Opacity;

            // Set them as local values
            Width = currentWidth;
            Height = currentHeight;
            _shell.Opacity = currentShellOpacity;
            _capsuleShell.Opacity = currentCapsuleOpacity;

            // Clear ongoing animations safely
            BeginAnimation(TransitionProgressProperty, null);
            _shell.BeginAnimation(UIElement.OpacityProperty, null);
            _capsuleShell.BeginAnimation(UIElement.OpacityProperty, null);
            ResetTransitionVisuals();

            _shell.Width = double.NaN;
            _shell.Height = double.NaN;
            _isApplyingCollapsedState = false;
        }

        _isApplyingCollapsedState = true;

        var capsuleWidth = CapsuleWindowWidth();
        double targetWidth = collapsed ? capsuleWidth : _paper.Width;
        double targetHeight = collapsed ? PaperLayoutDefaults.CapsuleHeight : _paper.Height;
        double finalTargetWidth = RoundToDevicePixelX(targetWidth);
        double finalTargetHeight = RoundToDevicePixelY(targetHeight);
        var arrangeDeepCapsulesAfterCollapse = collapsed && _controller.State.UseCapsuleMode && _controller.State.UseDeepCapsuleMode;

        var wasDeepCapsulePlaced = _isDeepCapsulePlaced;

        _paper.IsCollapsed = collapsed;
        if (!collapsed)
        {
            ClearDeepCapsulePlacement(restoreCollapsedPosition: false);
            if (alignExpandedToRight || wasDeepCapsulePlaced)
            {
                MoveWindowWithoutGeometrySave(() => AlignExpandedToRightEdge(finalTargetWidth, finalTargetHeight));
            }
        }

        RefreshEffectiveTopmost();
        _controller.MarkDirty();

        if (collapsed)
        {
            RefreshCapsuleLabel();
            _capsuleShell.Visibility = Visibility.Visible;

            if (_paperChrome.Effect is System.Windows.Media.Effects.DropShadowEffect shadow)
            {
                shadow.BlurRadius = 8;
                shadow.Opacity = 0.08;
            }
        }
        else
        {
            _shell.Visibility = Visibility.Visible;

            if (_paper.Type == PaperTypes.Todo)
            {
                RebuildTodoRows();
            }

            if (_paperChrome.Effect is System.Windows.Media.Effects.DropShadowEffect shadow)
            {
                shadow.BlurRadius = 14;
                shadow.Opacity = 0.18;
            }
        }

        if (animate)
        {
            var expandedWidth = collapsed ? RoundToDevicePixelX(Width) : finalTargetWidth;
            var expandedHeight = collapsed ? RoundToDevicePixelY(Height) : finalTargetHeight;
            _transitionBaseWidth = expandedWidth;
            _transitionBaseHeight = expandedHeight;
            _startTransitionWidth = collapsed ? expandedWidth : capsuleWidth;
            _startTransitionHeight = collapsed ? expandedHeight : PaperLayoutDefaults.CapsuleHeight;
            _targetTransitionWidth = collapsed ? finalTargetWidth : expandedWidth;
            _targetTransitionHeight = collapsed ? finalTargetHeight : expandedHeight;
            _isTransitionVisualsActive = true;

            // Prevent shell content reflow/wrapping by locking its size to the expanded dimensions
            _shell.Width = Math.Max(0, expandedWidth - WindowChromeInset);
            _shell.Height = Math.Max(0, expandedHeight - WindowChromeInset);

            TransitionProgress = 0.0;
            UpdateTransitionVisuals(0.0);
            if (!collapsed)
            {
                Width = expandedWidth;
                Height = expandedHeight;
            }

            var easeOut = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };

            var progressAnim = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = 0.0,
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(collapsed ? CollapseResizeMilliseconds : ExpandAnimationMilliseconds),
                BeginTime = collapsed ? TimeSpan.FromMilliseconds(CollapseShellFadeMilliseconds) : TimeSpan.Zero,
                EasingFunction = easeOut
            };

            if (collapsed)
            {
                _shell.Opacity = 1.0;
                _capsuleShell.Opacity = 0.0;

                var fadeOutShell = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = 1.0,
                    To = 0.0,
                    Duration = TimeSpan.FromMilliseconds(CollapseShellFadeMilliseconds),
                    EasingFunction = easeOut
                };
                _shell.BeginAnimation(UIElement.OpacityProperty, fadeOutShell);

                var fadeInCapsule = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = 0.0,
                    To = 1.0,
                    Duration = TimeSpan.FromMilliseconds(CollapseResizeMilliseconds),
                    BeginTime = TimeSpan.FromMilliseconds(CollapseShellFadeMilliseconds),
                    EasingFunction = easeOut
                };
                _capsuleShell.BeginAnimation(UIElement.OpacityProperty, fadeInCapsule);
            }
            else
            {
                _capsuleShell.Opacity = 1.0;
                _shell.Opacity = 0.0;

                var fadeOutCapsule = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = 1.0,
                    To = 0.0,
                    Duration = TimeSpan.FromMilliseconds(80),
                    EasingFunction = easeOut
                };
                _capsuleShell.BeginAnimation(UIElement.OpacityProperty, fadeOutCapsule);

                var fadeInShell = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = 0.0,
                    To = 1.0,
                    Duration = TimeSpan.FromMilliseconds(140),
                    BeginTime = TimeSpan.FromMilliseconds(80),
                    EasingFunction = easeOut
                };
                _shell.BeginAnimation(UIElement.OpacityProperty, fadeInShell);
            }

            progressAnim.Completed += (s, e) =>
            {
                // 1. Set local values before clearing animations to prevent snapping/flicker
                TransitionProgress = 1.0;
                UpdateTransitionVisuals(1.0);

                if (collapsed)
                {
                    _shell.Opacity = 0.0;
                    _capsuleShell.Opacity = 1.0;
                    _shell.Visibility = Visibility.Collapsed;
                }
                else
                {
                    _capsuleShell.Opacity = 0.0;
                    _shell.Opacity = 1.0;
                    _capsuleShell.Visibility = Visibility.Collapsed;
                }

                if (collapsed)
                {
                    MinWidth = capsuleWidth;
                    MinHeight = PaperLayoutDefaults.CapsuleHeight;
                    ResizeMode = ResizeMode.NoResize;
                }
                else
                {
                    MinWidth = PaperLayoutDefaults.MinWidth;
                    MinHeight = PaperLayoutDefaults.MinHeight;
                    ResizeMode = ResizeMode.CanResizeWithGrip;
                }

                Width = finalTargetWidth;
                Height = finalTargetHeight;
                // Re-measure at the final window size before removing the visual scale.
                UpdateLayout();

                // 2. Clear animations
                BeginAnimation(TransitionProgressProperty, null);
                _shell.BeginAnimation(UIElement.OpacityProperty, null);
                _capsuleShell.BeginAnimation(UIElement.OpacityProperty, null);
                ResetTransitionVisuals();

                // 3. Unlock shell layout
                _shell.Width = double.NaN;
                _shell.Height = double.NaN;

                _isApplyingCollapsedState = false;
                if (saveGeometry)
                {
                    _controller.UpdateGeometry(_paper, this);
                }
                if (arrangeDeepCapsulesAfterCollapse)
                {
                    _controller.ArrangeDeepCapsules();
                }
            };

            BeginAnimation(TransitionProgressProperty, progressAnim);
        }
        else
        {
            BeginAnimation(TransitionProgressProperty, null);
            _shell.BeginAnimation(UIElement.OpacityProperty, null);
            _capsuleShell.BeginAnimation(UIElement.OpacityProperty, null);
            ResetTransitionVisuals();

            TransitionProgress = 0.0;

            if (collapsed)
            {
                _shell.Visibility = Visibility.Collapsed;
                _shell.Opacity = 0;
                _capsuleShell.Visibility = Visibility.Visible;
                _capsuleShell.Opacity = 1;

                MinWidth = capsuleWidth;
                MinHeight = PaperLayoutDefaults.CapsuleHeight;
                ResizeMode = ResizeMode.NoResize;
            }
            else
            {
                _shell.Visibility = Visibility.Visible;
                _shell.Opacity = 1;
                _capsuleShell.Visibility = Visibility.Collapsed;
                _capsuleShell.Opacity = 0;

                MinWidth = PaperLayoutDefaults.MinWidth;
                MinHeight = PaperLayoutDefaults.MinHeight;
                ResizeMode = ResizeMode.CanResizeWithGrip;
            }

            _shell.Width = double.NaN;
            _shell.Height = double.NaN;

            Width = finalTargetWidth;
            Height = finalTargetHeight;

            _isApplyingCollapsedState = false;
            if (saveGeometry)
            {
                _controller.UpdateGeometry(_paper, this);
            }
            if (arrangeDeepCapsulesAfterCollapse)
            {
                _controller.ArrangeDeepCapsules();
            }
        }

        _paperChrome.ContextMenu = BuildPaperContextMenu();
    }
}
