using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.Win32;
using Application = System.Windows.Application;

namespace PaperTodo;

public sealed partial class AppController : IDisposable
{
    public static AppController Current { get; private set; } = null!;

    private readonly StateStore _store = new();
    private readonly Dictionary<string, PaperWindow> _windows = new();
    private readonly DispatcherTimer _saveTimer;
    private readonly DispatcherTimer _topmostRefreshTimer;

    private TaskbarIcon? _trayIcon;
    private ContextMenu? _trayMenu;
    private Window? _settingsWindow;
    private TextBox? _settingsExternalMarkdownTextBox;
    private bool _isExiting;
    private bool _suppressDirty;
    private bool _hasShownSaveFailure;
    private bool _ignoreSaveFailures;
    private int _trayRefreshSuppressionDepth;
    private long _saveVersion;
    private bool _suppressTopmostForFullscreenForeground;

    private static Brush TrayPaperBrush => Theme.PaperBrush;
    private static Brush TrayBorderBrush => Theme.PaperBorderBrush;
    private static Brush TrayTextBrush => Theme.TextBrush;
    private static Brush TrayWeakTextBrush => Theme.WeakTextBrush;
    private static Brush TrayHoverBrush => Theme.HoverBrush;

    public AppState State { get; private set; }
    public bool SuppressTopmostForFullscreenForeground => _suppressTopmostForFullscreenForeground;

    public AppController()
    {
        Current = this;
        State = _store.Load();

        _saveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(450)
        };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            SaveNow();
        };

        _topmostRefreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(800)
        };
        _topmostRefreshTimer.Tick += (_, _) => RefreshTopmostForForegroundWindow();

        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    public void Start(bool createDefaultPaper = true)
    {
        CreateTrayIcon();
        RefreshTopmostForForegroundWindow();
        _topmostRefreshTimer.Start();

        if (State.Papers.Count == 0)
        {
            if (createDefaultPaper)
            {
                CreatePaper(PaperTypes.Todo, show: true);
                SaveNow();
            }
            return;
        }

        var rescuedPapers = EnsurePapersOnScreen();

        // Closing a paper only hides it for the current session; a fresh app start
        // restores every non-deleted paper so a paper never feels "lost".
        _suppressDirty = true;
        try
        {
            foreach (var paper in State.Papers.ToList())
            {
                ShowPaper(paper);
            }
        }
        finally
        {
            _suppressDirty = false;
        }

        if (rescuedPapers)
        {
            SaveNow();
        }
    }

    public PaperData? CreatePaper(string type, bool show = true, PaperData? sourcePaper = null)
    {
        if (State.Papers.Count >= 100)
        {
            MessageBox.Show(Strings.Get("PaperLimitMessage"), Strings.Get("PaperLimitTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }

        var offset = State.Papers.Count * 24;

        double newX = 140 + offset;
        double newY = 140 + offset;

        if (sourcePaper != null)
        {
            newX = sourcePaper.X + 30;
            newY = sourcePaper.Y + 30;
        }

        while (State.Papers.Any(p => Math.Abs(p.X - newX) < 5 && Math.Abs(p.Y - newY) < 5))
        {
            newX += 30;
            newY += 30;
        }

        var paperType = type == PaperTypes.Note ? PaperTypes.Note : PaperTypes.Todo;
        var paper = new PaperData
        {
            Type = paperType,
            Title = PaperTitles.DefaultTitle(paperType, NextTitleNumber(paperType)),
            X = newX,
            Y = newY,
            Width = type == PaperTypes.Note ? PaperLayoutDefaults.NoteDefaultWidth : PaperLayoutDefaults.TodoDefaultWidth,
            Height = type == PaperTypes.Note ? PaperLayoutDefaults.NoteDefaultHeight : PaperLayoutDefaults.TodoDefaultHeight,
            IsVisible = show,
            AlwaysOnTop = sourcePaper?.AlwaysOnTop ?? false
        };

        RescuePaperIfOffScreen(paper, State.Papers.Count);

        if (paper.Type == PaperTypes.Todo)
        {
            paper.Items.Add(new PaperItem
            {
                Text = "",
                Done = false,
                Order = 0
            });
        }

        State.Papers.Add(paper);

        if (show)
        {
            _trayRefreshSuppressionDepth++;
            try
            {
                ShowPaper(paper);
                if (sourcePaper != null && _windows.TryGetValue(paper.Id, out var window))
                {
                    ForceWindowToFront(window);
                }
            }
            finally
            {
                _trayRefreshSuppressionDepth--;
            }
        }

        RefreshTrayMenu();
        MarkDirty();
        return paper;
    }

    private int NextTitleNumber(string paperType)
    {
        var normalizedType = paperType == PaperTypes.Note ? PaperTypes.Note : PaperTypes.Todo;
        var prefix = normalizedType == PaperTypes.Note ? "笔记" : "待办";
        var usedNumbers = new HashSet<int>();

        foreach (var paper in State.Papers.Where(p => p.Type == normalizedType))
        {
            var title = PaperTitles.CleanCustomTitle(paper.Title);
            if (title.StartsWith(prefix, StringComparison.Ordinal) &&
                int.TryParse(title[prefix.Length..], out var number) &&
                number > 0)
            {
                usedNumbers.Add(number);
            }
        }

        var next = 1;
        while (usedNumbers.Contains(next))
        {
            next++;
        }

        return next;
    }

    public int TitleNumberFor(PaperData paper)
    {
        var normalizedType = paper.Type == PaperTypes.Note ? PaperTypes.Note : PaperTypes.Todo;
        var number = 1;
        foreach (var existing in State.Papers)
        {
            if (existing.Type != normalizedType)
            {
                continue;
            }

            if (existing.Id == paper.Id)
            {
                return number;
            }

            number++;
        }

        return Math.Max(1, number);
    }

    public string PaperTitleText(PaperData paper)
    {
        return PaperTitles.EffectiveTitle(paper, TitleNumberFor(paper));
    }

    public string PaperCapsuleTitle(PaperData paper)
    {
        return PaperTitles.CapsuleText(paper, TitleNumberFor(paper));
    }

    public void UpdatePaperTitle(PaperData paper, string title)
    {
        var cleaned = PaperTitles.CleanCustomTitle(title);
        if (paper.Title == cleaned)
        {
            return;
        }

        paper.Title = cleaned;
        if (_windows.TryGetValue(paper.Id, out var window))
        {
            window.RefreshPaperTitle();
        }
        RefreshTrayMenu();
        MarkDirty();
    }

    public void SetPaperTextZoom(PaperData paper, double zoom)
    {
        if (paper.Type != PaperTypes.Note)
        {
            return;
        }

        var normalized = Math.Round(Math.Clamp(zoom, 0.5, 1.5), 1);
        if (Math.Abs(paper.TextZoom - normalized) < 0.001)
        {
            return;
        }

        paper.TextZoom = normalized;
        if (_windows.TryGetValue(paper.Id, out var window))
        {
            window.UpdateTextZoom();
        }
        MarkDirty();
    }

    public void TogglePaperVisibility(PaperData paper)
    {
        if (IsPaperShown(paper))
        {
            HidePaper(paper);
        }
        else
        {
            ShowPaper(paper);
        }
    }

    public void ExecuteStartupCommand(StartupCommand command)
    {
        switch (command.Kind)
        {
            case StartupCommandKind.Show:
                ShowAllPapers();
                break;
            case StartupCommandKind.Hide:
                HideAllPapers();
                break;
            case StartupCommandKind.Toggle:
                if (State.Papers.Any(IsPaperShown))
                {
                    HideAllPapers();
                }
                else
                {
                    ShowAllPapers();
                }
                break;
            case StartupCommandKind.NewTodo:
                CreatePaper(PaperTypes.Todo, show: true);
                break;
            case StartupCommandKind.NewNote:
                CreatePaper(PaperTypes.Note, show: true);
                break;
            case StartupCommandKind.Exit:
                Exit();
                break;
        }
    }

    public void ShowPaper(PaperData paper)
    {
        RefreshTopmostForForegroundWindow();
        paper.IsVisible = true;
        RescuePaperIfOffScreen(paper, State.Papers.IndexOf(paper));

        if (!_windows.TryGetValue(paper.Id, out var window))
        {
            window = new PaperWindow(paper, this);
            _windows[paper.Id] = window;
        }

        if (!window.IsVisible)
        {
            window.Left = paper.X;
            window.Top = paper.Y;
            if (paper.IsCollapsed && State.UseCapsuleMode)
            {
                window.Width = window.DesiredCapsuleWindowWidth;
                window.Height = PaperLayoutDefaults.CapsuleHeight;
            }
            else
            {
                window.Width = paper.Width;
                window.Height = paper.Height;
            }
            // To prevent a 1-frame DWM cache flash when a window's size changes while hidden,
            // we show it fully transparent first, then restore opacity after layout is complete.
            double originalOpacity = window.Opacity;
            window.Opacity = 0;
            window.Show();

            window.Dispatcher.InvokeAsync(() =>
            {
                window.Opacity = originalOpacity;
            }, System.Windows.Threading.DispatcherPriority.Render);
        }

        if (!_suppressTopmostForFullscreenForeground)
        {
            window.Activate();
        }
        if (State.UseCapsuleMode && State.UseDeepCapsuleMode && paper.IsCollapsed)
        {
            ArrangeDeepCapsules();
        }
        RefreshTrayMenu();
        MarkDirty();
    }

    private static void ForceWindowToFront(Window window)
    {
        if (FullscreenForegroundWindowDetector.IsForegroundFullscreen())
        {
            return;
        }

        var restoreTopmost = window.Topmost;
        window.Topmost = true;
        window.Activate();
        window.Focus();
        window.Dispatcher.BeginInvoke(
            () => window.Topmost = restoreTopmost,
            DispatcherPriority.ApplicationIdle);
    }

    private void RefreshTopmostForForegroundWindow()
    {
        var shouldSuppress = FullscreenForegroundWindowDetector.IsForegroundFullscreen();
        if (shouldSuppress == _suppressTopmostForFullscreenForeground)
        {
            return;
        }

        _suppressTopmostForFullscreenForeground = shouldSuppress;
        foreach (var window in _windows.Values)
        {
            window.RefreshEffectiveTopmost();
        }
    }

    public void HidePaper(PaperData paper)
    {
        paper.IsVisible = false;

        if (_windows.TryGetValue(paper.Id, out var window))
        {
            var saveGeometry = !window.IsDeepCapsulePlaced;
            window.Hide();
            if (paper.IsCollapsed)
            {
                window.SetCollapsedState(false, animate: false, saveGeometry: saveGeometry);
            }
        }
        else
        {
            paper.IsCollapsed = false;
        }

        ArrangeDeepCapsules();
        RefreshTrayMenu();
        MarkDirty();
    }

    public void ShowAllPapers()
    {
        EnsurePapersOnScreen();

        var wasSuppressingDirty = _suppressDirty;
        _suppressDirty = true;
        _trayRefreshSuppressionDepth++;
        try
        {
            foreach (var paper in State.Papers)
            {
                ShowPaper(paper);
            }
        }
        finally
        {
            _trayRefreshSuppressionDepth--;
            _suppressDirty = wasSuppressingDirty;
        }

        RefreshTrayMenu();
        MarkDirty();
    }

    public void HideAllPapers()
    {
        foreach (var paper in State.Papers)
        {
            paper.IsVisible = false;
        }

        foreach (var window in _windows.Values)
        {
            window.Hide();
            window.SetCollapsedState(false, animate: false, saveGeometry: !window.IsDeepCapsulePlaced);
        }

        foreach (var paper in State.Papers)
        {
            paper.IsCollapsed = false;
        }

        RefreshTrayMenu();
        MarkDirty();
    }

    public void DeletePaper(PaperData paper)
    {
        if (_windows.TryGetValue(paper.Id, out var window))
        {
            window.CloseForReal();
            _windows.Remove(paper.Id);
        }

        State.Papers.RemoveAll(p => p.Id == paper.Id);

        if (State.Papers.Count == 0)
        {
            _trayRefreshSuppressionDepth++;
            try
            {
                CreatePaper(PaperTypes.Todo, show: true);
            }
            finally
            {
                _trayRefreshSuppressionDepth--;
            }
        }

        ArrangeDeepCapsules();
        RefreshTrayMenu();
        MarkDirty();
    }

    public void UpdateGeometry(PaperData paper, Window window)
    {
        if (window is PaperWindow paperWindow && paperWindow.SuppressGeometrySave)
        {
            return;
        }

        if (double.IsNaN(window.Left) || double.IsNaN(window.Top))
        {
            return;
        }

        paper.X = Math.Round(window.Left);
        paper.Y = Math.Round(window.Top);
        if (!paper.IsCollapsed)
        {
            paper.Width = Math.Round(Math.Max(window.ActualWidth > 0 ? window.ActualWidth : window.Width, PaperLayoutDefaults.MinWidth));
            paper.Height = Math.Round(Math.Max(window.ActualHeight > 0 ? window.ActualHeight : window.Height, PaperLayoutDefaults.MinHeight));
        }

        MarkDirty();
    }

    public void ArrangeDeepCapsules()
    {
        if (!State.UseCapsuleMode || !State.UseDeepCapsuleMode)
        {
            foreach (var window in _windows.Values)
            {
                window.ClearDeepCapsulePlacement();
            }
            return;
        }

        var capsuleIndex = 0;
        foreach (var paper in State.Papers)
        {
            if (!_windows.TryGetValue(paper.Id, out var window))
            {
                continue;
            }

            if (paper.IsVisible && paper.IsCollapsed && window.IsVisible)
            {
                window.ApplyDeepCapsulePlacement(capsuleIndex);
                capsuleIndex++;
            }
            else
            {
                window.ClearDeepCapsulePlacement();
            }
        }
    }

    public void MarkDirty()
    {
        if (_isExiting || _suppressDirty)
        {
            return;
        }

        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public void SaveNow(bool sync = false)
    {
        try
        {
            _saveTimer.Stop();
            var version = Interlocked.Increment(ref _saveVersion);
            var json = _store.SerializeState(State);
            if (sync)
            {
                _store.SaveJsonSync(json, version);
                _hasShownSaveFailure = false;
            }
            else
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _store.SaveJsonAsync(json, version);
                        Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                        {
                            _hasShownSaveFailure = false;
                        }));
                    }
                    catch (Exception ex)
                    {
                        Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                        {
                            HandleSaveFailure(ex);
                        }));
                    }
                });
            }
        }
        catch (Exception ex)
        {
            HandleSaveFailure(ex);
        }
    }

    private static Style BuildDialogButtonStyle()
    {
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 4, 10, 4)));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Control.ForegroundProperty, Theme.TextBrush));
        style.Setters.Add(new Setter(Control.CursorProperty, Cursors.Hand));
        style.Setters.Add(new Setter(Control.FontSizeProperty, 13.0));

        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "Bd";
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));

        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ContentControl.ContentProperty));
        content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(content);

        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };

        var mouseOver = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        mouseOver.Setters.Add(new Setter(Control.BackgroundProperty, Theme.HoverBrush));

        var pressed = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(UIElement.OpacityProperty, 0.7));

        template.Triggers.Add(mouseOver);
        template.Triggers.Add(pressed);
        style.Setters.Add(new Setter(Control.TemplateProperty, template));

        return style;
    }

    private void HandleSaveFailure(Exception ex)
    {
        if (!_hasShownSaveFailure && !_ignoreSaveFailures)
        {
            _hasShownSaveFailure = true;
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                var dlg = new Window
                {
                    Title = Strings.Get("SaveFailureTitle"),
                    Width = 360,
                    Height = 180,
                    WindowStyle = WindowStyle.None,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    AllowsTransparency = true,
                    Background = Brushes.Transparent,
                    Topmost = true
                };
                var border = new Border
                {
                    Background = Theme.PaperBrush,
                    BorderBrush = Theme.PaperBorderBrush,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(20)
                };
                var grid = new Grid();
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var txt = new TextBlock
                {
                    Text = Strings.Format("SaveFailureMessage", ex.Message),
                    Foreground = Theme.TextBrush,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 14
                };
                grid.Children.Add(txt);
                var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
                Grid.SetRow(btnPanel, 1);
                var btnOk = new Button { Content = Strings.Get("CommonOk"), Width = 80, Margin = new Thickness(0, 0, 10, 0), Style = BuildDialogButtonStyle() };
                btnOk.Click += (s, e) => { _hasShownSaveFailure = false; dlg.Close(); };
                var btnIgnore = new Button { Content = Strings.Get("SaveFailureIgnore"), Width = 110, Style = BuildDialogButtonStyle() };
                btnIgnore.Click += (s, e) => { _ignoreSaveFailures = true; dlg.Close(); };
                btnPanel.Children.Add(btnOk);
                btnPanel.Children.Add(btnIgnore);
                grid.Children.Add(btnPanel);
                border.Child = grid;
                dlg.Content = border;
                dlg.ShowDialog();
            });
        }
    }

    private bool IsPaperShown(PaperData paper)
    {
        return paper.IsVisible && _windows.TryGetValue(paper.Id, out var window) && window.IsVisible;
    }

    private bool EnsurePapersOnScreen()
    {
        var changed = false;
        for (var i = 0; i < State.Papers.Count; i++)
        {
            changed |= RescuePaperIfOffScreen(State.Papers[i], i);
        }

        return changed;
    }

    private static bool RescuePaperIfOffScreen(PaperData paper, int offsetIndex)
    {
        if (IsPaperOnAnyScreen(paper))
        {
            return false;
        }

        var area = SystemParameters.WorkArea;
        var offset = Math.Min(Math.Max(offsetIndex, 0), 8) * 22;

        paper.Width = Math.Clamp(paper.Width, PaperLayoutDefaults.MinWidth, Math.Max(PaperLayoutDefaults.MinWidth, area.Width - 80));
        paper.Height = Math.Clamp(paper.Height, PaperLayoutDefaults.MinHeight, Math.Max(PaperLayoutDefaults.MinHeight, area.Height - 80));
        paper.X = area.Left + 40 + offset;
        paper.Y = area.Top + 40 + offset;
        return true;
    }

    private static bool IsPaperOnAnyScreen(PaperData paper)
    {
        var virtualScreen = new Rect(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);

        var paperRect = new Rect(
            paper.X,
            paper.Y,
            Math.Max(paper.Width, 80),
            Math.Max(paper.Height, 80));

        return virtualScreen.IntersectsWith(paperRect);
    }

    private void CreateTrayIcon()
    {
        _trayMenu = CreateTrayMenu();
        _trayMenu.Opened += (_, _) => RebuildTrayMenu();

        RebuildTrayMenu();

        _trayIcon = new TaskbarIcon
        {
            ToolTipText = "PaperTodo",
            IconSource = LoadTrayIconSource(),
            ContextMenu = _trayMenu,
            Visibility = Visibility.Visible
        };

        _trayIcon.TrayMouseDoubleClick += (_, _) =>
        {
            Application.Current.Dispatcher.Invoke(ShowAllPapers);
        };

    }

    private ImageSource LoadTrayIconSource()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "PaperTodo.ico");
        try
        {
            if (File.Exists(iconPath))
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(iconPath, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
        }
        catch
        {
            // Fall back to the embedded icon if the custom external file is corrupt or locked.
        }

        try
        {
            var resourceName = typeof(AppController).Assembly
                .GetManifestResourceNames()
                .FirstOrDefault(name => name.EndsWith(".PaperTodo.ico", StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(resourceName))
            {
                using var stream = typeof(AppController).Assembly.GetManifestResourceStream(resourceName);
                if (stream != null)
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.StreamSource = stream;
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    return bitmap;
                }
            }
        }
        catch
        {
            // Fallback to vector icon if the embedded resource cannot be loaded.
        }

        return CreateFallbackTrayIcon();
    }

    private static ImageSource CreateFallbackTrayIcon()
    {
        const int size = 32;

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var paper = FrozenBrush(Color.FromRgb(255, 248, 230));
            var border = new Pen(FrozenBrush(Color.FromRgb(126, 96, 58)), 2);
            var check = new Pen(FrozenBrush(Color.FromRgb(80, 96, 60)), 3)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };

            dc.DrawRoundedRectangle(paper, border, new Rect(5, 4, 22, 24), 4, 4);
            dc.DrawLine(check, new Point(10, 18), new Point(15, 23));
            dc.DrawLine(check, new Point(15, 23), new Point(23, 12));
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static readonly ControlTemplate SharedTrayMenuTemplate = BuildTrayMenuTemplate();
    private static readonly ControlTemplate SharedSeparatorTemplate = BuildSeparatorTemplate();
    private static readonly ControlTemplate SharedTrayMenuItemTemplate = BuildTrayMenuItemTemplate();
    private static readonly ControlTemplate SharedSegmentMenuItemTemplate = BuildSegmentMenuItemTemplate();
    private static readonly ControlTemplate SharedTrayContentMenuItemTemplate = BuildTrayContentMenuItemTemplate();
    private static readonly Style SharedTrayMenuItemStyle = BuildTrayMenuItemStyle();
    private static readonly Style SharedTrayContentMenuItemStyle = BuildTrayContentMenuItemStyle();
    private static readonly Style SharedTrayHeaderStyle = BuildTrayHeaderStyle();

    private static ControlTemplate BuildSegmentMenuItemTemplate()
    {
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.ContentSourceProperty, "Header");
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);

        return new ControlTemplate(typeof(MenuItem))
        {
            VisualTree = presenter
        };
    }

    private static ControlTemplate BuildTrayMenuTemplate()
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

    private static ControlTemplate BuildSeparatorTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.HeightProperty, 1.0);
        border.SetValue(Border.BackgroundProperty, new DynamicResourceExtension("TrayBorderBrushKey"));
        border.SetValue(UIElement.OpacityProperty, 0.45);

        return new ControlTemplate(typeof(Separator))
        {
            VisualTree = border
        };
    }

    private static ControlTemplate BuildTrayMenuItemTemplate()
    {
        var root = new FrameworkElementFactory(typeof(Grid));

        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "Bd";
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));

        var contentPanel = new FrameworkElementFactory(typeof(DockPanel));
        contentPanel.SetValue(FrameworkElement.MinWidthProperty, 0.0);
        contentPanel.SetValue(DockPanel.LastChildFillProperty, true);

        var arrow = new FrameworkElementFactory(typeof(TextBlock));
        arrow.Name = "SubmenuArrow";
        arrow.SetValue(TextBlock.TextProperty, "›");
        arrow.SetValue(TextBlock.MarginProperty, new Thickness(10, 0, 0, 0));
        arrow.SetValue(TextBlock.ForegroundProperty, new DynamicResourceExtension("TrayWeakTextBrushKey"));
        arrow.SetValue(TextBlock.FontSizeProperty, 13.0);
        arrow.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        arrow.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
        arrow.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        arrow.SetValue(DockPanel.DockProperty, Dock.Right);
        contentPanel.AppendChild(arrow);

        var content = new FrameworkElementFactory(typeof(TextBlock));
        content.SetBinding(TextBlock.TextProperty, new Binding("Header") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        content.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        content.SetValue(FrameworkElement.MaxWidthProperty, 170.0);
        content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
        content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        contentPanel.AppendChild(content);

        border.AppendChild(contentPanel);
        root.AppendChild(border);

        var popup = new FrameworkElementFactory(typeof(Popup));
        popup.Name = "PART_Popup";
        popup.SetValue(Popup.AllowsTransparencyProperty, true);
        popup.SetValue(Popup.FocusableProperty, false);
        popup.SetValue(Popup.PlacementProperty, PlacementMode.Right);
        popup.SetValue(Popup.PopupAnimationProperty, PopupAnimation.Fade);
        popup.SetBinding(Popup.IsOpenProperty, new Binding("IsSubmenuOpen") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        popup.SetBinding(Popup.PlacementTargetProperty, new Binding { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });

        var popupBorder = new FrameworkElementFactory(typeof(Border));
        popupBorder.SetValue(Border.BackgroundProperty, new DynamicResourceExtension("TrayPaperBrushKey"));
        popupBorder.SetValue(Border.BorderBrushProperty, new DynamicResourceExtension("TrayBorderBrushKey"));
        popupBorder.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        popupBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
        popupBorder.SetValue(Border.PaddingProperty, new Thickness(4));
        popupBorder.SetValue(FrameworkElement.MinWidthProperty, 190.0);

        var popupItems = new FrameworkElementFactory(typeof(ItemsPresenter));
        popupItems.SetValue(KeyboardNavigation.DirectionalNavigationProperty, KeyboardNavigationMode.Cycle);
        popupBorder.AppendChild(popupItems);
        popup.AppendChild(popupBorder);
        root.AppendChild(popup);

        var template = new ControlTemplate(typeof(MenuItem))
        {
            VisualTree = root
        };

        var hover = new Trigger
        {
            Property = MenuItem.IsHighlightedProperty,
            Value = true
        };
        hover.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("TrayHoverBrushKey"), "Bd"));

        var disabled = new Trigger
        {
            Property = UIElement.IsEnabledProperty,
            Value = false
        };
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.72));

        var hasItems = new Trigger
        {
            Property = ItemsControl.HasItemsProperty,
            Value = true
        };
        hasItems.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "SubmenuArrow"));

        template.Triggers.Add(hover);
        template.Triggers.Add(disabled);
        template.Triggers.Add(hasItems);

        return template;
    }

    private static ControlTemplate BuildTrayContentMenuItemTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "Bd";
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));

        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(ContentPresenter.ContentSourceProperty, "Header");
        content.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
        content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(content);

        var template = new ControlTemplate(typeof(MenuItem))
        {
            VisualTree = border
        };

        var hover = new Trigger
        {
            Property = MenuItem.IsHighlightedProperty,
            Value = true
        };
        hover.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("TrayHoverBrushKey"), "Bd"));

        template.Triggers.Add(hover);
        return template;
    }

    private static Style BuildTrayMenuItemStyle()
    {
        var style = new Style(typeof(MenuItem));
        style.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("TrayTextBrushKey")));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 4, 12, 4)));
        style.Setters.Add(new Setter(Control.MinHeightProperty, 24.0));
        style.Setters.Add(new Setter(Control.CursorProperty, System.Windows.Input.Cursors.Hand));
        style.Setters.Add(new Setter(Control.TemplateProperty, SharedTrayMenuItemTemplate));

        return style;
    }

    private static Style BuildTrayContentMenuItemStyle()
    {
        var style = new Style(typeof(MenuItem));
        style.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("TrayTextBrushKey")));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 3, 6, 3)));
        style.Setters.Add(new Setter(Control.MinHeightProperty, 24.0));
        style.Setters.Add(new Setter(Control.CursorProperty, System.Windows.Input.Cursors.Hand));
        style.Setters.Add(new Setter(Control.TemplateProperty, SharedTrayContentMenuItemTemplate));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));

        return style;
    }

    private static Style BuildTrayHeaderStyle()
    {
        var style = new Style(typeof(MenuItem));
        style.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("TrayWeakTextBrushKey")));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 2, 12, 2)));
        style.Setters.Add(new Setter(Control.MinHeightProperty, 22.0));
        style.Setters.Add(new Setter(Control.CursorProperty, System.Windows.Input.Cursors.Arrow));
        style.Setters.Add(new Setter(Control.TemplateProperty, SharedTrayMenuItemTemplate));
        style.Setters.Add(new Setter(Control.FontSizeProperty, 12.0));
        style.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));

        return style;
    }

    private ContextMenu CreateTrayMenu()
    {
        var menu = new ContextMenu
        {
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4, 4, 4, 4),
            HasDropShadow = true,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 13,
            MinWidth = 190,
            Template = SharedTrayMenuTemplate
        };
        UpdateTrayMenuResources(menu);
        return menu;
    }

    private void UpdateTrayMenuResources(ContextMenu menu)
    {
        menu.Resources["TrayPaperBrushKey"] = TrayPaperBrush;
        menu.Resources["TrayBorderBrushKey"] = TrayBorderBrush;
        menu.Resources["TrayTextBrushKey"] = TrayTextBrush;
        menu.Resources["TrayWeakTextBrushKey"] = TrayWeakTextBrush;
        menu.Resources["TrayHoverBrushKey"] = TrayHoverBrush;
    }

    private void RebuildTrayMenu()
    {
        if (_trayMenu == null)
        {
            return;
        }

        UpdateTrayMenuResources(_trayMenu);

        _trayMenu.Background = TrayPaperBrush;
        _trayMenu.BorderBrush = TrayBorderBrush;
        _trayMenu.Foreground = TrayTextBrush;

        _trayMenu.Items.Clear();

        _trayMenu.Items.Add(TrayHeader(AppDisplayName));
        _trayMenu.Items.Add(TrayItem(Strings.Get("TrayNewTodo"), () => CreatePaper(PaperTypes.Todo, show: true)));
        _trayMenu.Items.Add(TrayItem(Strings.Get("TrayNewNote"), () => CreatePaper(PaperTypes.Note, show: true)));
        _trayMenu.Items.Add(TraySeparator());

        _trayMenu.Items.Add(TrayItem(Strings.Get("TraySettings"), ShowSettingsWindow));
        _trayMenu.Items.Add(TraySeparator());

        _trayMenu.Items.Add(TrayItem(Strings.Get("TrayShowAll"), ShowAllPapers));
        _trayMenu.Items.Add(TrayItem(Strings.Get("TrayHideAll"), HideAllPapers));

        if (State.Papers.Count > 0)
        {
            _trayMenu.Items.Add(TraySeparator());
            _trayMenu.Items.Add(TrayHeader(Strings.Get("TrayPapers")));

            for (var index = 0; index < State.Papers.Count; index++)
            {
                var paper = State.Papers[index];
                _trayMenu.Items.Add(TrayPaperItem(paper));
            }
        }

        _trayMenu.Items.Add(TraySeparator());
        _trayMenu.Items.Add(TrayItem(Strings.Get("TrayExit"), Exit));
    }

    private void SetTheme(string theme)
    {
        State.Theme = theme;
        SaveNow();

        foreach (var window in _windows.Values)
        {
            window.UpdateTheme();
        }

        RebuildTrayMenu();
        RefreshSettingsWindowContent();
    }

    private UIElement CreateThemeSegmentSelector()
    {
        var segments = new[]
        {
            ("system", Strings.Get("ThemeSystem")),
            ("light", Strings.Get("ThemeLight")),
            ("dark", Strings.Get("ThemeDark"))
        };

        return CreateSegmentSelector(segments, State.Theme, SetTheme);
    }

    private void SetMarkdownRenderMode(string mode)
    {
        if (!MarkdownRenderModes.IsValid(mode))
        {
            return;
        }

        State.MarkdownRenderMode = mode;
        SaveNow();

        foreach (var window in _windows.Values)
        {
            window.UpdateMarkdownRenderMode();
        }

        RebuildTrayMenu();
        RefreshSettingsWindowContent();
    }

    private UIElement CreateMarkdownRenderSegmentSelector()
    {
        var segments = new[]
        {
            (MarkdownRenderModes.Off, Strings.Get("MarkdownRenderOff")),
            (MarkdownRenderModes.Basic, Strings.Get("MarkdownRenderBasic")),
            (MarkdownRenderModes.Enhanced, Strings.Get("MarkdownRenderEnhanced"))
        };

        return CreateSegmentSelector(segments, State.MarkdownRenderMode, SetMarkdownRenderMode);
    }

    private UIElement CreateExternalMarkdownExtensionEditor()
    {
        var textBox = new TextBox
        {
            Text = ExternalMarkdownFileExtensions.Normalize(State.ExternalMarkdownExtension),
            Foreground = TrayTextBrush,
            CaretBrush = TrayTextBrush,
            Background = Brushes.Transparent,
            BorderBrush = TrayBorderBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 4, 0, 8),
            FontSize = 13,
            Height = 28,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Center,
            Style = BuildSettingsTextBoxStyle()
        };

        _settingsExternalMarkdownTextBox = textBox;
        textBox.GotKeyboardFocus += (_, _) => textBox.SelectAll();
        textBox.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                CommitExternalMarkdownExtension(textBox);
                Keyboard.ClearFocus();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                textBox.Text = ExternalMarkdownFileExtensions.Normalize(State.ExternalMarkdownExtension);
                Keyboard.ClearFocus();
                e.Handled = true;
            }
        };
        textBox.LostKeyboardFocus += (_, _) => CommitExternalMarkdownExtension(textBox);

        return textBox;
    }

    private void CommitSettingsExternalMarkdownEditor()
    {
        if (_settingsExternalMarkdownTextBox != null)
        {
            CommitExternalMarkdownExtension(_settingsExternalMarkdownTextBox);
        }
    }

    private void CommitExternalMarkdownExtension(TextBox textBox)
    {
        var normalized = ExternalMarkdownFileExtensions.Normalize(textBox.Text);
        if (textBox.Text != normalized)
        {
            textBox.Text = normalized;
            textBox.CaretIndex = textBox.Text.Length;
        }

        SetExternalMarkdownExtension(normalized);
    }

    private void SetExternalMarkdownExtension(string extension)
    {
        var normalized = ExternalMarkdownFileExtensions.Normalize(extension);
        if (State.ExternalMarkdownExtension == normalized)
        {
            return;
        }

        State.ExternalMarkdownExtension = normalized;
        SaveNow();

        foreach (var window in _windows.Values)
        {
            window.UpdateExternalMarkdownExtension();
        }
    }

    private UIElement CreateSegmentSelector((string Key, string Label)[] segments, string activeKey, Action<string> onSelect)
    {
        var container = new Border
        {
            BorderBrush = TrayBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Background = Brushes.Transparent,
            Margin = new Thickness(0, 4, 0, 10),
            Height = 26,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var grid = new Grid();
        for (var i = 0; i < segments.Length; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        for (int i = 0; i < segments.Length; i++)
        {
            var key = segments[i].Key;
            var label = segments[i].Label;
            var isActive = activeKey == key;

            var segmentBorder = new Border
            {
                CornerRadius = new CornerRadius(5),
                Margin = new Thickness(1),
                Background = isActive ? Theme.ActiveBrush : Brushes.Transparent,
                Cursor = System.Windows.Input.Cursors.Hand
            };

            var textBlock = new TextBlock
            {
                Text = label,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 12,
                FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = isActive ? TrayPaperBrush : TrayTextBrush,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            segmentBorder.Child = textBlock;

            if (!isActive)
            {
                segmentBorder.MouseEnter += (_, _) =>
                {
                    segmentBorder.Background = TrayHoverBrush;
                };
                segmentBorder.MouseLeave += (_, _) =>
                {
                    segmentBorder.Background = Brushes.Transparent;
                };
            }

            segmentBorder.MouseLeftButtonDown += (_, _) =>
            {
                if (activeKey == key)
                {
                    return;
                }

                onSelect(key);
            };

            Grid.SetColumn(segmentBorder, i);
            grid.Children.Add(segmentBorder);
        }

        container.Child = grid;
        return container;
    }

    private void ShowSettingsWindow()
    {
        if (_trayMenu != null)
        {
            _trayMenu.IsOpen = false;
        }

        if (_settingsWindow != null)
        {
            RefreshSettingsWindowContent();
            _settingsWindow.Show();
            _settingsWindow.Activate();
            return;
        }

        var window = new Window
        {
            Title = Strings.Get("TraySettings"),
            Width = 320,
            SizeToContent = SizeToContent.Height,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            Topmost = true,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            FontFamily = new FontFamily("Segoe UI"),
            SnapsToDevicePixels = true,
            UseLayoutRounding = true
        };

        window.PreviewMouseDown += (_, e) =>
        {
            if (_settingsExternalMarkdownTextBox is not { IsKeyboardFocusWithin: true } textBox ||
                IsWithinElement(e.OriginalSource as DependencyObject, textBox))
            {
                return;
            }

            CommitExternalMarkdownExtension(textBox);
            Keyboard.ClearFocus();
        };
        window.Deactivated += (_, _) => CommitSettingsExternalMarkdownEditor();
        window.Closed += (_, _) =>
        {
            CommitSettingsExternalMarkdownEditor();
            _settingsExternalMarkdownTextBox = null;
            _settingsWindow = null;
        };
        _settingsWindow = window;
        RefreshSettingsWindowContent();
        window.Show();
        CenterSettingsWindow(window);
        window.Activate();
        window.Dispatcher.BeginInvoke(() => CenterSettingsWindow(window), DispatcherPriority.Loaded);
    }

    private void RefreshSettingsWindowContent()
    {
        if (_settingsWindow == null)
        {
            return;
        }

        _settingsWindow.Content = BuildSettingsWindowContent(_settingsWindow);
    }

    private UIElement BuildSettingsWindowContent(Window window)
    {
        var panel = new StackPanel
        {
            Width = 288
        };

        var titleRow = new Grid
        {
            Margin = new Thickness(0, 0, 0, 10),
            Background = Brushes.Transparent,
            Cursor = System.Windows.Input.Cursors.SizeAll
        };
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        titleRow.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                try { window.DragMove(); } catch { }
            }
        };

        var title = new TextBlock
        {
            Text = Strings.Get("TraySettings"),
            Foreground = TrayTextBrush,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(title, 0);
        titleRow.Children.Add(title);

        var closeButton = new Button
        {
            Content = "×",
            Width = 28,
            Height = 24,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = TrayWeakTextBrush,
            FontSize = 16,
            Cursor = System.Windows.Input.Cursors.Hand,
            Focusable = false
        };
        closeButton.Click += (_, _) => window.Close();
        Grid.SetColumn(closeButton, 1);
        titleRow.Children.Add(closeButton);

        panel.Children.Add(titleRow);

        panel.Children.Add(SettingsSectionLabel(Strings.Get("SettingsDisplay")));
        panel.Children.Add(SettingsFieldLabel(Strings.Get("TrayThemeMode")));
        panel.Children.Add(CreateThemeSegmentSelector());
        panel.Children.Add(SettingsFieldLabel(Strings.Get("TrayMarkdownRenderMode")));
        panel.Children.Add(CreateMarkdownRenderSegmentSelector());

        panel.Children.Add(SettingsSectionLabel(Strings.Get("SettingsExternalOpen")));
        panel.Children.Add(SettingsFieldLabel(Strings.Get("SettingsExternalMarkdownExtension")));
        panel.Children.Add(CreateExternalMarkdownExtensionEditor());
        panel.Children.Add(SettingsToggle(Strings.Get("SettingsShowTopBarExternalOpenButton"), State.ShowTopBarExternalOpenButton, ToggleTopBarExternalOpenButton));

        panel.Children.Add(SettingsSectionLabel(Strings.Get("SettingsTopBarButtons")));
        panel.Children.Add(SettingsToggle(Strings.Get("SettingsShowTopBarNewTodoButton"), State.ShowTopBarNewTodoButton, ToggleTopBarNewTodoButton));
        panel.Children.Add(SettingsToggle(Strings.Get("SettingsShowTopBarNewNoteButton"), State.ShowTopBarNewNoteButton, ToggleTopBarNewNoteButton));

        panel.Children.Add(SettingsSectionLabel(Strings.Get("SettingsBehavior")));
        panel.Children.Add(SettingsToggle(Strings.Get("TrayStartup"), SystemSettingsHelper.IsStartupEnabled(), ToggleStartup));
        panel.Children.Add(SettingsToggle(Strings.Get("TrayCapsuleMode"), State.UseCapsuleMode, ToggleCapsuleMode));
        panel.Children.Add(SettingsToggle(Strings.Get("TrayDeepCapsuleMode"), State.UseDeepCapsuleMode, ToggleDeepCapsuleMode));

        return new Border
        {
            Background = TrayPaperBrush,
            BorderBrush = TrayBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14, 12, 14, 14),
            Child = panel
        };
    }

    private static TextBlock SettingsSectionLabel(string text)
    {
        return new TextBlock
        {
            Text = text,
            Foreground = TrayWeakTextBrush,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 12, 0, 3)
        };
    }

    private static TextBlock SettingsFieldLabel(string text)
    {
        return new TextBlock
        {
            Text = text,
            Foreground = TrayWeakTextBrush,
            FontSize = 11,
            FontWeight = FontWeights.Medium,
            Margin = new Thickness(0, 0, 0, 0)
        };
    }

    private CheckBox SettingsToggle(string text, bool isChecked, Action onToggle)
    {
        var checkBox = new CheckBox
        {
            Content = text,
            IsChecked = isChecked,
            Foreground = TrayTextBrush,
            FontSize = 13,
            Margin = new Thickness(0, 4, 0, 0),
            Cursor = System.Windows.Input.Cursors.Hand,
            Focusable = false,
            Style = BuildSettingsCheckBoxStyle()
        };

        checkBox.Click += (_, _) => onToggle();
        return checkBox;
    }

    private Style BuildSettingsTextBoxStyle()
    {
        var style = new Style(typeof(TextBox));
        style.Setters.Add(new Setter(Control.ForegroundProperty, TrayTextBrush));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, TrayBorderBrush));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 4, 8, 4)));
        style.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
        style.Setters.Add(new Setter(UIElement.SnapsToDevicePixelsProperty, true));
        style.Setters.Add(new Setter(FrameworkElement.UseLayoutRoundingProperty, true));

        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "Bd";
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        border.SetValue(UIElement.SnapsToDevicePixelsProperty, true);

        var contentHost = new FrameworkElementFactory(typeof(ScrollViewer), "PART_ContentHost");
        contentHost.SetValue(FrameworkElement.VerticalAlignmentProperty, new TemplateBindingExtension(Control.VerticalContentAlignmentProperty));
        contentHost.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        border.AppendChild(contentHost);

        var template = new ControlTemplate(typeof(TextBox))
        {
            VisualTree = border
        };

        var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hoverTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, TrayWeakTextBrush, "Bd"));

        var focusTrigger = new Trigger { Property = UIElement.IsKeyboardFocusWithinProperty, Value = true };
        focusTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, Theme.ActiveBrush, "Bd"));

        var disabledTrigger = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabledTrigger.Setters.Add(new Setter(UIElement.OpacityProperty, 0.55));

        template.Triggers.Add(hoverTrigger);
        template.Triggers.Add(focusTrigger);
        template.Triggers.Add(disabledTrigger);

        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        return style;
    }

    private static bool IsWithinElement(DependencyObject? current, DependencyObject ancestor)
    {
        while (current != null)
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }

            current = GetElementParent(current);
        }

        return false;
    }

    private static DependencyObject? GetElementParent(DependencyObject current)
    {
        if (current is FrameworkElement fe && fe.Parent is DependencyObject parent)
        {
            return parent;
        }

        if (current is FrameworkContentElement fce && fce.Parent is DependencyObject contentParent)
        {
            return contentParent;
        }

        try
        {
            return VisualTreeHelper.GetParent(current);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private Style BuildSettingsCheckBoxStyle()
    {
        var style = new Style(typeof(CheckBox));
        style.Setters.Add(new Setter(Control.ForegroundProperty, TrayTextBrush));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Control.CursorProperty, System.Windows.Input.Cursors.Hand));
        style.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
        style.Setters.Add(new Setter(UIElement.FocusableProperty, false));
        style.Setters.Add(new Setter(UIElement.SnapsToDevicePixelsProperty, true));
        style.Setters.Add(new Setter(FrameworkElement.UseLayoutRoundingProperty, true));

        var root = new FrameworkElementFactory(typeof(StackPanel));
        root.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        root.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);

        var markHost = new FrameworkElementFactory(typeof(Grid));
        markHost.SetValue(FrameworkElement.WidthProperty, 16.0);
        markHost.SetValue(FrameworkElement.HeightProperty, 16.0);
        markHost.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 8, 0));
        markHost.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);

        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "CheckBorder";
        border.SetValue(FrameworkElement.WidthProperty, 16.0);
        border.SetValue(FrameworkElement.HeightProperty, 16.0);
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1.5));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        border.SetValue(Border.BorderBrushProperty, TrayBorderBrush);
        border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        markHost.AppendChild(border);

        var path = new FrameworkElementFactory(typeof(System.Windows.Shapes.Path));
        path.Name = "CheckMark";
        path.SetValue(System.Windows.Shapes.Path.DataProperty, Geometry.Parse("M 4,8.1 L 7,11 L 12,5"));
        path.SetValue(System.Windows.Shapes.Path.StrokeProperty, TrayPaperBrush);
        path.SetValue(System.Windows.Shapes.Path.StrokeThicknessProperty, 2.0);
        path.SetValue(System.Windows.Shapes.Path.StrokeStartLineCapProperty, PenLineCap.Round);
        path.SetValue(System.Windows.Shapes.Path.StrokeEndLineCapProperty, PenLineCap.Round);
        path.SetValue(System.Windows.Shapes.Path.StrokeLineJoinProperty, PenLineJoin.Round);
        path.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
        path.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        path.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        markHost.AppendChild(path);

        root.AppendChild(markHost);

        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ContentControl.ContentProperty));
        content.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
        content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        content.SetValue(System.Windows.Documents.TextElement.ForegroundProperty, new TemplateBindingExtension(Control.ForegroundProperty));
        root.AppendChild(content);

        var template = new ControlTemplate(typeof(CheckBox))
        {
            VisualTree = root
        };

        var checkedTrigger = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
        checkedTrigger.Setters.Add(new Setter(Border.BackgroundProperty, Theme.ActiveBrush, "CheckBorder"));
        checkedTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, Brushes.Transparent, "CheckBorder"));
        checkedTrigger.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "CheckMark"));

        var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty, TrayHoverBrush, "CheckBorder"));

        var disabledTrigger = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabledTrigger.Setters.Add(new Setter(UIElement.OpacityProperty, 0.55));

        template.Triggers.Add(hoverTrigger);
        template.Triggers.Add(checkedTrigger);
        template.Triggers.Add(disabledTrigger);

        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        return style;
    }

    private static void CenterSettingsWindow(Window? window)
    {
        if (window == null)
        {
            return;
        }

        var area = SystemParameters.WorkArea;
        var width = window.ActualWidth > 1 ? window.ActualWidth : window.Width;
        var height = window.ActualHeight > 1 ? window.ActualHeight : 280;
        window.Left = area.Left + Math.Max(16, (area.Width - width) / 2);
        window.Top = area.Top + Math.Max(16, (area.Height - height) / 2);
    }

    private void RefreshTrayMenu()
    {
        if (_trayRefreshSuppressionDepth > 0)
        {
            return;
        }

        if (_trayMenu != null && _trayMenu.IsOpen)
        {
            RebuildTrayMenu();
        }
    }

    private static MenuItem TrayItem(string text, Action action)
    {
        var item = new MenuItem
        {
            Header = text,
            Style = SharedTrayMenuItemStyle
        };

        item.Click += (_, _) => Application.Current.Dispatcher.Invoke(action);
        return item;
    }

    private MenuItem TrayPaperItem(PaperData paper)
    {
        var item = new MenuItem
        {
            Style = SharedTrayContentMenuItemStyle,
            StaysOpenOnClick = true
        };

        var grid = new Grid
        {
            MinWidth = 168
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var prefix = IsPaperShown(paper) ? "☑ " : "☐ ";
        var normalLabelText = prefix + PaperTypeIcon(paper) + " " + PaperLabel(paper);
        var label = new TextBlock
        {
            Text = normalLabelText,
            Foreground = TrayTextBrush,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 142,
            VerticalAlignment = VerticalAlignment.Center
        };

        var deleteText = new TextBlock
        {
            Text = "×",
            Foreground = TrayWeakTextBrush,
            FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var deleteArea = new Border
        {
            Width = 24,
            MinHeight = 20,
            CornerRadius = new CornerRadius(6),
            Background = Brushes.Transparent,
            Cursor = System.Windows.Input.Cursors.Hand,
            Child = deleteText
        };

        var confirmText = new TextBlock
        {
            Text = Strings.Get("TrayInlineConfirmAction"),
            Foreground = System.Windows.Media.Brushes.Red,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var confirmArea = new Border
        {
            Width = 42,
            MinHeight = 20,
            CornerRadius = new CornerRadius(6),
            Background = Brushes.Transparent,
            Cursor = System.Windows.Input.Cursors.Hand,
            Visibility = Visibility.Collapsed,
            Child = confirmText
        };

        bool confirmMode = false;

        static void ResetActionArea(Border area, TextBlock text, Brush foreground)
        {
            area.Background = Brushes.Transparent;
            area.Opacity = 1.0;
            text.Foreground = foreground;
        }

        void ResetDeleteVisual()
        {
            ResetActionArea(deleteArea, deleteText, confirmMode ? TrayTextBrush : TrayWeakTextBrush);
            ResetActionArea(confirmArea, confirmText, System.Windows.Media.Brushes.Red);
        }

        void ExitConfirmMode()
        {
            confirmMode = false;
            label.Text = normalLabelText;
            label.Foreground = TrayTextBrush;
            label.FontWeight = FontWeights.Normal;
            deleteText.Text = "×";
            deleteArea.Width = 24;
            deleteText.FontSize = 14;
            confirmArea.Visibility = Visibility.Collapsed;
            ResetDeleteVisual();
        }

        void EnterConfirmMode()
        {
            confirmMode = true;
            label.Text = Strings.Get("TrayInlineConfirmDelete");
            label.Foreground = System.Windows.Media.Brushes.Red;
            label.FontWeight = FontWeights.SemiBold;
            deleteText.Text = Strings.Get("CommonCancel");
            deleteArea.Width = 42;
            deleteText.FontSize = 12;
            deleteText.Foreground = TrayTextBrush;
            confirmArea.Visibility = Visibility.Visible;
        }

        static void AttachActionVisual(Border area, TextBlock text, Func<Brush> normalForeground, Brush hoverForeground)
        {
            area.MouseEnter += (_, _) =>
            {
                area.Background = TrayHoverBrush;
                text.Foreground = hoverForeground;
            };
            area.MouseLeave += (_, _) => ResetActionArea(area, text, normalForeground());
            area.MouseLeftButtonDown += (_, e) =>
            {
                area.Opacity = 0.72;
                e.Handled = true;
            };
        }

        AttachActionVisual(deleteArea, deleteText, () => confirmMode ? TrayTextBrush : TrayWeakTextBrush, TrayTextBrush);
        AttachActionVisual(confirmArea, confirmText, () => System.Windows.Media.Brushes.Red, System.Windows.Media.Brushes.Red);

        deleteArea.MouseLeftButtonUp += (_, e) =>
        {
            deleteArea.Opacity = 1.0;
            if (!confirmMode)
            {
                EnterConfirmMode();
            }
            else
            {
                ExitConfirmMode();
            }
            e.Handled = true;
        };
        confirmArea.MouseLeftButtonUp += (_, e) =>
        {
            confirmArea.Opacity = 1.0;
            if (confirmMode)
            {
                DeletePaper(paper);
            }
            e.Handled = true;
        };

        Grid.SetColumn(label, 0);
        Grid.SetColumn(confirmArea, 1);
        Grid.SetColumn(deleteArea, 2);
        grid.Children.Add(label);
        grid.Children.Add(confirmArea);
        grid.Children.Add(deleteArea);

        item.Header = grid;
        item.Click += (_, _) =>
        {
            if (confirmMode)
            {
                return;
            }

            Application.Current.Dispatcher.Invoke(() => TogglePaperVisibility(paper));
            if (_trayMenu != null)
            {
                _trayMenu.IsOpen = false;
            }
        };

        return item;
    }

    private static MenuItem TrayHeader(string text)
    {
        return new MenuItem
        {
            Header = text,
            IsEnabled = false,
            Style = SharedTrayHeaderStyle
        };
    }

    private static string AppDisplayName
    {
        get
        {
            var version = typeof(AppController).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion
                .Split('+')[0];
            return string.IsNullOrWhiteSpace(version) ? "PaperTodo" : $"PaperTodo v{version}";
        }
    }

    private static Separator TraySeparator()
    {
        return new Separator
        {
            Margin = new Thickness(8, 3, 8, 3),
            Template = SharedSeparatorTemplate
        };
    }

    private string PaperLabel(PaperData paper)
    {
        return PaperCapsuleTitle(paper);
    }

    private static string PaperTypeIcon(PaperData paper)
    {
        return paper.Type == PaperTypes.Note ? "✎" : "✓";
    }

    private static SolidColorBrush FrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public void Exit()
    {
        _isExiting = true;
        _saveTimer.Stop();
        if (_trayMenu != null)
        {
            _trayMenu.IsOpen = false;
        }
        _settingsWindow?.Close();
        _settingsWindow = null;
        SaveNow(sync: true);

        _trayIcon?.Dispose();
        _trayIcon = null;
        _trayMenu = null;

        foreach (var window in _windows.Values.ToList())
        {
            window.CloseForReal();
        }

        Application.Current.Shutdown();
        Environment.Exit(0);
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.General)
        {
            if (State.Theme == "system")
            {
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    foreach (var window in _windows.Values)
                    {
                        window.UpdateTheme();
                    }
                    RebuildTrayMenu();
                    RefreshSettingsWindowContent();
                }));
            }
        }
    }

    private void ToggleStartup()
    {
        var enabled = SystemSettingsHelper.IsStartupEnabled();
        if (!SystemSettingsHelper.ToggleStartup(!enabled))
        {
            _trayIcon?.ShowBalloonTip(
                Strings.Get("StartupFailureTitle"),
                Strings.Get("StartupFailureMessage"),
                BalloonIcon.Warning);
        }
        RebuildTrayMenu();
        RefreshSettingsWindowContent();
    }

    private void ToggleCapsuleMode()
    {
        State.UseCapsuleMode = !State.UseCapsuleMode;

        foreach (var window in _windows.Values)
        {
            window.UpdateCapsuleMode();
        }

        if (!State.UseCapsuleMode)
        {
            State.UseDeepCapsuleMode = false;
            foreach (var paper in State.Papers)
            {
                paper.IsCollapsed = false;
            }
        }

        ArrangeDeepCapsules();
        SaveNow();
        RebuildTrayMenu();
        RefreshSettingsWindowContent();
    }

    private void ToggleTopBarNewTodoButton()
    {
        State.ShowTopBarNewTodoButton = !State.ShowTopBarNewTodoButton;
        RefreshTopBarNewPaperButtonsSetting();
    }

    private void ToggleTopBarNewNoteButton()
    {
        State.ShowTopBarNewNoteButton = !State.ShowTopBarNewNoteButton;
        RefreshTopBarNewPaperButtonsSetting();
    }

    private void ToggleTopBarExternalOpenButton()
    {
        State.ShowTopBarExternalOpenButton = !State.ShowTopBarExternalOpenButton;
        RefreshTopBarNewPaperButtonsSetting();
    }

    private void RefreshTopBarNewPaperButtonsSetting()
    {
        foreach (var window in _windows.Values)
        {
            window.UpdateTopBarNewPaperButtons();
        }

        SaveNow();
        RefreshSettingsWindowContent();
    }

    private void ToggleDeepCapsuleMode()
    {
        State.UseDeepCapsuleMode = !State.UseDeepCapsuleMode;

        if (State.UseDeepCapsuleMode && !State.UseCapsuleMode)
        {
            State.UseCapsuleMode = true;
            foreach (var window in _windows.Values)
            {
                window.UpdateCapsuleMode();
            }
        }

        foreach (var window in _windows.Values)
        {
            window.UpdateDeepCapsuleMode();
        }

        ArrangeDeepCapsules();
        SaveNow();
        RebuildTrayMenu();
        RefreshSettingsWindowContent();
    }

    public void Dispose()
    {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _saveTimer.Stop();
        _topmostRefreshTimer.Stop();
        if (_trayMenu != null)
        {
            _trayMenu.IsOpen = false;
        }
        _settingsWindow?.Close();
        _settingsWindow = null;
        _trayIcon?.Dispose();
        _trayIcon = null;
        _trayMenu = null;
    }
}
