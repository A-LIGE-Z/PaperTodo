using System.IO;
using System.Text.Json.Serialization;

namespace PaperTodo;

public static class PaperTypes
{
    public const string Todo = "todo";
    public const string Note = "note";
}

public static class PaperLayoutDefaults
{
    public const double MinWidth = 220;
    public const double MinHeight = 160;

    public const double CapsuleWidth = 108; // 包含阴影边框边距
    public const double CapsuleHeight = 46;

    public const double TodoDefaultWidth = 280;
    public const double TodoDefaultHeight = 340;

    public const double NoteDefaultWidth = 320;
    public const double NoteDefaultHeight = 360;
}

public static class MarkdownRenderModes
{
    public const string Off = "off";
    public const string Basic = "basic";
    public const string Enhanced = "enhanced";

    public static bool IsValid(string? mode)
    {
        return mode is Off or Basic or Enhanced;
    }
}

public static class ExternalMarkdownFileExtensions
{
    public const string Default = ".md";

    public static string Normalize(string? extension)
    {
        var value = (extension ?? "").Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return Default;
        }

        if (value.StartsWith("*.", StringComparison.Ordinal))
        {
            value = value[1..];
        }
        if (!value.StartsWith(".", StringComparison.Ordinal))
        {
            value = "." + value;
        }

        if (value.Length is < 2 or > 32 ||
            value.Contains("..", StringComparison.Ordinal) ||
            value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return Default;
        }

        return value.ToLowerInvariant();
    }
}

public sealed class AppState
{
    public List<PaperData> Papers { get; set; } = new();
    public string Theme { get; set; } = "system";
    public string MarkdownRenderMode { get; set; } = MarkdownRenderModes.Enhanced;
    public string ExternalMarkdownExtension { get; set; } = ExternalMarkdownFileExtensions.Default;
    public double Zoom { get; set; } = 1.0;
    public bool UseCapsuleMode { get; set; } = true;
    public bool UseDeepCapsuleMode { get; set; } = true;
    public bool ShowTopBarNewTodoButton { get; set; } = true;
    public bool ShowTopBarNewNoteButton { get; set; } = true;
    public bool ShowTopBarExternalOpenButton { get; set; } = true;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ShowTopBarNewPaperButtons { get; set; }
}

public sealed class PaperData
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Type { get; set; } = PaperTypes.Todo;
    public string Title { get; set; } = "";

    public double X { get; set; } = 120;
    public double Y { get; set; } = 120;
    public double Width { get; set; } = 280;
    public double Height { get; set; } = 360;

    public bool IsVisible { get; set; } = true;
    public bool AlwaysOnTop { get; set; }
    public bool IsCollapsed { get; set; } = false;
    public double TextZoom { get; set; } = 1.0;

    public List<PaperItem> Items { get; set; } = new();
    public string Content { get; set; } = "";
}

public sealed class PaperItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Text { get; set; } = "";
    public bool Done { get; set; }
    public int Order { get; set; }
}

