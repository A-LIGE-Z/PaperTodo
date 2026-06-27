using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.Win32;
using Application = System.Windows.Application;

namespace PaperTodo;

public sealed partial class AppController
{
    private const string AuthorName = "Designed by trigger";
    private const string AuthorGithubUrl = "https://github.com/snownico0722";

    private void SetTheme(string theme)
    {
        State.Theme = theme;
        Theme.Invalidate();
        SaveNow();

        foreach (var window in _windows.Values)
        {
            window.UpdateTheme();
        }
        foreach (var m in _masterCapsules.Values) m.UpdateTheme();

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

    private void SetColorScheme(string scheme)
    {
        if (!ColorSchemes.IsValid(scheme))
        {
            return;
        }

        State.ColorScheme = scheme;
        Theme.Invalidate();
        SaveNow();

        foreach (var window in _windows.Values)
        {
            window.UpdateTheme();
        }
        foreach (var m in _masterCapsules.Values) m.UpdateTheme();

        RebuildTrayMenu();
        RefreshSettingsWindowContent();
    }

    private UIElement CreateColorSchemeSegmentSelector()
    {
        var segments = new[]
        {
            (ColorSchemes.Warm, Strings.Get("ColorSchemeWarm")),
            (ColorSchemes.Ink, Strings.Get("ColorSchemeInk")),
            (ColorSchemes.Forest, Strings.Get("ColorSchemeForest")),
            (ColorSchemes.Rose, Strings.Get("ColorSchemeRose"))
        };

        return CreateSegmentSelector(segments, ColorSchemes.Normalize(State.ColorScheme), SetColorScheme);
    }

    private void SetCustomThemeColor(string hex)
    {
        var normalized = Theme.NormalizeCustomThemeColorHex(hex);
        if (State.CustomThemeColorHex == normalized)
        {
            return;
        }

        State.CustomThemeColorHex = normalized;
        Theme.Invalidate();
        SaveNow();

        foreach (var window in _windows.Values)
        {
            window.UpdateTheme();
        }
        foreach (var m in _masterCapsules.Values) m.UpdateTheme();

        RebuildTrayMenu();
        RefreshSettingsWindowContent();
    }

    private UIElement CreateCustomThemeColorEditor()
    {
        var root = new Grid { Margin = new Thickness(0, 5, 0, 12) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var swatch = new Button
        {
            Width = 58,
            Height = 42,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(1),
            BorderBrush = TrayBorderBrush,
            Background = CustomThemeSwatchBrush(),
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = Strings.Get("SettingsCustomThemeColorPick")
        };
        swatch.Click += (_, _) => ShowCustomThemeColorDialog();
        Grid.SetColumn(swatch, 0);
        root.Children.Add(swatch);

        var detail = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0)
        };

        var current = new TextBlock
        {
            Foreground = TrayTextBrush,
            FontFamily = AppTypography.UiFontFamily,
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            Text = string.IsNullOrWhiteSpace(State.CustomThemeColorHex)
                ? Strings.Get("SettingsCustomThemeColorDefault")
                : State.CustomThemeColorHex
        };
        detail.Children.Add(current);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 7, 0, 0)
        };

        var pick = new Button
        {
            Content = Strings.Get("SettingsCustomThemeColorPick"),
            MinWidth = 76,
            Height = 27,
            Padding = new Thickness(10, 0, 10, 0),
            BorderThickness = new Thickness(0),
            Background = Theme.ActiveBrush,
            Foreground = TrayPaperBrush,
            FontFamily = AppTypography.UiFontFamily,
            FontSize = 12,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        pick.Click += (_, _) => ShowCustomThemeColorDialog();
        actions.Children.Add(pick);

        var clear = new Button
        {
            Content = Strings.Get("SettingsCustomThemeColorClear"),
            MinWidth = 82,
            Height = 27,
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(10, 0, 10, 0),
            BorderThickness = new Thickness(0),
            Background = TrayHoverBrush,
            Foreground = TrayTextBrush,
            FontFamily = AppTypography.UiFontFamily,
            FontSize = 12,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        clear.Click += (_, _) => SetCustomThemeColor("");
        actions.Children.Add(clear);

        detail.Children.Add(actions);

        Grid.SetColumn(detail, 1);
        root.Children.Add(detail);

        return root;
    }

    private void ShowCustomThemeColorDialog()
    {
        var current = Theme.TryParseHexColor(State.CustomThemeColorHex, out var color)
            ? color
            : BrushToColor(Theme.ActiveBrush);

        var owner = _settingsWindow is null ? IntPtr.Zero : new WindowInteropHelper(_settingsWindow).Handle;
        if (!TryChooseCustomThemeColor(owner, current, out var selected))
        {
            return;
        }

        SetCustomThemeColor($"#{selected.R:X2}{selected.G:X2}{selected.B:X2}");
    }

    private static bool TryChooseCustomThemeColor(IntPtr owner, Color current, out Color selected)
    {
        var customColors = new int[16];
        customColors[0] = ToColorRef(current);

        var handle = GCHandle.Alloc(customColors, GCHandleType.Pinned);
        try
        {
            var chooseColor = new ChooseColor
            {
                lStructSize = Marshal.SizeOf<ChooseColor>(),
                hwndOwner = owner,
                rgbResult = ToColorRef(current),
                lpCustColors = handle.AddrOfPinnedObject(),
                Flags = ChooseColorFlags.CC_ANYCOLOR | ChooseColorFlags.CC_FULLOPEN | ChooseColorFlags.CC_RGBINIT
            };

            if (!ChooseColorW(ref chooseColor))
            {
                selected = default;
                return false;
            }

            selected = FromColorRef(chooseColor.rgbResult);
            return true;
        }
        finally
        {
            handle.Free();
        }
    }

    private static Color BrushToColor(Brush brush)
    {
        return brush is SolidColorBrush solid
            ? solid.Color
            : Color.FromRgb(140, 115, 80);
    }

    private static Brush CustomThemeSwatchBrush()
    {
        return Theme.TryParseHexColor(AppController.Current?.State?.CustomThemeColorHex, out var color)
            ? new SolidColorBrush(color)
            : Theme.ActiveBrush;
    }

    private static int ToColorRef(Color color) => color.R | (color.G << 8) | (color.B << 16);

    private static Color FromColorRef(int colorRef) => Color.FromRgb(
        (byte)(colorRef & 0xFF),
        (byte)((colorRef >> 8) & 0xFF),
        (byte)((colorRef >> 16) & 0xFF));

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, EntryPoint = "ChooseColorW")]
    private static extern bool ChooseColorW(ref ChooseColor lpcc);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ChooseColor
    {
        public int lStructSize;
        public IntPtr hwndOwner;
        public IntPtr hInstance;
        public int rgbResult;
        public IntPtr lpCustColors;
        public ChooseColorFlags Flags;
        public IntPtr lCustData;
        public IntPtr lpfnHook;
        public IntPtr lpTemplateName;
    }

    [Flags]
    private enum ChooseColorFlags
    {
        CC_RGBINIT = 0x00000001,
        CC_FULLOPEN = 0x00000002,
        CC_ANYCOLOR = 0x00000100
    }

    private void SetUiFontPreset(string preset)
    {
        var normalized = UiFontPresets.Normalize(preset);
        if (State.UiFontPreset == normalized && string.IsNullOrWhiteSpace(State.SystemFontFamilyName))
        {
            return;
        }

        State.UiFontPreset = normalized;
        State.SystemFontFamilyName = "";
        AppTypography.Configure(State.UiFontPreset, State.SystemFontFamilyName);
        SaveNow();
        RefreshTypography();
        RefreshSettingsWindowContent();
    }

    private UIElement CreateUiFontPresetSegmentSelector()
    {
        var segments = new[]
        {
            (UiFontPresets.Default, Strings.Get("UiFontDefault")),
            (UiFontPresets.YaHei, Strings.Get("UiFontYaHei")),
            (UiFontPresets.DengXian, Strings.Get("UiFontDengXian"))
        };

        return CreateSegmentSelector(segments, UiFontPresets.Normalize(State.UiFontPreset), SetUiFontPreset);
    }

    private void SetSystemFontFamily(string? fontFamilyName)
    {
        var normalized = AppTypography.NormalizeSystemFontFamilyName(fontFamilyName);
        if (State.SystemFontFamilyName == normalized &&
            State.UiFontPreset == UiFontPresets.Default)
        {
            return;
        }

        State.SystemFontFamilyName = normalized;
        State.UiFontPreset = UiFontPresets.Default;
        AppTypography.Configure(State.UiFontPreset, State.SystemFontFamilyName);
        SaveNow();
        RefreshTypography();
        RefreshSettingsWindowContent();
    }

    private UIElement CreateSystemFontSelector()
    {
        var combo = new ComboBox
        {
            Height = 30,
            Margin = new Thickness(0, 4, 0, 10),
            Padding = new Thickness(8, 3, 8, 3),
            Background = TrayPaperBrush,
            BorderBrush = TrayBorderBrush,
            Foreground = TrayTextBrush,
            FontFamily = AppTypography.UiFontFamily,
            FontSize = 12.5,
            IsTextSearchEnabled = true,
            MaxDropDownHeight = 320,
            Style = BuildSettingsComboBoxStyle(),
            ItemContainerStyle = BuildSettingsComboBoxItemStyle()
        };

        combo.Items.Add(new ComboBoxItem
        {
            Content = Strings.Get("UiFontDefault"),
            Tag = "",
            FontFamily = AppTypography.UiFontFamily
        });

        foreach (var name in AppTypography.SystemFontFamilyNames)
        {
            combo.Items.Add(new ComboBoxItem
            {
                Content = name,
                Tag = name,
                FontFamily = new FontFamily(name)
            });
        }

        var selected = State.SystemFontFamilyName;
        foreach (ComboBoxItem item in combo.Items)
        {
            if (string.Equals(item.Tag as string ?? "", selected, StringComparison.CurrentCultureIgnoreCase))
            {
                combo.SelectedItem = item;
                break;
            }
        }

        combo.SelectedIndex = combo.SelectedIndex < 0 ? 0 : combo.SelectedIndex;

        var initialized = false;
        combo.SelectionChanged += (_, _) =>
        {
            if (!initialized || combo.SelectedItem is not ComboBoxItem item)
            {
                return;
            }

            SetSystemFontFamily(item.Tag as string);
        };
        initialized = true;

        return combo;
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

    private void SetFullscreenTopmostMode(string mode)
    {
        var normalized = FullscreenTopmostModes.Normalize(mode);
        if (State.FullscreenTopmostMode == normalized)
        {
            return;
        }

        State.FullscreenTopmostMode = normalized;
        RefreshTopmostForForegroundWindow();
        SaveNow();
        RefreshSettingsWindowContent();
    }

    private UIElement CreateFullscreenTopmostModeSegmentSelector()
    {
        var segments = new[]
        {
            (FullscreenTopmostModes.Avoid, Strings.Get("FullscreenTopmostModeAvoid")),
            (FullscreenTopmostModes.StayOnTop, Strings.Get("FullscreenTopmostModeStayOnTop"))
        };

        return CreateSegmentSelector(segments, FullscreenTopmostModes.Normalize(State.FullscreenTopmostMode), SetFullscreenTopmostMode);
    }

    private void SetTodoVisualSize(string size)
    {
        var normalized = TodoVisualSizes.Normalize(size);
        if (State.TodoVisualSize == normalized)
        {
            return;
        }

        State.TodoVisualSize = normalized;
        SaveNow();

        foreach (var window in _windows.Values)
        {
            window.UpdateTodoVisualSize();
        }

        RefreshSettingsWindowContent();
    }

    private UIElement CreateTodoVisualSizeSegmentSelector()
    {
        var segments = new[]
        {
            (TodoVisualSizes.Small, Strings.Get("TodoVisualSizeSmall")),
            (TodoVisualSizes.Medium, Strings.Get("TodoVisualSizeMedium")),
            (TodoVisualSizes.Large, Strings.Get("TodoVisualSizeLarge")),
            (TodoVisualSizes.ExtraLarge, Strings.Get("TodoVisualSizeExtraLarge"))
        };

        return CreateSegmentSelector(segments, TodoVisualSizes.Normalize(State.TodoVisualSize), SetTodoVisualSize);
    }

    private void SetTodoDueYearDisplayMode(string mode)
    {
        var normalized = TodoDueYearDisplayModes.Normalize(mode);
        if (State.TodoDueYearDisplayMode == normalized)
        {
            return;
        }

        State.TodoDueYearDisplayMode = normalized;
        SaveNow();

        foreach (var window in _windows.Values)
        {
            window.RefreshTodoRowsForExternalChange();
        }

        RefreshSettingsWindowContent();
    }

    private UIElement CreateTodoDueYearDisplayModeSelector()
    {
        var segments = new[]
        {
            (TodoDueYearDisplayModes.None, Strings.Get("TodoDueYearDisplayNone")),
            (TodoDueYearDisplayModes.Short, Strings.Get("TodoDueYearDisplayShort")),
            (TodoDueYearDisplayModes.Full, Strings.Get("TodoDueYearDisplayFull"))
        };

        return CreateSegmentSelector(segments, TodoDueYearDisplayModes.Normalize(State.TodoDueYearDisplayMode), SetTodoDueYearDisplayMode);
    }

    private UIElement CreateLineSpacingEditor(string paperType)
    {
        var isNote = paperType == PaperTypes.Note;
        var textBox = new TextBox
        {
            Text = CurrentLineSpacing(isNote).ToString("0.##", CultureInfo.InvariantCulture),
            Foreground = TrayTextBrush,
            CaretBrush = TrayTextBrush,
            Background = Brushes.Transparent,
            BorderBrush = TrayBorderBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 4, 8, 4),
            FontSize = 13,
            Height = 28,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Style = BuildSettingsTextBoxStyle()
        };

        var root = new Grid { Margin = new Thickness(0, 4, 0, 10) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(textBox, 0);
        root.Children.Add(textBox);

        void Commit()
        {
            var value = double.TryParse(textBox.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : CurrentLineSpacing(isNote);
            SetLineSpacing(isNote, value);
            textBox.Text = CurrentLineSpacing(isNote).ToString("0.##", CultureInfo.InvariantCulture);
            textBox.CaretIndex = textBox.Text.Length;
        }

        textBox.GotKeyboardFocus += (_, _) => textBox.SelectAll();
        textBox.LostKeyboardFocus += (_, _) => Commit();
        textBox.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Commit();
                Keyboard.ClearFocus();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                textBox.Text = CurrentLineSpacing(isNote).ToString("0.##", CultureInfo.InvariantCulture);
                Keyboard.ClearFocus();
                e.Handled = true;
            }
        };

        var reset = new Button
        {
            Content = Strings.Get("SettingsLineSpacingReset"),
            MinWidth = 58,
            Height = 26,
            Margin = new Thickness(8, 1, 0, 1),
            BorderThickness = new Thickness(0),
            Background = TrayHoverBrush,
            Foreground = TrayTextBrush,
            FontFamily = AppTypography.UiFontFamily,
            FontSize = 12,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        reset.Click += (_, _) =>
        {
            SetLineSpacing(isNote, 1.0);
            textBox.Text = "1";
        };
        Grid.SetColumn(reset, 1);
        root.Children.Add(reset);

        return root;
    }

    private double CurrentLineSpacing(bool isNote)
        => isNote ? NormalizeLineSpacing(State.NoteLineSpacing) : NormalizeLineSpacing(State.TodoLineSpacing);

    private void SetLineSpacing(bool isNote, double value)
    {
        var normalized = NormalizeLineSpacing(value);
        if (isNote)
        {
            if (Math.Abs(State.NoteLineSpacing - normalized) < 0.001)
            {
                return;
            }
            State.NoteLineSpacing = normalized;
        }
        else
        {
            if (Math.Abs(State.TodoLineSpacing - normalized) < 0.001)
            {
                return;
            }
            State.TodoLineSpacing = normalized;
        }

        SaveNow();
        foreach (var window in _windows.Values)
        {
            if (isNote)
            {
                window.UpdateNoteLineSpacing();
            }
            else
            {
                window.UpdateTodoVisualSize();
            }
        }
    }

    private static double NormalizeLineSpacing(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
        {
            return 1.0;
        }

        return Math.Clamp(Math.Round(value, 2), 0.8, 5.0);
    }

    private void SetTodoReminderIntervalUnit(string unit)
    {
        var normalized = TodoReminderIntervalUnits.Normalize(unit);
        if (State.TodoReminderIntervalUnit == normalized)
        {
            return;
        }

        State.TodoReminderIntervalUnit = normalized;
        _lastTodoReminderShownAt.Clear();
        SaveNow();
        RefreshSettingsWindowContent();
    }

    private UIElement CreateTodoReminderIntervalUnitSelector()
    {
        var segments = new[]
        {
            (TodoReminderIntervalUnits.Minutes, Strings.Get("TodoReminderIntervalUnitMinutes")),
            (TodoReminderIntervalUnits.Hours, Strings.Get("TodoReminderIntervalUnitHours"))
        };

        return CreateSegmentSelector(segments, TodoReminderIntervalUnits.Normalize(State.TodoReminderIntervalUnit), SetTodoReminderIntervalUnit);
    }

    private void SetTodoReminderScope(string scope)
    {
        var normalized = TodoReminderScopes.Normalize(scope);
        if (State.TodoReminderScope == normalized)
        {
            return;
        }

        State.TodoReminderScope = normalized;
        SaveNow();
        RefreshSettingsWindowContent();
    }

    private UIElement CreateTodoReminderScopeSegmentSelector()
    {
        var segments = new[]
        {
            (TodoReminderScopes.Nearest, Strings.Get("TodoReminderScopeNearest")),
            (TodoReminderScopes.All, Strings.Get("TodoReminderScopeAll"))
        };

        return CreateSegmentSelector(segments, TodoReminderScopes.Normalize(State.TodoReminderScope), SetTodoReminderScope);
    }

    private UIElement CreateTodoReminderIntervalStepper()
    {
        var textBox = new TextBox
        {
            Text = State.TodoReminderIntervalValue.ToString(CultureInfo.InvariantCulture),
            Foreground = TrayTextBrush,
            CaretBrush = TrayTextBrush,
            Background = Brushes.Transparent,
            BorderBrush = TrayBorderBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 4, 0, 10),
            FontSize = 13,
            Height = 28,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Style = BuildSettingsTextBoxStyle()
        };

        void Commit()
        {
            var value = int.TryParse(textBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : State.TodoReminderIntervalValue;
            SetTodoReminderIntervalValue(value);
            textBox.Text = State.TodoReminderIntervalValue.ToString(CultureInfo.InvariantCulture);
            textBox.CaretIndex = textBox.Text.Length;
        }

        textBox.GotKeyboardFocus += (_, _) => textBox.SelectAll();
        textBox.LostKeyboardFocus += (_, _) => Commit();
        textBox.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Commit();
                Keyboard.ClearFocus();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                textBox.Text = State.TodoReminderIntervalValue.ToString(CultureInfo.InvariantCulture);
                Keyboard.ClearFocus();
                e.Handled = true;
            }
        };

        return textBox;
    }

    private void SetTodoReminderIntervalValue(int value)
    {
        var normalized = Math.Clamp(value <= 0 ? 1 : value, 1, 240);
        if (State.TodoReminderIntervalValue == normalized)
        {
            return;
        }

        State.TodoReminderIntervalValue = normalized;
        _lastTodoReminderShownAt.Clear();
        SaveNow();
    }

    private UIElement CreateTodoReminderBubbleDurationEditor()
    {
        var textBox = new TextBox
        {
            Text = State.TodoReminderBubbleDurationSeconds.ToString(CultureInfo.InvariantCulture),
            Foreground = TrayTextBrush,
            CaretBrush = TrayTextBrush,
            Background = Brushes.Transparent,
            BorderBrush = TrayBorderBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 4, 0, 10),
            FontSize = 13,
            Height = 28,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Style = BuildSettingsTextBoxStyle()
        };

        void Commit()
        {
            var value = int.TryParse(textBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : State.TodoReminderBubbleDurationSeconds;
            SetTodoReminderBubbleDurationSeconds(value);
            textBox.Text = State.TodoReminderBubbleDurationSeconds.ToString(CultureInfo.InvariantCulture);
            textBox.CaretIndex = textBox.Text.Length;
        }

        textBox.GotKeyboardFocus += (_, _) => textBox.SelectAll();
        textBox.LostKeyboardFocus += (_, _) => Commit();
        textBox.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Commit();
                Keyboard.ClearFocus();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                textBox.Text = State.TodoReminderBubbleDurationSeconds.ToString(CultureInfo.InvariantCulture);
                Keyboard.ClearFocus();
                e.Handled = true;
            }
        };

        return textBox;
    }

    private void SetTodoReminderBubbleDurationSeconds(int value)
    {
        var normalized = Math.Clamp(value <= 0 ? 5 : value, 1, 600);
        if (State.TodoReminderBubbleDurationSeconds == normalized)
        {
            return;
        }

        State.TodoReminderBubbleDurationSeconds = normalized;
        SaveNow();
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

    private UIElement CreatePinnedPaperHotKeyEditor(string paperType)
    {
        var root = new Grid { Margin = new Thickness(0, 4, 0, 10) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var textBox = new TextBox
        {
            Text = paperType == PaperTypes.Note ? State.PinnedNoteHotKey : State.PinnedTodoHotKey,
            IsReadOnly = true,
            Foreground = TrayTextBrush,
            CaretBrush = Brushes.Transparent,
            Background = Brushes.Transparent,
            BorderBrush = TrayBorderBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 4, 8, 4),
            FontSize = 13,
            Height = 28,
            VerticalContentAlignment = VerticalAlignment.Center,
            Style = BuildSettingsTextBoxStyle()
        };
        textBox.GotKeyboardFocus += (_, _) => textBox.SelectAll();
        textBox.PreviewKeyDown += (_, e) =>
        {
            e.Handled = true;
            if (e.Key is Key.Escape or Key.Back or Key.Delete)
            {
                SetPinnedPaperHotKey(paperType, "");
                return;
            }

            var hotKey = HotKeyTextFromKeyEvent(e);
            if (string.IsNullOrWhiteSpace(hotKey))
            {
                return;
            }

            SetPinnedPaperHotKey(paperType, hotKey);
        };

        Grid.SetColumn(textBox, 0);
        root.Children.Add(textBox);

        var clear = new Button
        {
            Content = Strings.Get("SettingsHotKeyClear"),
            MinWidth = 52,
            Height = 26,
            Margin = new Thickness(8, 1, 0, 1),
            BorderThickness = new Thickness(0),
            Background = TrayHoverBrush,
            Foreground = TrayTextBrush,
            FontFamily = AppTypography.UiFontFamily,
            FontSize = 12,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        clear.Click += (_, _) => SetPinnedPaperHotKey(paperType, "");
        Grid.SetColumn(clear, 1);
        root.Children.Add(clear);

        return root;
    }

    private static string HotKeyTextFromKeyEvent(KeyEventArgs e)
    {
        var key = e.Key == Key.System
            ? e.SystemKey
            : e.Key == Key.ImeProcessed
                ? e.ImeProcessedKey
                : e.Key;

        if (key is Key.None or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or
            Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            return "";
        }

        var modifiers = Keyboard.Modifiers;
        if (modifiers == ModifierKeys.None)
        {
            return "";
        }

        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(new KeyConverter().ConvertToInvariantString(key) ?? key.ToString());
        return string.Join("+", parts);
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
            CornerRadius = AppUi.ControlRadius,
            Background = Brushes.Transparent,
            Margin = new Thickness(0, 4, 0, 10),
            Height = 28,
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
                CornerRadius = AppUi.ControlRadius,
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

    private UIElement CreateMaxTitleLengthStepper()
    {
        var container = new Border
        {
            BorderBrush = TrayBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = AppUi.ControlRadius,
            Background = Brushes.Transparent,
            Margin = new Thickness(0, 4, 0, 10),
            Height = 28,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var valueText = new TextBlock
        {
            Text = State.MaxTitleLength.ToString(CultureInfo.InvariantCulture),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = TrayTextBrush
        };
        Grid.SetColumn(valueText, 1);

        Border StepButton(string glyph, int column, Action onClick)
        {
            var glyphText = new TextBlock
            {
                Text = glyph,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontFamily = AppTypography.SymbolFontFamily,
                FontSize = 15,
                Foreground = TrayTextBrush
            };
            var button = new Border
            {
                Width = 34,
                Background = Brushes.Transparent,
                Cursor = System.Windows.Input.Cursors.Hand,
                Child = glyphText
            };
            button.MouseEnter += (_, _) => button.Background = TrayHoverBrush;
            button.MouseLeave += (_, _) => button.Background = Brushes.Transparent;
            button.MouseLeftButtonDown += (_, e) =>
            {
                onClick();
                valueText.Text = State.MaxTitleLength.ToString(CultureInfo.InvariantCulture);
                e.Handled = true;
            };
            Grid.SetColumn(button, column);
            return button;
        }

        grid.Children.Add(StepButton("−", 0, () => SetMaxTitleLength(State.MaxTitleLength - 1)));
        grid.Children.Add(valueText);
        grid.Children.Add(StepButton("＋", 2, () => SetMaxTitleLength(State.MaxTitleLength + 1)));

        container.Child = grid;
        return container;
    }

    private void SetMaxTitleLength(int value)
    {
        var normalized = PaperTitles.NormalizeMaxTitleLength(value);
        if (State.MaxTitleLength == normalized)
        {
            return;
        }

        State.MaxTitleLength = normalized;

        // Re-clamp existing custom titles to the new limit and refresh everything that shows them.
        foreach (var paper in State.Papers)
        {
            paper.Title = PaperTitles.CleanCustomTitle(paper.Title, normalized);
        }

        foreach (var window in _windows.Values)
        {
            window.RefreshPaperTitle();
        }

        ArrangeDeepCapsules(animate: true);
        SaveNow();
        RebuildTrayMenu();
        RefreshSettingsWindowContent();
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
            Width = SettingsWindowWidth(),
            Height = SettingsWindowDefaultHeight(),
            MinWidth = 560,
            MinHeight = 360,
            SizeToContent = SizeToContent.Manual,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.CanResizeWithGrip,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            Topmost = false,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            FontFamily = AppTypography.UiFontFamily,
            Language = AppTypography.Language,
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
            _settingsCapsuleModeCheckBox = null;
            _settingsDeepCapsuleModeCheckBox = null;
            _settingsDeepCapsuleExpandedSlotCheckBox = null;
            _settingsCollapseExpandedDeepCapsuleOnClickCheckBox = null;
            _settingsCapsuleCollapseAllCheckBox = null;
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
        _settingsWindow.FontFamily = AppTypography.UiFontFamily;
        _settingsWindow.Language = AppTypography.Language;
        ApplyToolTipSetting(_settingsWindow);
    }

    private void RefreshTypography()
    {
        RebuildTrayMenu();

        foreach (var window in _windows.Values)
        {
            window.UpdateTypography();
        }

        foreach (var masterCapsule in _masterCapsules.Values)
        {
            masterCapsule.UpdateTypography();
        }
    }

    private UIElement BuildSettingsWindowContent(Window window)
    {
        var root = new DockPanel
        {
            LastChildFill = true
        };

        var titleRow = new Grid
        {
            Margin = new Thickness(0, 0, 0, 12),
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
            FontFamily = AppTypography.SymbolFontFamily,
            FontSize = 16,
            Cursor = System.Windows.Input.Cursors.Hand,
            Focusable = false,
            Style = BuildSettingsCloseButtonStyle()
        };
        closeButton.Click += (_, _) => window.Close();
        Grid.SetColumn(closeButton, 1);
        titleRow.Children.Add(closeButton);

        DockPanel.SetDock(titleRow, Dock.Top);
        root.Children.Add(titleRow);

        var displayPanel = SettingsTabPanel();
        var todoNotePanel = SettingsTabPanel();
        var capsulePanel = SettingsTabPanel();
        var generalPanel = SettingsTabPanel();

        displayPanel.Children.Add(SettingsSectionLabel(Strings.Get("SettingsDisplay")));
        displayPanel.Children.Add(WrapWithHint(SettingsFieldLabel(Strings.Get("TrayThemeMode")), "TipThemeMode"));
        displayPanel.Children.Add(CreateThemeSegmentSelector());
        displayPanel.Children.Add(WrapWithHint(SettingsFieldLabel(Strings.Get("SettingsCustomThemeColor")), "TipCustomThemeColor"));
        displayPanel.Children.Add(CreateCustomThemeColorEditor());
        displayPanel.Children.Add(WrapWithHint(SettingsFieldLabel(Strings.Get("SettingsSystemFont")), "TipSystemFont"));
        displayPanel.Children.Add(CreateSystemFontSelector());
        displayPanel.Children.Add(WrapWithHint(SettingsFieldLabel(Strings.Get("TrayMarkdownRenderMode")), "TipMarkdownRender"));
        displayPanel.Children.Add(CreateMarkdownRenderSegmentSelector());
        displayPanel.Children.Add(WrapWithHint(SettingsFieldLabel(Strings.Get("SettingsFullscreenTopmostMode")), "TipFullscreenTopmostMode"));
        displayPanel.Children.Add(CreateFullscreenTopmostModeSegmentSelector());
        displayPanel.Children.Add(WrapWithHint(SettingsFieldLabel(Strings.Get("SettingsTodoVisualSize")), "TipTodoVisualSize"));
        displayPanel.Children.Add(CreateTodoVisualSizeSegmentSelector());
        displayPanel.Children.Add(WrapWithHint(SettingsFieldLabel(Strings.Get("SettingsTodoLineSpacing")), "TipTodoLineSpacing"));
        displayPanel.Children.Add(CreateLineSpacingEditor(PaperTypes.Todo));
        displayPanel.Children.Add(WrapWithHint(SettingsFieldLabel(Strings.Get("SettingsNoteLineSpacing")), "TipNoteLineSpacing"));
        displayPanel.Children.Add(CreateLineSpacingEditor(PaperTypes.Note));

        displayPanel.Children.Add(SettingsSectionLabel(Strings.Get("SettingsTopBarButtons")));
        displayPanel.Children.Add(WrapWithHint(SettingsToggle(Strings.Get("SettingsShowTopBarNewTodoButton"), State.ShowTopBarNewTodoButton, ToggleTopBarNewTodoButton), "TipNewTodoButton"));
        displayPanel.Children.Add(WrapWithHint(SettingsToggle(Strings.Get("SettingsShowTopBarNewNoteButton"), State.ShowTopBarNewNoteButton, ToggleTopBarNewNoteButton), "TipNewNoteButton"));
        displayPanel.Children.Add(WrapWithHint(SettingsToggle(Strings.Get("SettingsShowTopBarExternalOpenButton"), State.ShowTopBarExternalOpenButton, ToggleTopBarExternalOpenButton), "TipExternalOpenButton"));

        generalPanel.Children.Add(SettingsSectionLabel(Strings.Get("SettingsGeneral")));
        generalPanel.Children.Add(WrapWithHint(SettingsToggle(Strings.Get("TrayStartup"), SystemSettingsHelper.IsStartupEnabled(), ToggleStartup), "TipStartup"));
        generalPanel.Children.Add(WrapWithHint(SettingsToggle(Strings.Get("SettingsHidePapersFromWindowSwitcher"), State.HidePapersFromWindowSwitcher, ToggleHidePapersFromWindowSwitcher), "TipHidePapersFromWindowSwitcher"));
        generalPanel.Children.Add(WrapWithHint(SettingsToggle(Strings.Get("SettingsEnableToolTips"), State.EnableToolTips, ToggleToolTips), "TipEnableToolTips"));
        generalPanel.Children.Add(WrapWithHint(SettingsToggle(Strings.Get("SettingsEnableAnimations"), State.EnableAnimations, ToggleAnimations), "TipEnableAnimations"));
        generalPanel.Children.Add(WrapWithHint(SettingsFieldLabel(Strings.Get("SettingsPinnedTodoHotKey"), topMargin: 8), "TipPinnedTodoHotKey"));
        generalPanel.Children.Add(CreatePinnedPaperHotKeyEditor(PaperTypes.Todo));
        generalPanel.Children.Add(WrapWithHint(SettingsFieldLabel(Strings.Get("SettingsPinnedNoteHotKey")), "TipPinnedNoteHotKey"));
        generalPanel.Children.Add(CreatePinnedPaperHotKeyEditor(PaperTypes.Note));

        todoNotePanel.Children.Add(SettingsSectionLabel(Strings.Get("SettingsTodoNote")));
        todoNotePanel.Children.Add(WrapWithHint(SettingsToggle(Strings.Get("SettingsEnableTodoNoteLinks"), State.EnableTodoNoteLinks, ToggleTodoNoteLinks), "TipEnableTodoNoteLinks"));
        todoNotePanel.Children.Add(WrapWithHint(SettingsToggle(Strings.Get("SettingsShowTodoDueRelativeTime"), State.ShowTodoDueRelativeTime, ToggleTodoDueRelativeTime), "TipShowTodoDueRelativeTime"));
        todoNotePanel.Children.Add(WrapWithHint(SettingsFieldLabel(Strings.Get("SettingsTodoDueYearDisplay")), "TipTodoDueYearDisplay"));
        todoNotePanel.Children.Add(CreateTodoDueYearDisplayModeSelector());
        todoNotePanel.Children.Add(WrapWithHint(SettingsToggle(Strings.Get("SettingsUseTodoReminderInterval"), State.UseTodoReminderInterval, ToggleTodoReminderInterval), "TipUseTodoReminderInterval"));
        todoNotePanel.Children.Add(WrapWithHint(SettingsFieldLabel(Strings.Get("SettingsTodoReminderInterval"), topMargin: 6), "TipTodoReminderInterval"));
        todoNotePanel.Children.Add(CreateTodoReminderIntervalStepper());
        todoNotePanel.Children.Add(WrapWithHint(SettingsFieldLabel(Strings.Get("SettingsTodoReminderIntervalUnit")), "TipTodoReminderIntervalUnit"));
        todoNotePanel.Children.Add(CreateTodoReminderIntervalUnitSelector());
        todoNotePanel.Children.Add(WrapWithHint(SettingsFieldLabel(Strings.Get("SettingsTodoReminderScope")), "TipTodoReminderScope"));
        todoNotePanel.Children.Add(CreateTodoReminderScopeSegmentSelector());
        todoNotePanel.Children.Add(WrapWithHint(SettingsFieldLabel(Strings.Get("SettingsTodoReminderBubbleDuration"), topMargin: 6), "TipTodoReminderBubbleDuration"));
        todoNotePanel.Children.Add(CreateTodoReminderBubbleDurationEditor());
        var showLinkedNoteNameToggle = SettingsToggle(Strings.Get("SettingsShowLinkedNoteName"), State.ShowLinkedNoteName, ToggleLinkedNoteNameDisplay);
        showLinkedNoteNameToggle.IsEnabled = State.EnableTodoNoteLinks;
        todoNotePanel.Children.Add(WrapWithHint(showLinkedNoteNameToggle, "TipShowLinkedNoteName"));
        var allowLongLinkedNoteTitlesToggle = SettingsToggle(Strings.Get("SettingsAllowLongLinkedNoteTitles"), State.AllowLongLinkedNoteTitles, ToggleLongLinkedNoteTitles);
        allowLongLinkedNoteTitlesToggle.IsEnabled = State.EnableTodoNoteLinks && State.ShowLinkedNoteName;
        todoNotePanel.Children.Add(WrapWithHint(allowLongLinkedNoteTitlesToggle, "TipAllowLongLinkedNoteTitles"));
        var hideLinkedNotesFromCapsulesToggle = SettingsToggle(Strings.Get("SettingsHideLinkedNotesFromCapsules"), State.HideLinkedNotesFromCapsules, ToggleHideLinkedNotesFromCapsules);
        hideLinkedNotesFromCapsulesToggle.IsEnabled = State.EnableTodoNoteLinks;
        todoNotePanel.Children.Add(WrapWithHint(hideLinkedNotesFromCapsulesToggle, "TipHideLinkedNotesFromCapsules"));
        var runLinkedScriptCapsulesToggle = SettingsToggle(Strings.Get("SettingsRunLinkedScriptCapsulesOnClick"), State.RunLinkedScriptCapsulesOnClick, ToggleRunLinkedScriptCapsulesOnClick);
        runLinkedScriptCapsulesToggle.IsEnabled = State.EnableTodoNoteLinks;
        todoNotePanel.Children.Add(WrapWithHint(runLinkedScriptCapsulesToggle, "TipRunLinkedScriptCapsulesOnClick"));

        generalPanel.Children.Add(SettingsSectionLabel(Strings.Get("SettingsExternalOpen")));
        generalPanel.Children.Add(WrapWithHint(SettingsFieldLabel(Strings.Get("SettingsExternalMarkdownExtension")), "TipExternalExtension"));
        generalPanel.Children.Add(CreateExternalMarkdownExtensionEditor());

        generalPanel.Children.Add(SettingsSectionLabel(Strings.Get("SettingsScriptCapsule")));
        generalPanel.Children.Add(WrapWithHint(SettingsToggle(Strings.Get("SettingsPersistentPowerShellProcess"), State.UsePersistentPowerShellProcess, TogglePersistentPowerShellProcess), "TipPersistentPowerShellProcess"));
        generalPanel.Children.Add(WrapWithHint(SettingsToggle(Strings.Get("SettingsPreferPowerShell7"), State.PreferPowerShell7, TogglePreferPowerShell7), "TipPreferPowerShell7"));
        generalPanel.Children.Add(WrapWithHint(SettingsToggle(Strings.Get("SettingsHideScriptRunWindow"), State.HideScriptRunWindow, ToggleHideScriptRunWindow), "TipHideScriptRunWindow"));

        capsulePanel.Children.Add(SettingsSectionLabel(Strings.Get("SettingsCapsule")));
        _settingsCapsuleModeCheckBox = SettingsToggle(Strings.Get("TrayCapsuleMode"), State.UseCapsuleMode, ToggleCapsuleMode);
        _settingsDeepCapsuleModeCheckBox = SettingsToggle(Strings.Get("TrayDeepCapsuleMode"), State.UseDeepCapsuleMode, ToggleDeepCapsuleMode);
        _settingsDeepCapsuleExpandedSlotCheckBox = SettingsToggle(Strings.Get("SettingsShowDeepCapsuleWhileExpanded"), State.ShowDeepCapsuleWhileExpanded, ToggleDeepCapsuleExpandedSlot);
        _settingsCollapseExpandedDeepCapsuleOnClickCheckBox = SettingsToggle(Strings.Get("SettingsCollapseExpandedDeepCapsuleOnClick"), State.CollapseExpandedDeepCapsuleOnClick, ToggleCollapseExpandedDeepCapsuleOnClick);
        _settingsHideDeepCapsulesWhenCoveredCheckBox = SettingsToggle(Strings.Get("SettingsHideDeepCapsulesWhenCovered"), State.HideDeepCapsulesWhenCovered, ToggleHideDeepCapsulesWhenCovered);
        _settingsCapsuleCollapseAllCheckBox = SettingsToggle(Strings.Get("SettingsCapsuleCollapseAll"), State.UseCapsuleCollapseAll, ToggleCapsuleCollapseAll);
        capsulePanel.Children.Add(WrapWithHint(_settingsCapsuleModeCheckBox, "TipCapsuleMode"));
        capsulePanel.Children.Add(WrapWithHint(_settingsDeepCapsuleModeCheckBox, "TipDeepCapsuleMode"));
        capsulePanel.Children.Add(WrapWithHint(_settingsDeepCapsuleExpandedSlotCheckBox, "TipShowDeepCapsuleWhileExpanded"));
        capsulePanel.Children.Add(WrapWithHint(_settingsCollapseExpandedDeepCapsuleOnClickCheckBox, "TipCollapseExpandedDeepCapsuleOnClick"));
        capsulePanel.Children.Add(WrapWithHint(_settingsHideDeepCapsulesWhenCoveredCheckBox, "TipHideDeepCapsulesWhenCovered"));
        capsulePanel.Children.Add(WrapWithHint(_settingsCapsuleCollapseAllCheckBox, "TipCapsuleCollapseAll"));
        RefreshSettingsCapsuleToggleStates();
        capsulePanel.Children.Add(WrapWithHint(SettingsFieldLabel(Strings.Get("SettingsMaxTitleLength"), topMargin: 8), "TipMaxTitleLength"));
        capsulePanel.Children.Add(CreateMaxTitleLengthStepper());

        var pages = new (string Header, UIElement Content)[]
        {
            (Strings.Get("SettingsTabDisplay"), displayPanel),
            (Strings.Get("SettingsTabTodoNote"), todoNotePanel),
            (Strings.Get("SettingsTabCapsule"), capsulePanel),
            (Strings.Get("SettingsTabGeneral"), generalPanel)
        };
        _settingsSelectedTabIndex = Math.Clamp(_settingsSelectedTabIndex, 0, pages.Length - 1);

        var navigationLayout = new Grid
        {
            Margin = new Thickness(0, 0, 4, 0)
        };
        navigationLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(146) });
        navigationLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        navigationLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var nav = new StackPanel
        {
            Margin = new Thickness(0, 8, 12, 0)
        };
        for (var i = 0; i < pages.Length; i++)
        {
            nav.Children.Add(SettingsNavigationItem(pages[i].Header, i, i == _settingsSelectedTabIndex));
        }
        Grid.SetColumn(nav, 0);
        navigationLayout.Children.Add(nav);

        var separator = new Border
        {
            Width = 1,
            Margin = new Thickness(0, 8, 14, 4),
            Background = TrayBorderBrush,
            Opacity = 0.55
        };
        Grid.SetColumn(separator, 1);
        navigationLayout.Children.Add(separator);

        var contentScroll = new ScrollViewer
        {
            Content = pages[_settingsSelectedTabIndex].Content,
            Margin = new Thickness(0, 6, 0, 0),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            CanContentScroll = false,
            PanningMode = PanningMode.Both
        };
        Grid.SetColumn(contentScroll, 2);
        navigationLayout.Children.Add(contentScroll);

        var footer = BuildSettingsFooter();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        root.Children.Add(navigationLayout);

        return new Border
        {
            Background = TrayPaperBrush,
            BorderBrush = TrayBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = AppUi.ShellRadius,
            Padding = AppUi.SettingsPadding,
            Child = root,
            Effect = AppUi.SettingsShadow()
        };
    }

    private UIElement BuildSettingsFooter()
    {
        var signatureText = new TextBlock
        {
            Text = AuthorName,
            Foreground = TrayWeakTextBrush,
            FontSize = 11,
            FontWeight = FontWeights.Medium,
            VerticalAlignment = VerticalAlignment.Center
        };

        var signature = new Border
        {
            Background = Brushes.Transparent,
            Cursor = System.Windows.Input.Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 2, 0),
            Padding = new Thickness(6, 2, 0, 0),
            Child = signatureText,
            ToolTip = AuthorGithubUrl
        };
        ToolTipService.SetInitialShowDelay(signature, 300);
        ToolTipService.SetShowDuration(signature, 12000);
        signature.MouseEnter += (_, _) => signatureText.Foreground = TrayTextBrush;
        signature.MouseLeave += (_, _) => signatureText.Foreground = TrayWeakTextBrush;
        signature.MouseLeftButtonUp += (_, e) =>
        {
            OpenAuthorGithub();
            e.Handled = true;
        };

        return signature;
    }

    private static StackPanel SettingsTabPanel()
    {
        return new StackPanel
        {
            Margin = new Thickness(2, 2, 8, 2),
            MinWidth = 560
        };
    }

    private Border SettingsNavigationItem(string text, int index, bool isSelected)
    {
        var label = new TextBlock
        {
            Text = text,
            Foreground = isSelected ? TrayTextBrush : TrayWeakTextBrush,
            FontFamily = AppTypography.UiFontFamily,
            FontSize = 12.5,
            FontWeight = isSelected ? FontWeights.SemiBold : FontWeights.Medium,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };

        var marker = new Border
        {
            Width = 3,
            Height = 18,
            CornerRadius = new CornerRadius(2),
            Background = isSelected ? Theme.ActiveBrush : Brushes.Transparent,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };

        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(marker, 0);
        Grid.SetColumn(label, 1);
        content.Children.Add(marker);
        content.Children.Add(label);

        var item = new Border
        {
            Height = 34,
            Margin = new Thickness(0, 0, 0, 6),
            Padding = new Thickness(9, 0, 9, 0),
            CornerRadius = AppUi.ControlRadius,
            Background = isSelected ? Theme.Tint((byte)(Theme.IsDark ? 42 : 24)) : Brushes.Transparent,
            Cursor = System.Windows.Input.Cursors.Hand,
            Child = content
        };

        if (!isSelected)
        {
            item.MouseEnter += (_, _) => item.Background = TrayHoverBrush;
            item.MouseLeave += (_, _) => item.Background = Brushes.Transparent;
        }

        item.MouseLeftButtonDown += (_, e) =>
        {
            if (_settingsSelectedTabIndex != index)
            {
                _settingsSelectedTabIndex = index;
                RefreshSettingsWindowContent();
            }
            e.Handled = true;
        };

        return item;
    }

    private static void OpenAuthorGithub()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = AuthorGithubUrl,
                UseShellExecute = true
            });
        }
        catch
        {
            // Opening an external browser should not affect settings interaction.
        }
    }

    private static double SettingsWindowWidth()
    {
        return SettingsContentWidth() + 32;
    }

    private static double SettingsWindowDefaultHeight()
    {
        return Math.Clamp(SystemParameters.WorkArea.Height * 0.72, 520, 720);
    }

    private static double SettingsContentWidth()
    {
        return Math.Clamp(SystemParameters.WorkArea.Width - 96, 640, 760);
    }

    private static double SettingsWindowMaxHeight()
    {
        return Math.Max(260, SystemParameters.WorkArea.Height - 48);
    }

    private static double SettingsOptionsMaxHeight()
    {
        const double verticalPadding = 26;
        const double titleRowHeight = 34;
        const double footerHeight = 24;
        return Math.Max(180, SettingsWindowMaxHeight() - verticalPadding - titleRowHeight - footerHeight);
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

    private static TextBlock SettingsFieldLabel(string text, double topMargin = 0)
    {
        return new TextBlock
        {
            Text = text,
            Foreground = TrayWeakTextBrush,
            FontSize = 11,
            FontWeight = FontWeights.Medium,
            Margin = new Thickness(0, topMargin, 0, 0)
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

    // Lays the option out as: [option .....stretch.....] [ⓘ]. The trailing ⓘ shows a themed
    // tooltip with the detailed explanation on hover, so every row stays short while the full
    // description is one hover away. tipKey is a Strings resource key.
    private UIElement WrapWithHint(FrameworkElement option, string tipKey)
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // The option keeps its own top margin via its style; reset it here so the row controls spacing.
        option.Margin = new Thickness(0);
        option.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(option, 0);
        grid.Children.Add(option);

        var hintGlyph = new TextBlock
        {
            Text = "ⓘ",
            Foreground = TrayWeakTextBrush,
            FontFamily = AppTypography.SymbolFontFamily,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var hint = new Border
        {
            Width = 18,
            Height = 18,
            Margin = new Thickness(6, 0, 0, 0),
            Background = Brushes.Transparent,
            Cursor = System.Windows.Input.Cursors.Help,
            Child = hintGlyph,
            ToolTip = BuildSettingsHintTooltip(Strings.Get(tipKey))
        };
        ToolTipPreferences.SetAlwaysEnabled(hint, true);
        ToolTipService.SetInitialShowDelay(hint, 200);
        ToolTipService.SetShowDuration(hint, 20000);
        ToolTipService.SetBetweenShowDelay(hint, 0);
        hint.MouseEnter += (_, _) => hintGlyph.Foreground = TrayTextBrush;
        hint.MouseLeave += (_, _) => hintGlyph.Foreground = TrayWeakTextBrush;
        Grid.SetColumn(hint, 1);
        grid.Children.Add(hint);

        return grid;
    }

    private ToolTip BuildSettingsHintTooltip(string text)
    {
        return new ToolTip
        {
            Content = new TextBlock
            {
                Text = text,
                Foreground = TrayTextBrush,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 240
            },
            Background = TrayPaperBrush,
            BorderBrush = TrayBorderBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 7, 10, 7),
            HasDropShadow = true
        };
    }

    private void RefreshSettingsCapsuleToggleStates()
    {
        if (_settingsCapsuleModeCheckBox != null)
        {
            _settingsCapsuleModeCheckBox.IsChecked = State.UseCapsuleMode;
        }
        if (_settingsDeepCapsuleModeCheckBox != null)
        {
            _settingsDeepCapsuleModeCheckBox.IsChecked = State.UseDeepCapsuleMode;
            _settingsDeepCapsuleModeCheckBox.IsEnabled = State.UseCapsuleMode;
        }
        if (_settingsDeepCapsuleExpandedSlotCheckBox != null)
        {
            _settingsDeepCapsuleExpandedSlotCheckBox.IsChecked = State.ShowDeepCapsuleWhileExpanded;
            _settingsDeepCapsuleExpandedSlotCheckBox.IsEnabled = State.UseCapsuleMode && State.UseDeepCapsuleMode;
        }
        if (_settingsCollapseExpandedDeepCapsuleOnClickCheckBox != null)
        {
            _settingsCollapseExpandedDeepCapsuleOnClickCheckBox.IsChecked = State.CollapseExpandedDeepCapsuleOnClick;
            _settingsCollapseExpandedDeepCapsuleOnClickCheckBox.IsEnabled = State.UseCapsuleMode && State.UseDeepCapsuleMode &&
                State.ShowDeepCapsuleWhileExpanded;
        }
        if (_settingsHideDeepCapsulesWhenCoveredCheckBox != null)
        {
            _settingsHideDeepCapsulesWhenCoveredCheckBox.IsChecked = State.HideDeepCapsulesWhenCovered;
            _settingsHideDeepCapsulesWhenCoveredCheckBox.IsEnabled = State.UseCapsuleMode && State.UseDeepCapsuleMode;
        }
        if (_settingsCapsuleCollapseAllCheckBox != null)
        {
            _settingsCapsuleCollapseAllCheckBox.IsChecked = State.UseCapsuleCollapseAll;
            _settingsCapsuleCollapseAllCheckBox.IsEnabled = State.UseCapsuleMode && State.UseDeepCapsuleMode;
        }
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
        border.SetValue(Border.CornerRadiusProperty, AppUi.ControlRadius);
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

    private Style BuildSettingsComboBoxStyle()
    {
        var style = new Style(typeof(ComboBox));
        style.Setters.Add(new Setter(Control.ForegroundProperty, TrayTextBrush));
        style.Setters.Add(new Setter(Control.BackgroundProperty, TrayPaperBrush));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, TrayBorderBrush));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 4, 8, 4)));
        style.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
        style.Setters.Add(new Setter(UIElement.SnapsToDevicePixelsProperty, true));
        style.Setters.Add(new Setter(FrameworkElement.UseLayoutRoundingProperty, true));

        var root = new FrameworkElementFactory(typeof(Grid));

        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "Bd";
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
        border.SetValue(Border.CornerRadiusProperty, AppUi.ControlRadius);
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        border.SetValue(UIElement.SnapsToDevicePixelsProperty, true);

        var contentDock = new FrameworkElementFactory(typeof(DockPanel));
        contentDock.SetValue(DockPanel.LastChildFillProperty, true);
        contentDock.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);

        var selected = new FrameworkElementFactory(typeof(ContentPresenter));
        selected.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ComboBox.SelectionBoxItemProperty));
        selected.SetValue(ContentPresenter.ContentTemplateProperty, new TemplateBindingExtension(ComboBox.SelectionBoxItemTemplateProperty));
        selected.SetValue(ContentPresenter.ContentStringFormatProperty, new TemplateBindingExtension(ComboBox.SelectionBoxItemStringFormatProperty));
        selected.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        selected.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        selected.SetValue(UIElement.IsHitTestVisibleProperty, false);

        var arrow = new FrameworkElementFactory(typeof(System.Windows.Shapes.Path));
        arrow.Name = "Arrow";
        arrow.SetValue(System.Windows.Shapes.Path.DataProperty, Geometry.Parse("M 0 0 L 5 5 L 10 0 Z"));
        arrow.SetValue(System.Windows.Shapes.Path.FillProperty, TrayWeakTextBrush);
        arrow.SetValue(FrameworkElement.WidthProperty, 10.0);
        arrow.SetValue(FrameworkElement.HeightProperty, 5.0);
        arrow.SetValue(FrameworkElement.MarginProperty, new Thickness(10, 0, 1, 0));
        arrow.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        arrow.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right);
        arrow.SetValue(DockPanel.DockProperty, Dock.Right);

        contentDock.AppendChild(arrow);
        contentDock.AppendChild(selected);
        border.AppendChild(contentDock);
        root.AppendChild(border);

        var toggle = new FrameworkElementFactory(typeof(ToggleButton), "DropDownToggle");
        toggle.SetValue(Control.BackgroundProperty, Brushes.Transparent);
        toggle.SetValue(Control.BorderThicknessProperty, new Thickness(0));
        toggle.SetValue(UIElement.FocusableProperty, false);
        toggle.SetValue(ButtonBase.ClickModeProperty, ClickMode.Press);
        toggle.SetBinding(ToggleButton.IsCheckedProperty, new Binding(nameof(ComboBox.IsDropDownOpen))
        {
            RelativeSource = RelativeSource.TemplatedParent,
            Mode = BindingMode.TwoWay
        });

        var toggleChrome = new FrameworkElementFactory(typeof(Border));
        toggleChrome.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        var toggleTemplate = new ControlTemplate(typeof(ToggleButton))
        {
            VisualTree = toggleChrome
        };
        toggle.SetValue(Control.TemplateProperty, toggleTemplate);
        root.AppendChild(toggle);

        var popup = new FrameworkElementFactory(typeof(Popup), "PART_Popup");
        popup.SetValue(Popup.AllowsTransparencyProperty, true);
        popup.SetValue(Popup.FocusableProperty, false);
        popup.SetValue(Popup.PlacementProperty, PlacementMode.Bottom);
        popup.SetBinding(Popup.PlacementTargetProperty, new Binding
        {
            RelativeSource = RelativeSource.TemplatedParent
        });
        popup.SetBinding(Popup.IsOpenProperty, new Binding(nameof(ComboBox.IsDropDownOpen))
        {
            RelativeSource = RelativeSource.TemplatedParent,
            Mode = BindingMode.TwoWay
        });

        var popupBorder = new FrameworkElementFactory(typeof(Border));
        popupBorder.SetValue(Border.BackgroundProperty, TrayPaperBrush);
        popupBorder.SetValue(Border.BorderBrushProperty, TrayBorderBrush);
        popupBorder.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        popupBorder.SetValue(Border.CornerRadiusProperty, AppUi.BlockRadius);
        popupBorder.SetValue(Border.PaddingProperty, new Thickness(4));
        popupBorder.SetValue(Border.EffectProperty, AppUi.FloatingShadow());
        popupBorder.SetBinding(FrameworkElement.MinWidthProperty, new Binding("ActualWidth")
        {
            RelativeSource = RelativeSource.TemplatedParent
        });

        var scroll = new FrameworkElementFactory(typeof(ScrollViewer));
        scroll.SetValue(ScrollViewer.CanContentScrollProperty, true);
        scroll.SetValue(FrameworkElement.MaxHeightProperty, new TemplateBindingExtension(ComboBox.MaxDropDownHeightProperty));
        scroll.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        scroll.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);

        var items = new FrameworkElementFactory(typeof(ItemsPresenter));
        scroll.AppendChild(items);
        popupBorder.AppendChild(scroll);
        popup.AppendChild(popupBorder);
        root.AppendChild(popup);

        var template = new ControlTemplate(typeof(ComboBox))
        {
            VisualTree = root
        };

        var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hoverTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, TrayWeakTextBrush, "Bd"));
        hoverTrigger.Setters.Add(new Setter(System.Windows.Shapes.Path.FillProperty, TrayTextBrush, "Arrow"));

        var focusTrigger = new Trigger { Property = UIElement.IsKeyboardFocusWithinProperty, Value = true };
        focusTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, Theme.ActiveBrush, "Bd"));

        var openTrigger = new Trigger { Property = ComboBox.IsDropDownOpenProperty, Value = true };
        openTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, Theme.ActiveBrush, "Bd"));
        openTrigger.Setters.Add(new Setter(System.Windows.Shapes.Path.FillProperty, Theme.ActiveBrush, "Arrow"));

        var disabledTrigger = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabledTrigger.Setters.Add(new Setter(UIElement.OpacityProperty, 0.55));

        template.Triggers.Add(hoverTrigger);
        template.Triggers.Add(focusTrigger);
        template.Triggers.Add(openTrigger);
        template.Triggers.Add(disabledTrigger);
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        return style;
    }

    private Style BuildSettingsComboBoxItemStyle()
    {
        var style = new Style(typeof(ComboBoxItem));
        style.Setters.Add(new Setter(Control.ForegroundProperty, TrayTextBrush));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 5, 8, 5)));
        style.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
        style.Setters.Add(new Setter(UIElement.SnapsToDevicePixelsProperty, true));
        style.Setters.Add(new Setter(FrameworkElement.UseLayoutRoundingProperty, true));

        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "Bd";
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.CornerRadiusProperty, AppUi.ControlRadius);
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));

        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ContentControl.ContentProperty));
        content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        content.SetValue(System.Windows.Documents.TextElement.ForegroundProperty, new TemplateBindingExtension(Control.ForegroundProperty));
        border.AppendChild(content);

        var template = new ControlTemplate(typeof(ComboBoxItem))
        {
            VisualTree = border
        };

        var hoverTrigger = new Trigger { Property = ComboBoxItem.IsHighlightedProperty, Value = true };
        hoverTrigger.Setters.Add(new Setter(Control.BackgroundProperty, TrayHoverBrush));

        var selectedTrigger = new Trigger { Property = ComboBoxItem.IsSelectedProperty, Value = true };
        selectedTrigger.Setters.Add(new Setter(Control.BackgroundProperty, Theme.Tint((byte)(Theme.IsDark ? 46 : 28))));
        selectedTrigger.Setters.Add(new Setter(Control.ForegroundProperty, TrayTextBrush));

        template.Triggers.Add(hoverTrigger);
        template.Triggers.Add(selectedTrigger);
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
        border.SetValue(Border.CornerRadiusProperty, AppUi.SmallRadius);
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

    private Style BuildSettingsCloseButtonStyle()
    {
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        style.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
        style.Setters.Add(new Setter(UIElement.SnapsToDevicePixelsProperty, true));
        style.Setters.Add(new Setter(FrameworkElement.UseLayoutRoundingProperty, true));

        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "Bd";
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
        border.SetValue(Border.CornerRadiusProperty, AppUi.ControlRadius);
        border.SetValue(UIElement.SnapsToDevicePixelsProperty, true);

        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ContentControl.ContentProperty));
        content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        content.SetValue(System.Windows.Documents.TextElement.ForegroundProperty, new TemplateBindingExtension(Control.ForegroundProperty));
        border.AppendChild(content);

        var template = new ControlTemplate(typeof(Button))
        {
            VisualTree = border
        };

        var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hoverTrigger.Setters.Add(new Setter(Control.BackgroundProperty, TrayHoverBrush));
        hoverTrigger.Setters.Add(new Setter(Control.ForegroundProperty, TrayTextBrush));

        var pressedTrigger = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
        pressedTrigger.Setters.Add(new Setter(Control.BackgroundProperty, Theme.ActiveBrush));
        pressedTrigger.Setters.Add(new Setter(Control.ForegroundProperty, TrayPaperBrush));

        template.Triggers.Add(hoverTrigger);
        template.Triggers.Add(pressedTrigger);

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
        var minLeft = area.Left + 16;
        var minTop = area.Top + 16;
        var maxLeft = area.Right - width - 16;
        var maxTop = area.Bottom - height - 16;
        var centeredLeft = area.Left + (area.Width - width) / 2;
        var centeredTop = area.Top + (area.Height - height) / 2;

        window.Left = ClampWindowCoordinate(centeredLeft, minLeft, maxLeft);
        window.Top = ClampWindowCoordinate(centeredTop, minTop, maxTop);
    }

    private static double ClampWindowCoordinate(double value, double min, double max)
    {
        return max < min ? min : Math.Clamp(value, min, max);
    }


    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.General)
        {
            if (State.Theme == "system")
            {
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    Theme.Invalidate();
                    foreach (var window in _windows.Values)
                    {
                        window.UpdateTheme();
                    }
                    foreach (var m in _masterCapsules.Values) m.UpdateTheme();
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

    private void ToggleAnimations()
    {
        State.EnableAnimations = !State.EnableAnimations;
        SaveNow();
        RefreshSettingsWindowContent();
    }

    private void ToggleHidePapersFromWindowSwitcher()
    {
        State.HidePapersFromWindowSwitcher = !State.HidePapersFromWindowSwitcher;
        SaveNow();
        RefreshPaperWindowSwitcherVisibility();
        RefreshSettingsWindowContent();
    }

    private void RefreshPaperWindowSwitcherVisibility()
    {
        foreach (var window in _windows.Values)
        {
            window.UpdateWindowSwitcherVisibility();
        }
    }

    private void TogglePersistentPowerShellProcess()
    {
        State.UsePersistentPowerShellProcess = !State.UsePersistentPowerShellProcess;
        if (!State.UsePersistentPowerShellProcess)
        {
            PaperWindow.StopPersistentScriptProcesses();
        }
        else
        {
            PaperWindow.EnsurePersistentScriptProcessForSettings(State);
        }

        SaveNow();
        RefreshSettingsWindowContent();
    }

    private void TogglePreferPowerShell7()
    {
        State.PreferPowerShell7 = !State.PreferPowerShell7;
        PaperWindow.StopPersistentScriptProcesses();
        PaperWindow.EnsurePersistentScriptProcessForSettings(State);
        SaveNow();
        RefreshSettingsWindowContent();
    }

    private void ToggleHideScriptRunWindow()
    {
        State.HideScriptRunWindow = !State.HideScriptRunWindow;
        PaperWindow.StopPersistentScriptProcesses();
        PaperWindow.EnsurePersistentScriptProcessForSettings(State);
        SaveNow();
        RefreshSettingsWindowContent();
    }

    private void ToggleToolTips()
    {
        State.EnableToolTips = !State.EnableToolTips;
        SaveNow();
        RefreshToolTipSetting();
        RefreshSettingsWindowContent();
    }

    private void RefreshToolTipSetting()
    {
        foreach (var window in _windows.Values)
        {
            window.UpdateToolTipSetting();
        }

        foreach (var m in _masterCapsules.Values) m.UpdateToolTipSetting();

        if (_settingsWindow != null)
        {
            ApplyToolTipSetting(_settingsWindow);
        }
    }

    private void ApplyToolTipSetting(Window window)
    {
        ToolTipPreferences.Apply(window, State.EnableToolTips);
    }

    private void ToggleCapsuleMode()
    {
        State.UseCapsuleMode = !State.UseCapsuleMode;

        if (!State.UseCapsuleMode)
        {
            State.UseDeepCapsuleMode = false;
            State.UseCapsuleCollapseAll = false;
            State.CapsuleCollapseAllActive = false;
            State.CapsuleCollapseAllActiveQueues.Clear();
            ResetDeepCapsuleStartTopMargins();
            foreach (var paper in State.Papers)
            {
                paper.IsCollapsed = false;
            }
        }

        foreach (var window in _windows.Values)
        {
            window.UpdateCapsuleMode();
        }

        ArrangeDeepCapsules();
        RestoreMissingVisiblePaperSurfaces();
        SaveNow();
        RebuildTrayMenu();
        RefreshSettingsCapsuleToggleStates();
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

    private void ToggleLinkedNoteNameDisplay()
    {
        State.ShowLinkedNoteName = !State.ShowLinkedNoteName;

        foreach (var window in _windows.Values)
        {
            window.RefreshTodoRowsForExternalChange();
        }

        SaveNow();
        RefreshSettingsWindowContent();
    }

    private void ToggleTodoDueRelativeTime()
    {
        State.ShowTodoDueRelativeTime = !State.ShowTodoDueRelativeTime;

        foreach (var window in _windows.Values)
        {
            window.RefreshTodoRowsForExternalChange();
        }

        SaveNow();
        RefreshSettingsWindowContent();
    }

    private void ToggleTodoReminderInterval()
    {
        State.UseTodoReminderInterval = !State.UseTodoReminderInterval;
        _shownTodoReminderKeys.Clear();
        _lastTodoReminderShownAt.Clear();
        SaveNow();
        RefreshSettingsWindowContent();
    }

    private void ToggleLongLinkedNoteTitles()
    {
        State.AllowLongLinkedNoteTitles = !State.AllowLongLinkedNoteTitles;

        foreach (var window in _windows.Values)
        {
            window.RefreshTodoRowsForExternalChange();
        }

        SaveNow();
        RefreshSettingsWindowContent();
    }

    private void ToggleHideLinkedNotesFromCapsules()
    {
        State.HideLinkedNotesFromCapsules = !State.HideLinkedNotesFromCapsules;
        RefreshCapsuleEligibilityForLinkedNotes();
        SaveNow();
        RefreshSettingsWindowContent();
    }

    private void ToggleRunLinkedScriptCapsulesOnClick()
    {
        State.RunLinkedScriptCapsulesOnClick = !State.RunLinkedScriptCapsulesOnClick;

        foreach (var window in _windows.Values)
        {
            window.RefreshTodoRowsForExternalChange();
        }

        SaveNow();
        RefreshSettingsWindowContent();
    }

    private void ToggleTodoNoteLinks()
    {
        State.EnableTodoNoteLinks = !State.EnableTodoNoteLinks;
        ClearNoteLinkDropTarget();

        foreach (var window in _windows.Values)
        {
            window.UpdateTodoLinkFeature();
        }

        RefreshCapsuleEligibilityForLinkedNotes();
        SaveNow();
        RefreshSettingsWindowContent();
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
        else if (!State.UseDeepCapsuleMode)
        {
            State.UseCapsuleCollapseAll = false;
            State.CapsuleCollapseAllActive = false;
            State.CapsuleCollapseAllActiveQueues.Clear();
            ResetDeepCapsuleStartTopMargins();
        }

        foreach (var window in _windows.Values)
        {
            window.UpdateDeepCapsuleMode();
        }

        ArrangeDeepCapsules();
        RestoreMissingVisiblePaperSurfaces();
        SaveNow();
        RebuildTrayMenu();
        RefreshSettingsCapsuleToggleStates();
    }

    private void ToggleDeepCapsuleExpandedSlot()
    {
        State.ShowDeepCapsuleWhileExpanded = !State.ShowDeepCapsuleWhileExpanded;

        foreach (var window in _windows.Values)
        {
            window.UpdateDeepCapsuleExpandedSlotMode();
        }

        ArrangeDeepCapsules(animate: State.EnableAnimations);
        SaveNow();
        RefreshSettingsCapsuleToggleStates();
    }

    private void ToggleCollapseExpandedDeepCapsuleOnClick()
    {
        State.CollapseExpandedDeepCapsuleOnClick = !State.CollapseExpandedDeepCapsuleOnClick;
        SaveNow();
        RefreshSettingsCapsuleToggleStates();
    }

    private void ToggleHideDeepCapsulesWhenCovered()
    {
        State.HideDeepCapsulesWhenCovered = !State.HideDeepCapsulesWhenCovered;
        ArrangeDeepCapsules(animate: State.EnableAnimations);
        SaveNow();
        RefreshSettingsCapsuleToggleStates();
    }

    private void RestoreMissingVisiblePaperSurfaces()
    {
        foreach (var paper in State.Papers.ToList())
        {
            if (!paper.IsVisible ||
                !_windows.TryGetValue(paper.Id, out var window) ||
                window.HasVisibleSurface)
            {
                continue;
            }

            RestoreExistingPaperWindowSurface(paper, window);
        }
    }

    private void RestoreExistingPaperWindowSurface(PaperData paper, PaperWindow window)
    {
        RescuePaperIfOffScreen(paper, State.Papers.IndexOf(paper));
        window.CancelPendingVisibilityTransitions();
        window.DetachFromDeepCapsuleStack(animate: false);

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

        window.Opacity = 1.0;
        if (!window.IsVisible)
        {
            window.Show();
        }
        window.RefreshEffectiveTopmost();
    }

}
