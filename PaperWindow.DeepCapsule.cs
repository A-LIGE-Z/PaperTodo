using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
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

public sealed partial class PaperWindow
{
    private Window EnsureDeepCapsuleSlotHost()
    {
        if (_deepCapsuleSlotHost != null)
        {
            return _deepCapsuleSlotHost;
        }

        _deepCapsuleSlotHostRoot = new Grid
        {
            Background = null,
            ClipToBounds = true,
            Opacity = 1
        };

        _deepCapsuleSlotChrome = new Border
        {
            Margin = new Thickness(WindowChromeMargin),
            CornerRadius = new CornerRadius(CapsuleChromeCornerRadius),
            BorderThickness = new Thickness(1),
            SnapsToDevicePixels = true
        };
        Panel.SetZIndex(_deepCapsuleSlotChrome, 0);
        _deepCapsuleSlotHostRoot.Children.Add(_deepCapsuleSlotChrome);

        _deepCapsuleSlotShell = BuildDeepCapsuleSlotShell();
        Panel.SetZIndex(_deepCapsuleSlotShell, 10);
        _deepCapsuleSlotHostRoot.Children.Add(_deepCapsuleSlotShell);

        _deepCapsuleSlotOutline = new Border
        {
            Margin = new Thickness(WindowChromeMargin - DeepCapsuleSlotOutlineThickness + DeepCapsuleSlotOutlineOverlap),
            CornerRadius = new CornerRadius(CapsuleChromeCornerRadius + DeepCapsuleSlotOutlineThickness - DeepCapsuleSlotOutlineOverlap),
            BorderThickness = new Thickness(DeepCapsuleSlotOutlineThickness),
            Background = Brushes.Transparent,
            IsHitTestVisible = false,
            SnapsToDevicePixels = true
        };
        Panel.SetZIndex(_deepCapsuleSlotOutline, 20);
        _deepCapsuleSlotHostRoot.Children.Add(_deepCapsuleSlotOutline);

        var host = new Window
        {
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ResizeMode = ResizeMode.NoResize,
            FontFamily = new FontFamily("Segoe UI"),
            SnapsToDevicePixels = true,
            UseLayoutRounding = true,
            Topmost = !_controller.SuppressTopmostForFullscreenForeground,
            Width = CapsuleWindowWidth(usesDeepCapsulePresentation: true),
            Height = PaperLayoutDefaults.CapsuleHeight,
            Content = _deepCapsuleSlotHostRoot
        };
        host.SourceInitialized += (_, _) => ApplyNoActivateStyle(host);
        host.Deactivated += (_, _) => CloseDeepCapsuleSlotContextMenu();
        _deepCapsuleSlotHost = host;
        UpdateDeepCapsuleSlotHostTheme();
        return host;
    }

    private Grid BuildDeepCapsuleSlotShell()
    {
        var shell = new Grid
        {
            Width = DeepCapsuleSlotShellLayoutWidth(),
            Height = 30,
            Margin = new Thickness(WindowChromeMargin),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Background = Brushes.Transparent
        };
        shell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        shell.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var leftArea = new Border
        {
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(CapsuleInnerCornerRadius, 0, 0, CapsuleInnerCornerRadius),
            Cursor = Cursors.Hand,
            ClipToBounds = true
        };

        var leftStack = new Grid
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(CapsuleLeftPadding, 0, 0, 0)
        };
        leftStack.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        leftStack.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _deepCapsuleSlotIconText = new TextBlock
        {
            Text = _paper.Type == PaperTypes.Note ? "✎" : "✓",
            Foreground = BrightWeakTextBrush,
            FontFamily = NoteTypography.FontFamily,
            FontSize = CapsuleIconFontSize,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(_deepCapsuleSlotIconText, 0);
        leftStack.Children.Add(_deepCapsuleSlotIconText);

        _deepCapsuleSlotLabelText = new TextBlock
        {
            Foreground = WeakTextBrush,
            FontFamily = NoteTypography.FontFamily,
            FontSize = CapsuleLabelFontSize,
            Margin = new Thickness(CapsuleIconGap, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(_deepCapsuleSlotLabelText, 1);
        leftStack.Children.Add(_deepCapsuleSlotLabelText);
        leftArea.Child = leftStack;

        leftArea.MouseEnter += (_, _) => leftArea.Background = HoverBrush;
        leftArea.MouseLeave += (_, _) => leftArea.Background = Brushes.Transparent;
        shell.MouseEnter += (_, _) => SetDeepCapsuleHover(true);
        shell.MouseLeave += (_, _) => SetDeepCapsuleHover(false);
        leftArea.PreviewMouseLeftButtonDown += (_, e) =>
        {
            _deepCapsuleSlotMouseDownScreenPos = DeepCapsuleSlotPointerScreenPosition(e);
            SetDeepCapsuleGestureState(DeepCapsuleGestureState.PendingClick);
            leftArea.CaptureMouse();
            e.Handled = true;
        };
        leftArea.PreviewMouseMove += (_, e) =>
        {
            if (IsDeepCapsuleReordering)
            {
                UpdateDeepCapsuleReorderDrag(DeepCapsuleSlotPointerScreenPosition(e));
                e.Handled = true;
                return;
            }

            if (!IsDeepCapsuleSlotPendingClick)
            {
                return;
            }

            var currentScreenPos = DeepCapsuleSlotPointerScreenPosition(e);
            var deltaX = Math.Abs(currentScreenPos.X - _deepCapsuleSlotMouseDownScreenPos.X);
            var deltaY = Math.Abs(currentScreenPos.Y - _deepCapsuleSlotMouseDownScreenPos.Y);
            if (CanReorderDeepCapsuleSlot())
            {
                if (deltaY >= SystemParameters.MinimumVerticalDragDistance + DeepCapsuleReorderDragExtraThreshold)
                {
                    SetDeepCapsuleGestureState(DeepCapsuleGestureState.Idle);
                    StartDeepCapsuleReorderDrag(currentScreenPos);
                    e.Handled = true;
                }

                return;
            }

            if (deltaX >= SystemParameters.MinimumHorizontalDragDistance ||
                deltaY >= SystemParameters.MinimumVerticalDragDistance)
            {
                SetDeepCapsuleGestureState(DeepCapsuleGestureState.Idle);
                leftArea.ReleaseMouseCapture();
            }
        };
        leftArea.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (IsDeepCapsuleReordering)
            {
                EndDeepCapsuleReorderDrag(commit: true);
                leftArea.ReleaseMouseCapture();
                e.Handled = true;
                return;
            }

            if (IsDeepCapsuleSlotPendingClick)
            {
                SetDeepCapsuleGestureState(DeepCapsuleGestureState.Idle);
                leftArea.ReleaseMouseCapture();
                ActivateFromDeepCapsuleSlot();
                e.Handled = true;
            }
        };
        leftArea.LostMouseCapture += (_, _) =>
        {
            if (IsDeepCapsuleSlotPendingClick)
            {
                SetDeepCapsuleGestureState(DeepCapsuleGestureState.Idle);
            }
            if (IsDeepCapsuleReordering && Mouse.LeftButton != MouseButtonState.Pressed)
            {
                EndDeepCapsuleReorderDrag(commit: false);
            }
        };
        leftArea.ContextMenu = BuildDeepCapsuleSlotContextMenu();

        Grid.SetColumn(leftArea, 0);
        shell.Children.Add(leftArea);

        var closeGlyphOffset = new TranslateTransform(CapsuleCloseGlyphDeepOffset, 0);
        _deepCapsuleSlotCloseGlyphOffset = closeGlyphOffset;
        _deepCapsuleSlotCloseGlyph = new TextBlock
        {
            Text = "×",
            Foreground = WeakTextBrush,
            FontSize = 18,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            RenderTransform = closeGlyphOffset
        };

        _deepCapsuleSlotCloseArea = new Border
        {
            Width = CapsuleCloseWidth,
            Margin = new Thickness(0, 0, 2, 0),
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(0, CapsuleInnerCornerRadius, CapsuleInnerCornerRadius, 0),
            Cursor = Cursors.Hand,
            ToolTip = Strings.Get("ToolTipHideThisPaper"),
            Child = _deepCapsuleSlotCloseGlyph
        };
        _deepCapsuleSlotCloseArea.MouseEnter += (_, _) =>
        {
            leftArea.Background = Brushes.Transparent;
            _deepCapsuleSlotCloseArea.Background = HoverBrush;
            _deepCapsuleSlotCloseGlyph.Foreground = TextBrush;
        };
        _deepCapsuleSlotCloseArea.MouseLeave += (_, _) =>
        {
            _deepCapsuleSlotCloseArea.Background = Brushes.Transparent;
            _deepCapsuleSlotCloseGlyph.Foreground = WeakTextBrush;
            _deepCapsuleSlotCloseArea.Opacity = 1.0;
        };
        _deepCapsuleSlotCloseArea.MouseLeftButtonDown += (_, e) =>
        {
            _deepCapsuleSlotCloseArea.Opacity = 0.72;
            e.Handled = true;
        };
        _deepCapsuleSlotCloseArea.MouseLeftButtonUp += (_, e) =>
        {
            _deepCapsuleSlotCloseArea.Opacity = 1.0;
            _controller.HidePaper(_paper);
            e.Handled = true;
        };

        Grid.SetColumn(_deepCapsuleSlotCloseArea, 1);
        shell.Children.Add(_deepCapsuleSlotCloseArea);

        RefreshDeepCapsuleSlotLabel();
        return shell;
    }

    private Point DeepCapsuleSlotPointerScreenPosition(MouseEventArgs e)
    {
        if (_deepCapsuleSlotShell != null && PresentationSource.FromVisual(_deepCapsuleSlotShell) != null)
        {
            return _deepCapsuleSlotShell.PointToScreen(e.GetPosition(_deepCapsuleSlotShell));
        }

        return PointToScreen(e.GetPosition(this));
    }

    private void ActivateFromDeepCapsuleSlot()
    {
        CloseDeepCapsuleSlotContextMenu();
        if (_paper.IsCollapsed)
        {
            ShowMainWindowForDeepCapsuleActivation();
            SetCollapsedState(false, alignExpandedToRight: true);
        }
        else
        {
            EnsureExpandedSurfaceGeometry(alignToRightEdge: true);
            _controller.BringPaperToFront(_paper);
        }
    }

    private void ShowMainWindowForDeepCapsuleActivation()
    {
        if (IsVisible)
        {
            BeginAnimation(Window.OpacityProperty, null);
            Opacity = 1.0;
            return;
        }

        BeginAnimation(Window.OpacityProperty, null);
        Opacity = 1.0;
        Width = DesiredCapsuleWindowWidth;
        Height = PaperLayoutDefaults.CapsuleHeight;
        if (_deepCapsuleSlotHost != null)
        {
            Left = RoundToDevicePixelX(_deepCapsuleSlotHost.Left);
            Top = RoundToDevicePixelY(_deepCapsuleSlotHost.Top);
        }
        else
        {
            Left = _paper.X;
            Top = _paper.Y;
        }
        Show();
    }

    private void HideMainWindowForDeepCapsuleRest()
    {
        if (!_paper.IsCollapsed || !_controller.State.UseCapsuleMode || !_controller.State.UseDeepCapsuleMode)
        {
            return;
        }

        if (!IsVisible)
        {
            return;
        }

        BeginAnimation(Window.OpacityProperty, null);
        Opacity = 1.0;
        Hide();
    }

    internal void HideMainWindowForDeepCapsuleMode()
    {
        HideMainWindowForDeepCapsuleRest();
    }

    public void EnsureExpandedSurfaceGeometry(bool alignToRightEdge = false)
    {
        if (_paper.IsCollapsed)
        {
            return;
        }

        var needsRestore =
            !IsVisible ||
            _isApplyingCollapsedState ||
            _isTransitionVisualsActive ||
            Width <= DesiredCapsuleWindowWidth + 8 ||
            Height <= PaperLayoutDefaults.CapsuleHeight + 8 ||
            _shell.Visibility != Visibility.Visible ||
            _capsuleShell.Visibility == Visibility.Visible;
        if (!needsRestore)
        {
            return;
        }

        BeginAnimation(TransitionProgressProperty, null);
        _shell.BeginAnimation(UIElement.OpacityProperty, null);
        _capsuleShell.BeginAnimation(UIElement.OpacityProperty, null);
        ResetTransitionVisuals();

        _isApplyingCollapsedState = false;
        _shell.Width = double.NaN;
        _shell.Height = double.NaN;
        _shell.Visibility = Visibility.Visible;
        _shell.Opacity = 1.0;
        _capsuleShell.Visibility = Visibility.Collapsed;
        _capsuleShell.Opacity = 0.0;
        MinWidth = PaperLayoutDefaults.MinWidth;
        MinHeight = PaperLayoutDefaults.MinHeight;
        ResizeMode = ResizeMode.CanResizeWithGrip;

        var targetWidth = RoundToDevicePixelX(Math.Max(_paper.Width, PaperLayoutDefaults.MinWidth));
        var targetHeight = RoundToDevicePixelY(Math.Max(_paper.Height, PaperLayoutDefaults.MinHeight));
        MoveWindowWithoutGeometrySave(() =>
        {
            Width = targetWidth;
            Height = targetHeight;
            if (alignToRightEdge)
            {
                var requiredRightInset = _controller.State.ShowDeepCapsuleWhileExpanded && _controller.CanPaperDisplayAsCapsule(_paper)
                    ? ExpandedDeepCapsuleVisibleWidth() + DeepCapsuleGap
                    : 0;
                AlignExpandedToRightEdge(targetWidth, targetHeight, requiredRightInset);
            }
        });

        if (!IsVisible)
        {
            Opacity = 1.0;
            Show();
        }

        RefreshEffectiveTopmost();
    }

    public void ExpandForProgrammaticOpen()
    {
        if (!_paper.IsCollapsed)
        {
            EnsureExpandedSurfaceGeometry(alignToRightEdge: true);
            return;
        }

        if (_controller.State.UseCapsuleMode &&
            _controller.State.UseDeepCapsuleMode &&
            HasDeepCapsuleSlotPlacement)
        {
            ShowMainWindowForDeepCapsuleActivation();
            SetCollapsedState(false);
            return;
        }

        if (!IsVisible)
        {
            BeginAnimation(Window.OpacityProperty, null);
            Opacity = 1.0;
            Left = _paper.X;
            Top = _paper.Y;
            Width = DesiredCapsuleWindowWidth;
            Height = PaperLayoutDefaults.CapsuleHeight;
            Show();
        }

        SetCollapsedState(false);
    }

    private void UpdateDeepCapsuleSlotHostTheme()
    {
        if (_deepCapsuleSlotChrome != null)
        {
            _deepCapsuleSlotChrome.Background = PaperBrush;
            _deepCapsuleSlotChrome.BorderBrush = PaperBorderBrush;
        }

        if (_deepCapsuleSlotOutline != null)
        {
            _deepCapsuleSlotOutline.BorderBrush = Theme.CapsuleFocusBorderBrush;
            _deepCapsuleSlotOutline.Visibility = IsDeepCapsuleSlotActive
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        if (_deepCapsuleSlotLabelText != null)
        {
            _deepCapsuleSlotLabelText.Foreground = WeakTextBrush;
        }

        if (_deepCapsuleSlotIconText != null)
        {
            _deepCapsuleSlotIconText.Foreground = BrightWeakTextBrush;
        }

        if (_deepCapsuleSlotCloseGlyph != null)
        {
            _deepCapsuleSlotCloseGlyph.Foreground = WeakTextBrush;
        }

    }

    private void MoveExpandedDeepCapsuleSlotHost(
        double targetLeft,
        double targetTop,
        double visibleWidth,
        bool animate,
        int durationMs = DeepCapsuleLayout.SlotMoveMilliseconds,
        bool keepHiding = false)
    {
        var host = EnsureDeepCapsuleSlotHost();
        var rightEdge = targetLeft + visibleWidth;
        var viewportWidth = visibleWidth;
        var targetHostLeft = RoundToDevicePixelX(rightEdge - viewportWidth);
        host.Height = PaperLayoutDefaults.CapsuleHeight;
        if (!keepHiding)
        {
            if (IsDeepCapsuleSlotRetracting)
            {
                SetDeepCapsuleSlotState(_paper.IsCollapsed
                    ? DeepCapsuleSlotState.CollapsedDocked
                    : DeepCapsuleSlotState.None);
            }
        }
        _deepCapsuleSlotTop = targetTop;
        if (_deepCapsuleSlotHostRoot != null)
        {
            _deepCapsuleSlotHostRoot.BeginAnimation(UIElement.OpacityProperty, null);
            _deepCapsuleSlotHostRoot.Opacity = 1;
            _deepCapsuleSlotHostRoot.IsHitTestVisible = !keepHiding;
        }

        if (!host.IsVisible)
        {
            host.BeginAnimation(Window.OpacityProperty, null);
            host.Left = targetHostLeft;
            host.Top = targetTop;
            ApplyDeepCapsuleSlotHostViewport(viewportWidth);
            _deepCapsuleSlotLeft = targetHostLeft;
            host.Opacity = _isCollapseAllRetracted ? 0 : 1;
            host.Show();
            RefreshEffectiveTopmost();
            return;
        }

        host.BeginAnimation(Window.OpacityProperty, null);
        if (!_isCollapseAllRetracted)
        {
            host.Opacity = 1;
        }

        var generation = ++_deepCapsuleSlotMoveGeneration;
        if (!animate)
        {
            host.BeginAnimation(Window.LeftProperty, null);
            host.BeginAnimation(Window.TopProperty, null);
            ClearDeepCapsuleSlotHorizontalAnimation();
            host.Left = targetHostLeft;
            host.Top = targetTop;
            ApplyDeepCapsuleSlotHostViewport(viewportWidth);
            _deepCapsuleSlotLeft = targetHostLeft;
            _deepCapsuleSlotTop = targetTop;
            return;
        }

        var currentTop = double.IsNaN(host.Top) || double.IsInfinity(host.Top) ? targetTop : RoundToDevicePixelY(host.Top);
        var currentHostLeft = double.IsNaN(host.Left) || double.IsInfinity(host.Left) ? targetHostLeft : RoundToDevicePixelX(host.Left);
        var currentViewportWidth = double.IsNaN(host.Width) || double.IsInfinity(host.Width) || host.Width <= 0
            ? viewportWidth
            : RoundToDevicePixelX(host.Width);
        var easeOut = new System.Windows.Media.Animation.CubicEase
        {
            EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut
        };

        host.BeginAnimation(Window.LeftProperty, null);
        var targetRight = targetHostLeft + viewportWidth;
        var currentRight = currentHostLeft + currentViewportWidth;
        var needsHorizontalAnimation =
            Math.Abs(currentHostLeft - targetHostLeft) >= 0.5 ||
            Math.Abs(currentRight - targetRight) >= 0.5 ||
            Math.Abs(currentViewportWidth - viewportWidth) >= 0.5;
        if (needsHorizontalAnimation)
        {
            _deepCapsuleSlotTargetLeft = targetHostLeft;
            _deepCapsuleSlotStartViewportWidth = currentViewportWidth;
            _deepCapsuleSlotTargetViewportWidth = viewportWidth;

            ApplyDeepCapsuleSlotHostViewport(currentViewportWidth);
            ApplyDeepCapsuleSlotHorizontalProgress(0.0);
            var horizontalAnim = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = 0.0,
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                EasingFunction = easeOut
            };
            horizontalAnim.Completed += (_, _) =>
            {
                if (generation != _deepCapsuleSlotMoveGeneration)
                {
                    return;
                }

                ClearDeepCapsuleSlotHorizontalAnimation();
                ApplyDeepCapsuleSlotHostViewport(viewportWidth);
                host.Left = targetHostLeft;
                _deepCapsuleSlotLeft = targetHostLeft;
            };
            BeginAnimation(DeepCapsuleSlotHorizontalProgressProperty, horizontalAnim, System.Windows.Media.Animation.HandoffBehavior.SnapshotAndReplace);
        }
        else
        {
            ClearDeepCapsuleSlotHorizontalAnimation();
            host.Left = targetHostLeft;
            ApplyDeepCapsuleSlotHostViewport(viewportWidth);
            _deepCapsuleSlotLeft = targetHostLeft;
        }

        if (Math.Abs(currentTop - targetTop) >= 0.5)
        {
            var topAnim = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = currentTop,
                To = targetTop,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                EasingFunction = easeOut
            };
            topAnim.Completed += (_, _) =>
            {
                if (generation != _deepCapsuleSlotMoveGeneration)
                {
                    return;
                }

                host.BeginAnimation(Window.TopProperty, null);
                host.Top = targetTop;
                _deepCapsuleSlotTop = targetTop;
            };
            host.BeginAnimation(Window.TopProperty, topAnim, System.Windows.Media.Animation.HandoffBehavior.SnapshotAndReplace);
        }
        else
        {
            host.BeginAnimation(Window.TopProperty, null);
            host.Top = targetTop;
            _deepCapsuleSlotTop = targetTop;
        }
    }

    private void AnimateSlotHostOpacity(double to, bool animate)
    {
        if (_deepCapsuleSlotHost == null)
        {
            return;
        }

        if (!animate || Math.Abs(_deepCapsuleSlotHost.Opacity - to) < 0.001)
        {
            _deepCapsuleSlotHost.BeginAnimation(Window.OpacityProperty, null);
            _deepCapsuleSlotHost.Opacity = to;
            return;
        }

        var anim = new System.Windows.Media.Animation.DoubleAnimation
        {
            From = _deepCapsuleSlotHost.Opacity,
            To = to,
            Duration = TimeSpan.FromMilliseconds(160),
            EasingFunction = new System.Windows.Media.Animation.CubicEase
            {
                EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut
            }
        };
        anim.Completed += (_, _) =>
        {
            _deepCapsuleSlotHost?.BeginAnimation(Window.OpacityProperty, null);
            if (_deepCapsuleSlotHost != null)
            {
                _deepCapsuleSlotHost.Opacity = to;
            }
        };
        _deepCapsuleSlotHost.BeginAnimation(Window.OpacityProperty, anim);
    }

    private void CloseExpandedDeepCapsuleSlotHostForReal()
    {
        CloseDeepCapsuleSlotContextMenu();
        if (!_paper.IsCollapsed && _deepCapsuleSlotState == DeepCapsuleSlotState.ExpandedReserved)
        {
            SetDeepCapsuleSlotState(DeepCapsuleSlotState.None);
        }
        if (_deepCapsuleSlotHost == null)
        {
            return;
        }

        ClearDeepCapsuleSlotHorizontalAnimation();
        _deepCapsuleSlotHost.Content = null;
        _deepCapsuleSlotHost.Close();
        _deepCapsuleSlotHost = null;
        _deepCapsuleSlotHostRoot = null;
        _deepCapsuleSlotChrome = null;
        _deepCapsuleSlotOutline = null;
        _deepCapsuleSlotShell = null;
        _deepCapsuleSlotIconText = null;
        _deepCapsuleSlotCloseArea = null;
        _deepCapsuleSlotCloseGlyph = null;
        _deepCapsuleSlotCloseGlyphOffset = null;
        _deepCapsuleSlotLabelText = null;
    }

    private ContextMenu BuildDeepCapsuleSlotContextMenu()
    {
        var menu = BuildPaperContextMenu(forDeepCapsuleSlot: true);

        menu.Opened += (_, _) =>
        {
            if (_deepCapsuleSlotContextMenu != null && !ReferenceEquals(_deepCapsuleSlotContextMenu, menu))
            {
                _deepCapsuleSlotContextMenu.IsOpen = false;
            }

            _deepCapsuleSlotContextMenu = menu;
            StartDeepCapsuleContextMenuGuards();
        };

        menu.Closed += (_, _) =>
        {
            if (ReferenceEquals(_deepCapsuleSlotContextMenu, menu))
            {
                _deepCapsuleSlotContextMenu = null;
                StopDeepCapsuleContextMenuGuards();
            }
        };

        return menu;
    }

    private void CloseDeepCapsuleSlotContextMenu()
    {
        var menu = _deepCapsuleSlotContextMenu;
        if (menu != null)
        {
            menu.IsOpen = false;
        }

        _deepCapsuleSlotContextMenu = null;
        StopDeepCapsuleContextMenuGuards();
    }

    private void StartDeepCapsuleContextMenuGuards()
    {
        if (_deepCapsuleForegroundHook == IntPtr.Zero)
        {
            _deepCapsuleForegroundHookProc = OnDeepCapsuleForegroundChanged;
            _deepCapsuleForegroundHook = SetWinEventHook(
                EventSystemForeground,
                EventSystemForeground,
                IntPtr.Zero,
                _deepCapsuleForegroundHookProc,
                0,
                0,
                WineventOutOfContext);
        }

        if (_deepCapsuleMouseHook == IntPtr.Zero)
        {
            _deepCapsuleMouseHookProc = OnDeepCapsuleMouseHook;
            _deepCapsuleMouseHook = SetWindowsHookEx(WhMouseLl, _deepCapsuleMouseHookProc, GetModuleHandle(null), 0);
        }
    }

    private void StopDeepCapsuleContextMenuGuards()
    {
        if (_deepCapsuleForegroundHook != IntPtr.Zero)
        {
            UnhookWinEvent(_deepCapsuleForegroundHook);
            _deepCapsuleForegroundHook = IntPtr.Zero;
        }

        _deepCapsuleForegroundHookProc = null;

        if (_deepCapsuleMouseHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_deepCapsuleMouseHook);
            _deepCapsuleMouseHook = IntPtr.Zero;
        }

        _deepCapsuleMouseHookProc = null;
    }

    private void OnDeepCapsuleForegroundChanged(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime)
    {
        if (_deepCapsuleSlotContextMenu?.IsOpen != true || hwnd == IntPtr.Zero || IsWindowFromCurrentProcess(hwnd))
        {
            return;
        }

        Dispatcher.BeginInvoke(new Action(CloseDeepCapsuleSlotContextMenu));
    }

    private IntPtr OnDeepCapsuleMouseHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && IsMouseButtonDownMessage(wParam) && _deepCapsuleSlotContextMenu?.IsOpen == true)
        {
            var hook = Marshal.PtrToStructure<MouseHookStruct>(lParam);
            var screenPoint = new Point(hook.Point.X, hook.Point.Y);
            if (!IsPointInsideDeepCapsuleContextSurface(screenPoint))
            {
                Dispatcher.BeginInvoke(new Action(CloseDeepCapsuleSlotContextMenu));
            }
        }

        return CallNextHookEx(_deepCapsuleMouseHook, nCode, wParam, lParam);
    }

    private bool IsPointInsideDeepCapsuleContextSurface(Point screenPoint)
    {
        if (IsPointInsideElement(_deepCapsuleSlotContextMenu, screenPoint))
        {
            return true;
        }

        return _deepCapsuleSlotHost?.IsVisible == true && IsPointInsideWindow(_deepCapsuleSlotHost, screenPoint);
    }

    private static bool IsPointInsideElement(FrameworkElement? element, Point screenPoint)
    {
        if (element == null || element.ActualWidth <= 0 || element.ActualHeight <= 0)
        {
            return false;
        }

        try
        {
            var localPoint = element.PointFromScreen(screenPoint);
            return localPoint.X >= 0 &&
                localPoint.Y >= 0 &&
                localPoint.X <= element.ActualWidth &&
                localPoint.Y <= element.ActualHeight;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool IsPointInsideWindow(Window window, Point screenPoint)
    {
        try
        {
            var localPoint = window.PointFromScreen(screenPoint);
            return localPoint.X >= 0 &&
                localPoint.Y >= 0 &&
                localPoint.X <= window.ActualWidth &&
                localPoint.Y <= window.ActualHeight;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool IsMouseButtonDownMessage(IntPtr message)
    {
        var value = message.ToInt32();
        return value == WmLButtonDown ||
            value == WmRButtonDown ||
            value == WmMButtonDown ||
            value == WmXButtonDown;
    }

    private static bool IsWindowFromCurrentProcess(IntPtr hwnd)
    {
        GetWindowThreadProcessId(hwnd, out var processId);
        return processId == Environment.ProcessId;
    }

    private void ClearDeepCapsulePositionAnimation()
    {
        ClearDeepCapsuleLeftPositionAnimation();
        ClearDeepCapsuleTopPositionAnimation();
    }

    private void ClearDeepCapsuleLeftPositionAnimation()
    {
        BeginAnimation(DeepCapsuleAnimatedLeftProperty, null);
        BeginAnimation(Window.LeftProperty, null);
    }

    private void ClearDeepCapsuleTopPositionAnimation()
    {
        BeginAnimation(DeepCapsuleAnimatedTopProperty, null);
        BeginAnimation(Window.TopProperty, null);
    }

    private void ClearDeepCapsuleSlotHorizontalAnimation()
    {
        BeginAnimation(DeepCapsuleSlotHorizontalProgressProperty, null);
    }

    private bool IsDeepCapsuleSlotHorizontalAnimating => !double.IsNaN(DeepCapsuleSlotHorizontalProgress);

    private static Rect DeepCapsuleWorkArea()
    {
        return DeepCapsuleLayout.WorkArea;
    }

    private void ApplyDeepCapsuleSlotHostViewport(double viewportWidth)
    {
        if (_deepCapsuleSlotHost == null)
        {
            return;
        }

        _deepCapsuleSlotHost.Width = DeepCapsuleSlotViewportWidth(viewportWidth);
        _deepCapsuleSlotHost.Height = PaperLayoutDefaults.CapsuleHeight;
        ApplyDeepCapsuleSlotFixedLayout();
    }

    private void ApplyDeepCapsuleSlotFixedLayout()
    {
        var fullWidth = CapsuleWindowWidth(usesDeepCapsulePresentation: true);
        var outlineMargin = WindowChromeMargin - DeepCapsuleSlotOutlineThickness + DeepCapsuleSlotOutlineOverlap;

        if (_deepCapsuleSlotChrome != null)
        {
            _deepCapsuleSlotChrome.Margin = new Thickness(WindowChromeMargin, WindowChromeMargin, 0, WindowChromeMargin);
            _deepCapsuleSlotChrome.Width = Math.Max(0, fullWidth - WindowChromeMargin);
        }
        if (_deepCapsuleSlotShell != null)
        {
            _deepCapsuleSlotShell.Margin = new Thickness(WindowChromeMargin, WindowChromeMargin, 0, WindowChromeMargin);
            _deepCapsuleSlotShell.Width = DeepCapsuleSlotShellLayoutWidth();
        }
        if (_deepCapsuleSlotOutline != null)
        {
            _deepCapsuleSlotOutline.Margin = new Thickness(outlineMargin, outlineMargin, 0, outlineMargin);
            _deepCapsuleSlotOutline.Width = Math.Max(0, fullWidth - outlineMargin);
        }
    }

    private void ApplyDeepCapsuleSlotHorizontalProgress(double progress)
    {
        if (_deepCapsuleSlotHost == null)
        {
            return;
        }

        progress = Math.Clamp(progress, 0.0, 1.0);
        var viewportWidth = Lerp(_deepCapsuleSlotStartViewportWidth, _deepCapsuleSlotTargetViewportWidth, progress);
        var anchorRight = _deepCapsuleSlotTargetLeft + _deepCapsuleSlotTargetViewportWidth;
        var left = anchorRight - viewportWidth;

        _deepCapsuleSlotHost.Left = RoundToDevicePixelX(left);
        _deepCapsuleSlotHost.Width = DeepCapsuleSlotViewportWidth(RoundToDevicePixelX(viewportWidth));
        _deepCapsuleSlotHost.Height = PaperLayoutDefaults.CapsuleHeight;
        _deepCapsuleSlotLeft = _deepCapsuleSlotHost.Left;
    }

    private static double Lerp(double from, double to, double progress)
    {
        return from + (to - from) * progress;
    }

    private double DeepCapsuleSlotShellLayoutWidth()
    {
        var fullWidth = CapsuleWindowWidth(usesDeepCapsulePresentation: true);
        return Math.Max(0, Math.Max(
            CapsuleShellWidth(usesDeepCapsulePresentation: true),
            fullWidth - WindowChromeMargin));
    }

    private double DeepCapsuleSlotViewportWidth(double viewportWidth)
    {
        return Math.Clamp(viewportWidth, 1, CapsuleWindowWidth(usesDeepCapsulePresentation: true));
    }

    private double DeepCapsuleTopForIndex(int index)
    {
        return DeepCapsuleLayout.TopForIndex(index, _controller.State.DeepCapsuleStartTopMargin);
    }

    private void MoveDeepCapsuleToCurrentTarget(
        bool animate = false,
        int durationMs = DeepCapsuleLayout.SlotMoveMilliseconds,
        bool keepHiding = false,
        bool forceRestingOffset = false)
    {
        if (!HasDeepCapsuleSlotPlacement || _isCollapseAllRetracted)
        {
            return;
        }

        var area = DeepCapsuleWorkArea();
        var capsuleWidth = CapsuleWindowWidth(usesDeepCapsulePresentation: true);
        var deepCapsuleVisibleWidth = DeepCapsuleVisibleWidth();
        var shouldUseActiveOffset = !keepHiding &&
            !forceRestingOffset &&
            (_deepCapsuleVisualState is DeepCapsuleVisualState.Hovered or DeepCapsuleVisualState.Active);
        var visibleWidth = shouldUseActiveOffset
            ? ExpandedDeepCapsuleVisibleWidth()
            : deepCapsuleVisibleWidth;
        var targetLeft = RoundToDevicePixelX(area.Right - visibleWidth);
        var targetTop = RoundToDevicePixelY(DeepCapsuleTopForIndex(_deepCapsuleIndex + _deepCapsuleVisualOffset));

        MoveExpandedDeepCapsuleSlotHost(
            targetLeft,
            targetTop,
            visibleWidth,
            animate,
            durationMs,
            keepHiding);
    }

    private double DeepCapsuleVisibleWidth()
    {
        var capsuleWidth = CapsuleWindowWidth(usesDeepCapsulePresentation: true);
        // Resting edge-attached state is a docked tag, not a cropped full capsule. Keep the
        // close area fully off-screen and size the visible part to the icon + title only.
        return Math.Clamp(
            WindowChromeMargin + CapsuleLeftPadding + MeasureCapsuleIconWidth() + CapsuleIconGap + MeasureCapsuleTitleWidth() + 4,
            34,
            Math.Max(34, capsuleWidth - WindowChromeMargin - 24));
    }

    private double ExpandedDeepCapsuleVisibleWidth()
    {
        return DeepCapsuleLayout.FocusVisibleWidth(
            CapsuleWindowWidth(usesDeepCapsulePresentation: true),
            DeepCapsuleVisibleWidth());
    }

    private bool IsLikelyAtDeepCapsuleEdge(double capsuleWidth)
    {
        if (!_paper.IsCollapsed || !_controller.State.UseCapsuleMode || !_controller.State.UseDeepCapsuleMode)
        {
            return false;
        }

        var area = DeepCapsuleWorkArea();
        var minVisibleWidth = Math.Min(DeepCapsuleVisibleWidth(), ExpandedDeepCapsuleVisibleWidth());
        var leftEdgeThreshold = area.Right - capsuleWidth - DeepCapsuleGap;
        var rightEdgeThreshold = area.Right - minVisibleWidth + DeepCapsuleGap;
        var withinVerticalStack = Top >= area.Top + DeepCapsuleTopMargin - DeepCapsuleGap
            && Top <= area.Bottom - PaperLayoutDefaults.CapsuleHeight + DeepCapsuleGap;
        return withinVerticalStack && Left >= leftEdgeThreshold && Left <= rightEdgeThreshold;
    }

    // Shared position animator for every deep-capsule move (slot placement, hover peek,
    // retract, release). A generation token guards completions so a superseded animation's
    // Completed handler never snaps the window to a stale target.
    private void BeginDeepCapsuleMove(double targetLeft, double targetTop, int leftDurationMs, int topDurationMs, bool animate)
    {
        MoveWindowWithoutGeometrySave(() =>
        {
            Width = CapsuleWindowWidth();
            Height = PaperLayoutDefaults.CapsuleHeight;
        });
        UpdateCapsuleClosePlacement();

        var gen = ++_deepCapsuleMoveGeneration;

        if (!animate)
        {
            ClearDeepCapsulePositionAnimation();
            MoveWindowWithoutGeometrySave(() =>
            {
                Left = targetLeft;
                Top = targetTop;
            });
            UpdateCapsuleClosePlacement();
            return;
        }

        var currentLeft = double.IsNaN(Left) || double.IsInfinity(Left) ? targetLeft : RoundToDevicePixelX(Left);
        var currentTop = double.IsNaN(Top) || double.IsInfinity(Top) ? targetTop : RoundToDevicePixelY(Top);
        var animateLeft = Math.Abs(currentLeft - targetLeft) >= 0.5;
        var animateTop = Math.Abs(currentTop - targetTop) >= 0.5;

        if (!animateLeft && !animateTop)
        {
            ClearDeepCapsulePositionAnimation();
            MoveWindowWithoutGeometrySave(() =>
            {
                Left = targetLeft;
                Top = targetTop;
            });
            UpdateCapsuleClosePlacement();
            return;
        }

        var easeOut = new System.Windows.Media.Animation.CubicEase
        {
            EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut
        };

        if (animateLeft)
        {
            var leftAnimation = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = currentLeft,
                To = targetLeft,
                Duration = TimeSpan.FromMilliseconds(leftDurationMs),
                EasingFunction = easeOut
            };
            leftAnimation.Completed += (_, _) =>
            {
                if (gen != _deepCapsuleMoveGeneration)
                {
                    return;
                }

                MoveWindowWithoutGeometrySave(() =>
                {
                    ClearDeepCapsuleLeftPositionAnimation();
                    Left = targetLeft;
                });
                UpdateCapsuleClosePlacement();
            };

            BeginAnimation(DeepCapsuleAnimatedLeftProperty, leftAnimation, System.Windows.Media.Animation.HandoffBehavior.SnapshotAndReplace);
        }
        else
        {
            ClearDeepCapsuleLeftPositionAnimation();
            MoveWindowWithoutGeometrySave(() => Left = targetLeft);
        }

        if (animateTop)
        {
            var topAnimation = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = currentTop,
                To = targetTop,
                Duration = TimeSpan.FromMilliseconds(topDurationMs),
                EasingFunction = easeOut
            };
            topAnimation.Completed += (_, _) =>
            {
                if (gen != _deepCapsuleMoveGeneration)
                {
                    return;
                }

                MoveWindowWithoutGeometrySave(() =>
                {
                    ClearDeepCapsuleTopPositionAnimation();
                    Top = targetTop;
                });
            };

            BeginAnimation(DeepCapsuleAnimatedTopProperty, topAnimation, System.Windows.Media.Animation.HandoffBehavior.SnapshotAndReplace);
        }
        else
        {
            ClearDeepCapsuleTopPositionAnimation();
            MoveWindowWithoutGeometrySave(() => Top = targetTop);
        }
    }

    // Slide this capsule up to the master's slot and fade it out. The window stays shown
    // (so it keeps counting as a deep-capsule member) but, being a per-pixel transparent
    // window at Opacity 0, it is fully click-through and never blocks the master pill.
    public void RetractIntoMaster(double anchorTop, bool animate)
    {
        if (!OccupiesDeepCapsuleSlot || !_controller.State.UseCapsuleMode || !_controller.State.UseDeepCapsuleMode || !_paper.IsVisible)
        {
            ClearDeepCapsulePlacement();
            return;
        }

        SetDeepCapsuleSlotState(DeepCapsuleSlotState.CollapsedDocked);
        _isCollapseAllRetracted = true;
        SetDeepCapsuleVisualState(DeepCapsuleVisualState.Resting);
        RefreshEffectiveTopmost();

        var area = DeepCapsuleWorkArea();
        var currentSlotVisible = _deepCapsuleSlotHost?.IsVisible == true &&
            !double.IsNaN(_deepCapsuleSlotHost.Width) &&
            !double.IsInfinity(_deepCapsuleSlotHost.Width) &&
            _deepCapsuleSlotHost.Width > 0;
        var visibleWidth = currentSlotVisible
            ? DeepCapsuleSlotViewportWidth(_deepCapsuleSlotHost!.Width)
            : DeepCapsuleVisibleWidth();
        var targetLeft = currentSlotVisible &&
            !double.IsNaN(_deepCapsuleSlotHost!.Left) &&
            !double.IsInfinity(_deepCapsuleSlotHost.Left)
                ? RoundToDevicePixelX(_deepCapsuleSlotHost.Left)
                : RoundToDevicePixelX(area.Right - visibleWidth);
        var targetTop = RoundToDevicePixelY(anchorTop);

        MoveExpandedDeepCapsuleSlotHost(targetLeft, targetTop, visibleWidth, animate);
        if (_deepCapsuleSlotHost != null)
        {
            AnimateSlotHostOpacity(0.0, animate);
        }
        if (_paper.IsCollapsed)
        {
            HideMainWindowForDeepCapsuleRest();
        }
    }

    private void SetDeepCapsuleHover(bool hovering)
    {
        if (IsDeepCapsuleReordering || !HasDeepCapsuleSlotPlacement || !_paper.IsCollapsed || !_controller.State.UseDeepCapsuleMode)
        {
            return;
        }

        if (IsDeepCapsuleSlotActive)
        {
            return;
        }

        SetDeepCapsuleVisualState(hovering ? DeepCapsuleVisualState.Hovered : DeepCapsuleVisualState.Resting);
        MoveDeepCapsuleToCurrentTarget(animate: true);
    }

    public void ApplyDeepCapsulePlacement(int index, bool animate = false, int visualOffset = 0)
    {
        if (!_paper.IsCollapsed || !_paper.IsVisible || !_controller.State.UseCapsuleMode || !_controller.State.UseDeepCapsuleMode)
        {
            ClearDeepCapsulePlacement();
            return;
        }

        var keepActiveUntilRetracted = animate &&
            IsDeepCapsuleSlotActive &&
            _deepCapsuleSlotHost?.IsVisible == true;

        SetDeepCapsuleSlotState(DeepCapsuleSlotState.CollapsedDocked);
        if (!_isApplyingCollapsedState && !keepActiveUntilRetracted)
        {
            SetDeepCapsuleVisualState(DeepCapsuleVisualState.Resting);
        }
        if (keepActiveUntilRetracted)
        {
            SetDeepCapsuleVisualState(DeepCapsuleVisualState.Resting);
        }
        _isCollapseAllRetracted = false;
        _deepCapsuleIndex = Math.Max(0, index);
        _deepCapsuleVisualOffset = Math.Max(0, visualOffset);
        RefreshCapsuleLabel();
        MoveDeepCapsuleToCurrentTarget(
            animate,
            keepActiveUntilRetracted ? 120 : DeepCapsuleLayout.SlotMoveMilliseconds,
            forceRestingOffset: keepActiveUntilRetracted);
        if (keepActiveUntilRetracted)
        {
            ClearDeepCapsuleSlotActiveAfterMove(120);
        }
        AnimateSlotHostOpacity(1.0, animate);
        if (!_isApplyingCollapsedState)
        {
            HideMainWindowForDeepCapsuleRest();
        }
        RefreshEffectiveTopmost();
    }

    public void ApplyExpandedDeepCapsuleSlotPlacement(int index, bool animate = false, int visualOffset = 0)
    {
        var shouldReserveWhileExpanded = _controller.State.ShowDeepCapsuleWhileExpanded &&
            _controller.CanPaperDisplayAsCapsule(_paper);
        if (_paper.IsCollapsed ||
            !shouldReserveWhileExpanded ||
            !_controller.State.UseCapsuleMode ||
            !_controller.State.UseDeepCapsuleMode ||
            !_paper.IsVisible)
        {
            ClearExpandedDeepCapsuleSlotPlacement();
            return;
        }

        SetDeepCapsuleOpenOrigin(DeepCapsuleOpenOrigin.EdgeSlot);
        SetDeepCapsuleSlotState(DeepCapsuleSlotState.ExpandedReserved);
        SetDeepCapsuleVisualState(DeepCapsuleVisualState.Active);
        _isCollapseAllRetracted = false;
        _deepCapsuleIndex = Math.Max(0, index);
        _deepCapsuleVisualOffset = Math.Max(0, visualOffset);
        RefreshCapsuleLabel();
        UpdateDeepCapsuleSlotHostTheme();

        var area = DeepCapsuleWorkArea();
        var visibleWidth = ExpandedDeepCapsuleVisibleWidth();
        var targetLeft = RoundToDevicePixelX(area.Right - visibleWidth);
        var targetTop = RoundToDevicePixelY(DeepCapsuleTopForIndex(index + visualOffset));
        RefreshDeepCapsuleSlotLabel();

        var firstShow = _deepCapsuleSlotHost?.IsVisible != true;
        if (firstShow)
        {
            MoveExpandedDeepCapsuleSlotHost(targetLeft, targetTop, visibleWidth, animate: false);
        }
        else
        {
            MoveExpandedDeepCapsuleSlotHost(targetLeft, targetTop, visibleWidth, animate);
        }
        UpdateDeepCapsuleSlotClosePlacement();
        AnimateSlotHostOpacity(1.0, animate);
        RefreshEffectiveTopmost();
        UpdateToolTipSetting();
    }

    public void ClearExpandedDeepCapsuleSlotPlacement(bool animate = false)
    {
        var wasActive = IsDeepCapsuleSlotActive;
        var keepActiveUntilRetracted = animate &&
            wasActive &&
            _paper.IsCollapsed &&
            HasDeepCapsuleSlotPlacement &&
            _deepCapsuleSlotHost?.IsVisible == true;
        _deepCapsuleSlotMoveGeneration++;
        if (_deepCapsuleSlotState == DeepCapsuleSlotState.ExpandedReserved)
        {
            SetDeepCapsuleSlotState(_paper.IsCollapsed ? DeepCapsuleSlotState.CollapsedDocked : DeepCapsuleSlotState.None);
        }
        if (!_isApplyingCollapsedState && !keepActiveUntilRetracted)
        {
            SetDeepCapsuleVisualState(DeepCapsuleVisualState.Resting);
        }
        if (wasActive && !_isApplyingCollapsedState && !keepActiveUntilRetracted)
        {
            SetDeepCapsuleVisualState(DeepCapsuleVisualState.Resting);
        }
        if (keepActiveUntilRetracted)
        {
            SetDeepCapsuleVisualState(DeepCapsuleVisualState.Resting);
        }
        if (_deepCapsuleSlotState == DeepCapsuleSlotState.Retracting)
        {
            SetDeepCapsuleSlotState(_paper.IsCollapsed ? DeepCapsuleSlotState.CollapsedDocked : DeepCapsuleSlotState.None);
        }
        UpdateDeepCapsuleSlotHostTheme();
        UpdateDeepCapsuleSlotClosePlacement(updateHostViewport: !_paper.IsCollapsed || !HasDeepCapsuleSlotPlacement);

        if (_paper.IsCollapsed && HasDeepCapsuleSlotPlacement)
        {
            MoveDeepCapsuleToCurrentTarget(
                animate,
                keepActiveUntilRetracted ? 120 : DeepCapsuleLayout.SlotMoveMilliseconds,
                forceRestingOffset: keepActiveUntilRetracted);
        }
    }

    private void ClearDeepCapsuleSlotActiveAfterMove(int durationMs)
    {
        var generation = _deepCapsuleSlotMoveGeneration;
        var timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(durationMs + 20)
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (generation != _deepCapsuleSlotMoveGeneration)
            {
                return;
            }

            SetDeepCapsuleVisualState(DeepCapsuleVisualState.Resting);
            UpdateDeepCapsuleSlotHostTheme();
            UpdateDeepCapsuleSlotClosePlacement();
        };
        timer.Start();
    }

    private void HideExpandedDeepCapsuleSlotHost(bool animate)
    {
        if (_deepCapsuleSlotHost == null)
        {
            return;
        }

        if (!animate || !_deepCapsuleSlotHost.IsVisible || _deepCapsuleSlotHostRoot == null)
        {
            if (IsDeepCapsuleSlotRetracting)
            {
                SetDeepCapsuleSlotState(DeepCapsuleSlotState.None);
            }
            _deepCapsuleSlotHostRoot?.BeginAnimation(UIElement.OpacityProperty, null);
            if (_deepCapsuleSlotHostRoot != null)
            {
                _deepCapsuleSlotHostRoot.Opacity = 1.0;
            }
            ClearDeepCapsuleSlotHorizontalAnimation();
            _deepCapsuleSlotHost.Hide();
            return;
        }

        if (IsDeepCapsuleSlotRetracting)
        {
            return;
        }

        SetDeepCapsuleSlotState(DeepCapsuleSlotState.Retracting);
        _deepCapsuleSlotHostRoot.IsHitTestVisible = false;
        var hideGeneration = _deepCapsuleSlotMoveGeneration;
        var fadeOut = new System.Windows.Media.Animation.DoubleAnimation
        {
            From = _deepCapsuleSlotHostRoot.Opacity,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(110),
            EasingFunction = new System.Windows.Media.Animation.CubicEase
            {
                EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut
            }
        };
        fadeOut.Completed += (_, _) =>
        {
            if (hideGeneration != _deepCapsuleSlotMoveGeneration || _deepCapsuleSlotHost == null)
            {
                return;
            }

            if (_deepCapsuleSlotState == DeepCapsuleSlotState.Retracting)
            {
                SetDeepCapsuleSlotState(DeepCapsuleSlotState.None);
            }
            _deepCapsuleSlotHostRoot?.BeginAnimation(UIElement.OpacityProperty, null);
            _deepCapsuleSlotHost.Hide();
            if (_deepCapsuleSlotHostRoot != null)
            {
                _deepCapsuleSlotHostRoot.Opacity = 1.0;
                _deepCapsuleSlotHostRoot.IsHitTestVisible = true;
            }
        };
        _deepCapsuleSlotHostRoot.BeginAnimation(UIElement.OpacityProperty, fadeOut, System.Windows.Media.Animation.HandoffBehavior.SnapshotAndReplace);
    }

    private void RetractAndHideDeepCapsuleSlotHost(bool animate)
    {
        if (_deepCapsuleSlotHost == null)
        {
            return;
        }

        var root = _deepCapsuleSlotHostRoot;
        if (!animate || !_deepCapsuleSlotHost.IsVisible || root == null || _deepCapsuleSlotHost.Opacity < 0.05)
        {
            HideExpandedDeepCapsuleSlotHost(animate: false);
            return;
        }

        SetDeepCapsuleSlotState(DeepCapsuleSlotState.Retracting);
        SetDeepCapsuleVisualState(DeepCapsuleVisualState.Resting);
        UpdateDeepCapsuleSlotHostTheme();
        UpdateDeepCapsuleSlotClosePlacement(updateHostViewport: false);

        root.BeginAnimation(UIElement.OpacityProperty, null);
        root.Opacity = 1.0;
        root.IsHitTestVisible = false;

        MoveDeepCapsuleToCurrentTarget(animate: true, durationMs: 120, keepHiding: true);
        var generation = _deepCapsuleSlotMoveGeneration;

        var finishTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(125)
        };
        finishTimer.Tick += (_, _) =>
        {
            finishTimer.Stop();
            BeginDeepCapsuleSlotHideFade(generation);
        };
        finishTimer.Start();
    }

    private void BeginDeepCapsuleSlotHideFade(int generation)
    {
        if (_deepCapsuleSlotHostRoot == null)
        {
            return;
        }

        var fadeOut = new System.Windows.Media.Animation.DoubleAnimation
        {
            From = 1.0,
            To = 0.0,
            Duration = TimeSpan.FromMilliseconds(45),
            EasingFunction = new System.Windows.Media.Animation.CubicEase
            {
                EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut
            }
        };
        fadeOut.Completed += (_, _) =>
        {
            if (generation != _deepCapsuleSlotMoveGeneration || _deepCapsuleSlotHost == null)
            {
                return;
            }

            SetDeepCapsuleSlotState(DeepCapsuleSlotState.None);
            SetDeepCapsuleVisualState(DeepCapsuleVisualState.Resting);
            _isCollapseAllRetracted = false;
            _deepCapsuleVisualOffset = 0;
            _deepCapsuleIndex = -1;
            UpdateDeepCapsuleSlotHostTheme();
            UpdateDeepCapsuleSlotClosePlacement();
            _deepCapsuleSlotHost.Hide();
            if (_deepCapsuleSlotHostRoot != null)
            {
                _deepCapsuleSlotHostRoot.BeginAnimation(UIElement.OpacityProperty, null);
                _deepCapsuleSlotHostRoot.Opacity = 1.0;
                _deepCapsuleSlotHostRoot.IsHitTestVisible = true;
            }
        };
        _deepCapsuleSlotHostRoot.BeginAnimation(UIElement.OpacityProperty, fadeOut, System.Windows.Media.Animation.HandoffBehavior.SnapshotAndReplace);
    }

    public void ClearDeepCapsulePlacement(bool restoreCollapsedPosition = true, bool animate = false)
    {
        var shouldRetractBeforeHide = animate &&
            _deepCapsuleSlotHost?.IsVisible == true &&
            _deepCapsuleSlotHostRoot != null &&
            HasDeepCapsuleSlotPlacement &&
            !_isCollapseAllRetracted;

        if (shouldRetractBeforeHide)
        {
            ClearDeepCapsulePositionAnimation();
            RetractAndHideDeepCapsuleSlotHost(animate: true);
        }
        else
        {
            ClearDeepCapsulePositionAnimation();
            SetDeepCapsuleSlotState(DeepCapsuleSlotState.None);
            SetDeepCapsuleVisualState(DeepCapsuleVisualState.Resting);
            _isCollapseAllRetracted = false;
            _deepCapsuleVisualOffset = 0;
            _deepCapsuleIndex = -1;
            UpdateCapsuleClosePlacement();
            HideExpandedDeepCapsuleSlotHost(animate);
        }

        // A capsule may have been faded out while retracted behind the master; never leave
        // a live (expanded or free-floating) window invisible.
        if (Math.Abs(Opacity - 1.0) > 0.001)
        {
            BeginAnimation(OpacityProperty, null);
            Opacity = 1.0;
        }
    }

    public void ClearDeepCapsuleSlotReservation(bool animate = false)
    {
        if (_deepCapsuleSlotState == DeepCapsuleSlotState.ExpandedReserved)
        {
            SetDeepCapsuleSlotState(_paper.IsCollapsed ? DeepCapsuleSlotState.CollapsedDocked : DeepCapsuleSlotState.None);
        }
        ClearExpandedDeepCapsuleSlotPlacement(animate);
    }

    public void UpdateDeepCapsuleMode()
    {
        if (!_controller.State.UseCapsuleMode || !_controller.State.UseDeepCapsuleMode)
        {
            if (_deepCapsuleSlotState == DeepCapsuleSlotState.ExpandedReserved)
            {
                SetDeepCapsuleSlotState(DeepCapsuleSlotState.None);
            }
            ClearDeepCapsulePlacement();
        }
        else if (!_paper.IsCollapsed)
        {
            ClearDeepCapsulePlacement(restoreCollapsedPosition: false);
        }
        else
        {
            MoveDeepCapsuleToCurrentTarget();
        }

        RefreshEffectiveTopmost();
    }

    public void UpdateDeepCapsuleExpandedSlotMode()
    {
        if (_paper.IsCollapsed)
        {
            return;
        }

        if (!_paper.IsVisible || !_controller.State.UseCapsuleMode || !_controller.State.UseDeepCapsuleMode)
        {
            if (_deepCapsuleSlotState == DeepCapsuleSlotState.ExpandedReserved)
            {
                SetDeepCapsuleSlotState(DeepCapsuleSlotState.None);
            }
            return;
        }

        if (_controller.State.ShowDeepCapsuleWhileExpanded && _controller.CanPaperDisplayAsCapsule(_paper))
        {
            SetDeepCapsuleSlotState(DeepCapsuleSlotState.ExpandedReserved);
            SetDeepCapsuleVisualState(DeepCapsuleVisualState.Active);
            _isCollapseAllRetracted = false;
            RefreshCapsuleLabel();
            UpdateDeepCapsuleSlotHostTheme();
            UpdateDeepCapsuleSlotClosePlacement();
            return;
        }

        if (!_controller.State.ShowDeepCapsuleWhileExpanded && HoldsDeepCapsuleSlotWhileExpanded)
        {
            SetDeepCapsuleSlotState(DeepCapsuleSlotState.None);
            SetDeepCapsuleVisualState(DeepCapsuleVisualState.Resting);
            ClearDeepCapsulePlacement(restoreCollapsedPosition: false, animate: _controller.State.EnableAnimations);
        }
    }

    private void StartDeepCapsuleReorderDrag(Point currentScreenPos)
    {
        if (!CanReorderDeepCapsuleSlot() || _deepCapsuleSlotHost == null)
        {
            return;
        }

        SetDeepCapsuleGestureState(DeepCapsuleGestureState.Reordering);
        SetDeepCapsuleVisualState(DeepCapsuleVisualState.Hovered);
        // currentScreenPos is in physical device pixels (PointToScreen); Top is in DIPs.
        // Convert to DIPs so the capsule tracks the cursor 1:1 at any DPI.
        var dpiScaleY = VisualTreeHelper.GetDpi(this).DpiScaleY;
        _deepCapsuleDragMouseOffsetY = currentScreenPos.Y / dpiScaleY - _deepCapsuleSlotHost.Top;

        _deepCapsuleSlotHost.BeginAnimation(Window.LeftProperty, null);
        _deepCapsuleSlotHost.BeginAnimation(Window.TopProperty, null);

        var area = DeepCapsuleWorkArea();
        var visibleWidth = ExpandedDeepCapsuleVisibleWidth();
        _deepCapsuleDragLeft = RoundToDevicePixelX(area.Right - visibleWidth);

        _deepCapsuleSlotHost.Left = _deepCapsuleDragLeft;
        ApplyDeepCapsuleSlotHostViewport(visibleWidth);
        _deepCapsuleSlotLeft = _deepCapsuleSlotHost.Left;

        Mouse.OverrideCursor = Cursors.SizeNS;
        UpdateDeepCapsuleReorderDrag(currentScreenPos);
    }

    private void UpdateDeepCapsuleReorderDrag(Point currentScreenPos)
    {
        if (!IsDeepCapsuleReordering)
        {
            return;
        }

        var area = DeepCapsuleWorkArea();
        var minTop = area.Top + DeepCapsuleTopMargin;
        var maxTop = Math.Max(minTop, area.Bottom - PaperLayoutDefaults.CapsuleHeight - DeepCapsuleTopMargin);
        // currentScreenPos is in physical device pixels; convert to DIPs to match Top/offset.
        var dpiScaleY = VisualTreeHelper.GetDpi(this).DpiScaleY;
        var targetTop = Math.Clamp(currentScreenPos.Y / dpiScaleY - _deepCapsuleDragMouseOffsetY, minTop, maxTop);

        if (_deepCapsuleSlotHost != null)
        {
            _deepCapsuleSlotHost.Left = _deepCapsuleDragLeft;
            _deepCapsuleSlotHost.Top = RoundToDevicePixelY(targetTop);
            _deepCapsuleSlotLeft = _deepCapsuleSlotHost.Left;
            _deepCapsuleSlotTop = _deepCapsuleSlotHost.Top;
        }
    }

    private void EndDeepCapsuleReorderDrag(bool commit)
    {
        if (!IsDeepCapsuleReordering)
        {
            return;
        }

        SetDeepCapsuleGestureState(DeepCapsuleGestureState.Idle);
        Mouse.OverrideCursor = null;
        SetDeepCapsuleVisualState(
            _deepCapsuleSlotShell?.IsMouseOver == true
                ? DeepCapsuleVisualState.Hovered
                : DeepCapsuleVisualState.Resting);

        if (commit)
        {
            _controller.ReorderDeepCapsule(_paper, DeepCapsuleDropIndexForCurrentPosition());
            return;
        }

        MoveDeepCapsuleToCurrentTarget();
    }

    private bool CanReorderDeepCapsuleSlot()
    {
        return HasDeepCapsuleSlotPlacement &&
            _deepCapsuleSlotHost?.IsVisible == true &&
            (_paper.IsCollapsed || (_controller.State.ShowDeepCapsuleWhileExpanded && IsDeepCapsuleSlotActive));
    }

    private int DeepCapsuleDropIndexForCurrentPosition()
    {
        var count = _controller.VisibleDeepCapsuleCount();
        if (count <= 1)
        {
            return 0;
        }

        var centerY = (_deepCapsuleSlotHost?.Top ?? _deepCapsuleSlotTop) + (PaperLayoutDefaults.CapsuleHeight / 2);
        var area = DeepCapsuleWorkArea();
        // Real capsules start at slot _deepCapsuleVisualOffset when the master capsule occupies slot 0.
        var firstCenterY = DeepCapsuleTopForIndex(_deepCapsuleVisualOffset) + (PaperLayoutDefaults.CapsuleHeight / 2);
        var slotHeight = PaperLayoutDefaults.CapsuleHeight + DeepCapsuleGap;
        var originalIndex = Math.Clamp(_deepCapsuleIndex, 0, count - 1);
        var rawIndex = (centerY - firstCenterY) / slotHeight;
        var index = rawIndex >= originalIndex
            ? (int)Math.Floor(rawIndex)
            : (int)Math.Ceiling(rawIndex);
        return Math.Clamp(index, 0, count - 1);
    }

    private void AlignExpandedToRightEdge(double targetWidth, double targetHeight, double requiredRightInset = 0)
    {
        var area = DeepCapsuleWorkArea();
        var width = Math.Max(targetWidth, PaperLayoutDefaults.MinWidth);
        var height = Math.Max(targetHeight, PaperLayoutDefaults.MinHeight);
        var rightInset = Math.Min(
            Math.Max(
                Math.Max(DeepCapsuleExpandedRightInset, requiredRightInset),
                _controller.VisibleDeepCapsuleRestingWidth() + DeepCapsuleGap),
            Math.Max(0, area.Width - width));
        var targetTop = Math.Clamp(Top, area.Top + DeepCapsuleTopMargin, Math.Max(area.Top + DeepCapsuleTopMargin, area.Bottom - height - DeepCapsuleTopMargin));

        Left = RoundToDevicePixelX(area.Right - width - rightInset);
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

}
