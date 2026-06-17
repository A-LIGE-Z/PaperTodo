using System.Globalization;
using System.Windows;

namespace PaperTodo;

// Shared geometry for the edge-aligned (deep) capsule stack. Both the real paper
// capsules (PaperWindow) and the standalone "collapse-all" master capsule resolve
// their positions through here so they never disagree on where a slot sits.
public static class DeepCapsuleLayout
{
    // Focus reveal is a right-anchored viewport expansion. It intentionally reveals only
    // part of the hidden tail so the capsule does not travel too far into the desktop.
    public const double HoverOutsideOffset = 26;
    // Expanded windows opened from edge-aligned capsules should not hug the screen edge.
    public const double ExpandedRightInset = 36;
    // Vertical breathing room at the top/bottom of the work area.
    public const double TopMargin = 8;
    // Where slot 0 starts from the top of the work area (leaves room above for reach).
    public const double StartTopMargin = 48;
    // Vertical gap between stacked capsules.
    public const double Gap = 4;
    // Shared pill corner radius for the real capsules and the master capsule.
    public const double CornerRadius = 12;
    // Transparent outer chrome around the capsule body. The docked viewport must hide at
    // least this margin plus the corner radius so the screen edge cuts through the straight
    // body, not through the rounded cap.
    public const double WindowChromeMargin = 8;
    // Top-level transparent windows are expensive to move; slightly longer durations give
    // the compositor more frames and make each frame's position delta smaller.
    public const int SlideOutMilliseconds = 220;
    public const int SlideInMilliseconds = 180;
    public const int SlotMoveMilliseconds = 200;
    public static double FocusVisibleWidth(double capsuleWindowWidth, double restingVisibleWidth)
    {
        var resting = Math.Clamp(restingVisibleWidth, 1, capsuleWindowWidth);
        var visibleWidth = resting + ((capsuleWindowWidth - resting) * 0.5);
        return Math.Clamp(visibleWidth, Math.Min(54, capsuleWindowWidth), capsuleWindowWidth);
    }

    // Display-weighted character count: CJK / fullwidth glyphs count as 2, everything
    // else as 1. A 6-digit number title then weighs the same as a 3-CJK-character title,
    // so the capsule no longer looks long-but-empty for numeric titles.
    public static int DisplayWidth(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var width = 0;
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            width += IsWide((string)enumerator.Current) ? 2 : 1;
        }

        return width;
    }

    private static bool IsWide(string element)
    {
        if (element.Length == 0)
        {
            return false;
        }

        var c = element[0];
        // CJK Unified, Hiragana/Katakana, Hangul, fullwidth forms, CJK symbols/punct.
        return (c >= 0x1100 && c <= 0x115F)   // Hangul Jamo
            || (c >= 0x2E80 && c <= 0x303E)   // CJK radicals / Kangxi / symbols
            || (c >= 0x3041 && c <= 0x33FF)   // Hiragana, Katakana, CJK symbols
            || (c >= 0x3400 && c <= 0x4DBF)   // CJK Ext A
            || (c >= 0x4E00 && c <= 0x9FFF)   // CJK Unified
            || (c >= 0xA000 && c <= 0xA4CF)   // Yi
            || (c >= 0xAC00 && c <= 0xD7A3)   // Hangul syllables
            || (c >= 0xF900 && c <= 0xFAFF)   // CJK compatibility ideographs
            || (c >= 0xFF00 && c <= 0xFF60)   // Fullwidth forms
            || (c >= 0xFFE0 && c <= 0xFFE6);  // Fullwidth signs
    }

    public static Rect WorkArea => SystemParameters.WorkArea;

    public static double SlotHeight => PaperLayoutDefaults.CapsuleHeight + Gap;

    public static double TopForIndex(int index, double startTopMargin = StartTopMargin)
    {
        var area = WorkArea;
        var desiredTop = area.Top + NormalizeStartTopMargin(startTopMargin) + Math.Max(0, index) * SlotHeight;
        var maxTop = Math.Max(area.Top + TopMargin, area.Bottom - PaperLayoutDefaults.CapsuleHeight - TopMargin);
        return Math.Min(desiredTop, maxTop);
    }

    public static double MaxStartTopMarginForCount(int slotCount)
    {
        var area = WorkArea;
        var count = Math.Max(1, slotCount);
        var stackHeight = PaperLayoutDefaults.CapsuleHeight + (count - 1) * SlotHeight;
        var maxMargin = area.Height - stackHeight - TopMargin;
        return Math.Max(TopMargin, maxMargin);
    }

    public static double NormalizeStartTopMargin(double value, int slotCount = 1)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            value = StartTopMargin;
        }

        var max = MaxStartTopMarginForCount(slotCount);
        return Math.Round(Math.Clamp(value, TopMargin, max), 1);
    }
}
