using System.Diagnostics;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using MdBlock = Markdig.Syntax.Block;
using MdInline = Markdig.Syntax.Inlines.Inline;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using WpfInline = System.Windows.Documents.Inline;
using WpfList = System.Windows.Documents.List;
using WpfListItem = System.Windows.Documents.ListItem;

namespace PaperTodo;

public static class MarkdownRenderer
{
    private static readonly DependencyProperty SourceStartProperty =
        DependencyProperty.RegisterAttached(
            "SourceStart",
            typeof(int),
            typeof(MarkdownRenderer),
            new PropertyMetadata(-1));

    private static readonly DependencyProperty SourceLengthProperty =
        DependencyProperty.RegisterAttached(
            "SourceLength",
            typeof(int),
            typeof(MarkdownRenderer),
            new PropertyMetadata(0));

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAutoLinks()
        .UseEmphasisExtras()
        .UsePreciseSourceLocation()
        .Build();


    private static readonly System.Windows.Media.FontFamily ConsolasFontFamily = new("Consolas");
    private static Brush TextBrush => Theme.TextBrush;
    private static Brush WeakBrush => Theme.WeakTextBrush;
    private static Brush CodeBrush => Theme.CodeBrush;
    private static Brush QuoteBorderBrush => Theme.QuoteBorderBrush;
    private static Brush LinkBrush => Theme.LinkBrush;

    public static FlowDocument Render(string? markdown)
    {
        var source = markdown ?? string.Empty;
        var document = CreateDocument();
        var parsed = Markdown.Parse(source, Pipeline);

        foreach (var block in parsed)
        {
            AddBlock(document.Blocks, block, source);
        }

        if (document.Blocks.Count == 0)
        {
            document.Blocks.Add(new Paragraph());
        }

        return document;
    }

    public static bool TryGetSourceSpan(TextElement element, out int start, out int length)
    {
        start = (int)element.GetValue(SourceStartProperty);
        length = (int)element.GetValue(SourceLengthProperty);
        return start >= 0 && length >= 0;
    }

    private static void SetSourceSpan(TextElement element, int start, int length)
    {
        if (start < 0)
        {
            return;
        }

        element.SetValue(SourceStartProperty, start);
        element.SetValue(SourceLengthProperty, Math.Max(0, length));
    }

    private static int SourceLength(MdBlock block)
    {
        return SourceLength(block.Span.Start, block.Span.End);
    }

    private static int SourceLength(int start, int end)
    {
        return start >= 0 && end >= start ? end - start + 1 : 0;
    }

    private static FlowDocument CreateDocument()
    {
        return new FlowDocument
        {
            PagePadding = new Thickness(14, 8, 14, 8),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 14,
            Foreground = TextBrush,
            Background = Brushes.Transparent,
            LineHeight = 21
        };
    }

    private static void AddBlock(BlockCollection blocks, MdBlock block, string source)
    {
        switch (block)
        {
            case HeadingBlock heading:
                AddHeading(blocks, heading, source);
                break;

            case ParagraphBlock paragraph:
                blocks.Add(CreateParagraph(paragraph.Inline, new Thickness(0, 2, 0, 6), source, paragraph.Span.Start, SourceLength(paragraph)));
                break;

            case QuoteBlock quote:
                AddQuote(blocks, quote, source);
                break;

            case Markdig.Syntax.ListBlock list:
                AddList(blocks, list, source);
                break;

            case FencedCodeBlock fenced:
                AddCodeBlock(blocks, fenced.Lines.ToString(), SourceStartForText(source, fenced.Span.Start, fenced.Span.End, fenced.Lines.ToString().TrimEnd('\r', '\n')), fenced.Span.Start, SourceLength(fenced));
                break;

            case CodeBlock code:
                AddCodeBlock(blocks, code.Lines.ToString(), SourceStartForText(source, code.Span.Start, code.Span.End, code.Lines.ToString().TrimEnd('\r', '\n')), code.Span.Start, SourceLength(code));
                break;

            case ThematicBreakBlock thematicBreak:
                AddThematicBreak(blocks, thematicBreak.Span.Start, SourceLength(thematicBreak));
                break;

            case HtmlBlock:
                // PaperTodo intentionally does not support embedded HTML.
                break;

            default:
                if (block is ContainerBlock container)
                {
                    foreach (var child in container)
                    {
                        AddBlock(blocks, child, source);
                    }
                }
                break;
        }
    }

    private static void AddHeading(BlockCollection blocks, HeadingBlock heading, string source)
    {
        var size = heading.Level switch
        {
            1 => 21,
            2 => 18,
            3 => 16,
            _ => 14
        };

        var paragraph = CreateParagraph(heading.Inline, new Thickness(0, heading.Level == 1 ? 8 : 6, 0, 6), source, heading.Span.Start, SourceLength(heading));
        paragraph.FontSize = size;
        paragraph.FontWeight = FontWeights.SemiBold;
        blocks.Add(paragraph);
    }

    private static Paragraph CreateParagraph(ContainerInline? inline, Thickness margin, string source, int sourceStart, int sourceLength)
    {
        var paragraph = new Paragraph
        {
            Margin = margin
        };

        SetSourceSpan(paragraph, sourceStart, sourceLength);
        AddInlines(paragraph.Inlines, inline, source);
        return paragraph;
    }

    private static void AddQuote(BlockCollection blocks, QuoteBlock quote, string source)
    {
        var section = new Section
        {
            Margin = new Thickness(6, 4, 0, 8),
            Padding = new Thickness(8, 0, 0, 0),
            BorderThickness = new Thickness(3, 0, 0, 0),
            BorderBrush = QuoteBorderBrush,
            Foreground = WeakBrush
        };
        SetSourceSpan(section, quote.Span.Start, SourceLength(quote));

        foreach (var child in quote)
        {
            AddBlock(section.Blocks, child, source);
        }

        blocks.Add(section);
    }

    private static void AddList(BlockCollection blocks, Markdig.Syntax.ListBlock list, string source)
    {
        var wpfList = new WpfList
        {
            MarkerStyle = list.IsOrdered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
            Margin = new Thickness(16, 2, 0, 6),
            Padding = new Thickness(12, 0, 0, 0)
        };
        SetSourceSpan(wpfList, list.Span.Start, SourceLength(list));

        foreach (var rawItem in list)
        {
            if (rawItem is not ListItemBlock item)
            {
                continue;
            }

            var wpfItem = new WpfListItem();
            SetSourceSpan(wpfItem, item.Span.Start, SourceLength(item));

            foreach (var child in item)
            {
                AddBlock(wpfItem.Blocks, child, source);
            }

            if (wpfItem.Blocks.Count == 0)
            {
                wpfItem.Blocks.Add(new Paragraph());
            }

            wpfList.ListItems.Add(wpfItem);
        }

        blocks.Add(wpfList);
    }

    private static void AddCodeBlock(BlockCollection blocks, string code, int sourceStart, int blockSourceStart, int blockSourceLength)
    {
        var renderedCode = code.TrimEnd('\r', '\n');
        var paragraph = new Paragraph
        {
            FontFamily = ConsolasFontFamily,
            FontSize = 13,
            Background = CodeBrush,
            Padding = new Thickness(8),
            Margin = new Thickness(0, 6, 0, 8)
        };
        SetSourceSpan(paragraph, blockSourceStart, blockSourceLength);

        var run = new Run(renderedCode);
        SetSourceSpan(run, sourceStart, renderedCode.Length);
        paragraph.Inlines.Add(run);
        blocks.Add(paragraph);
    }

    private static void AddThematicBreak(BlockCollection blocks, int sourceStart, int sourceLength)
    {
        var paragraph = new Paragraph
        {
            Margin = new Thickness(0, 8, 0, 8),
            Foreground = WeakBrush
        };
        SetSourceSpan(paragraph, sourceStart, sourceLength);
        paragraph.Inlines.Add(new Run("────────────────"));
        blocks.Add(paragraph);
    }

    private static void AddInlines(InlineCollection target, ContainerInline? container, string source)
    {
        if (container == null)
        {
            return;
        }

        for (var inline = container.FirstChild; inline != null; inline = inline.NextSibling)
        {
            AddInline(target, inline, source);
        }
    }

    private static void AddInline(InlineCollection target, MdInline inline, string source)
    {
        switch (inline)
        {
            case LiteralInline literal:
                {
                    var text = literal.Content.ToString();
                    var run = new Run(text);
                    SetSourceSpan(run, literal.Span.Start, text.Length);
                    target.Add(run);
                }
                break;

            case CodeInline code:
                {
                    var run = new Run(code.Content)
                    {
                        FontFamily = ConsolasFontFamily,
                        FontSize = 13,
                        Background = CodeBrush
                    };
                    SetSourceSpan(run, SourceStartForText(source, code.Span.Start, code.Span.End, code.Content), code.Content.Length);
                    target.Add(run);
                }
                break;

            case EmphasisInline emphasis:
                target.Add(RenderEmphasis(emphasis, source));
                break;

            case LinkInline link:
                AddLink(target, link, source);
                break;

            case LineBreakInline:
                target.Add(new LineBreak());
                break;

            case HtmlInline:
                // PaperTodo intentionally does not support embedded HTML.
                break;

            case ContainerInline container:
                AddInlines(target, container, source);
                break;

            default:
                if (inline is LeafInline leaf)
                {
                    var text = leaf.ToString() ?? string.Empty;
                    var run = new Run(text);
                    SetSourceSpan(run, leaf.Span.Start, text.Length);
                    target.Add(run);
                }
                break;
        }
    }

    private static WpfInline RenderEmphasis(EmphasisInline emphasis, string source)
    {
        var span = new Span();
        AddInlines(span.Inlines, emphasis, source);

        if (emphasis.DelimiterChar == '~')
        {
            span.TextDecorations = TextDecorations.Strikethrough;
            span.Foreground = WeakBrush;
            return span;
        }

        if (emphasis.DelimiterCount >= 2)
        {
            return new Bold(span);
        }

        return new Italic(span);
    }

    private static void AddLink(InlineCollection target, LinkInline link, string source)
    {
        if (link.IsImage)
        {
            // Images are intentionally unsupported. Render the alt text only.
            var alt = new Span { Foreground = WeakBrush };
            AddInlines(alt.Inlines, link, source);
            target.Add(alt);
            return;
        }

        var label = new Span();
        AddInlines(label.Inlines, link, source);

        if (label.Inlines.Count == 0)
        {
            label.Inlines.Add(new Run(link.Url ?? Strings.Get("MarkdownDefaultLinkLabel")));
        }

        var hyperlink = new Hyperlink(label)
        {
            Foreground = LinkBrush
        };

        if (!string.IsNullOrEmpty(link.Url) && Uri.TryCreate(link.Url, UriKind.Absolute, out var uri))
        {
            hyperlink.NavigateUri = uri;
            hyperlink.RequestNavigate += OpenLink;
            hyperlink.Cursor = System.Windows.Input.Cursors.Hand;
        }

        target.Add(hyperlink);
    }

    private static int SourceStartForText(string source, int spanStart, int spanEnd, string text)
    {
        if (spanStart < 0)
        {
            return -1;
        }

        if (string.IsNullOrEmpty(text) || spanStart >= source.Length)
        {
            return spanStart;
        }

        var endExclusive = spanEnd >= spanStart
            ? Math.Min(source.Length, spanEnd + 1)
            : source.Length;
        if (endExclusive <= spanStart)
        {
            return spanStart;
        }

        var spanText = source.Substring(spanStart, endExclusive - spanStart);
        var index = spanText.IndexOf(text, StringComparison.Ordinal);
        return index >= 0 ? spanStart + index : spanStart;
    }
    private static void OpenLink(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        if (e.Uri == null)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = e.Uri.ToString(),
                UseShellExecute = true
            });
            e.Handled = true;
        }
        catch
        {
            // Keep preview quiet if Windows cannot open the link.
        }
    }
}
