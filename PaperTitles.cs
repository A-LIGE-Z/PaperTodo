using System.Globalization;

namespace PaperTodo;

public static class PaperTitles
{
    public const int MaxTitleLength = 40;

    public static string DefaultTitle(string paperType, int number)
    {
        var prefix = paperType == PaperTypes.Note ? "笔记" : "待办";
        return prefix + Math.Max(1, number).ToString(CultureInfo.InvariantCulture);
    }

    public static string CleanCustomTitle(string? title)
    {
        var cleaned = (title ?? "").Trim();
        cleaned = string.Join("", cleaned.Where(ch => !char.IsControl(ch)));
        return TakeTextElements(cleaned, MaxTitleLength);
    }

    public static string EffectiveTitle(PaperData paper, int fallbackNumber)
    {
        var title = CleanCustomTitle(paper.Title);
        return string.IsNullOrWhiteSpace(title)
            ? DefaultTitle(paper.Type, fallbackNumber)
            : title;
    }

    public static string CapsuleText(PaperData paper, int fallbackNumber)
    {
        return EffectiveTitle(paper, fallbackNumber);
    }

    private static string TakeTextElements(string text, int maxLength)
    {
        var indexes = StringInfo.ParseCombiningCharacters(text);
        if (indexes.Length <= maxLength)
        {
            return text;
        }

        return text[..indexes[maxLength]];
    }
}
