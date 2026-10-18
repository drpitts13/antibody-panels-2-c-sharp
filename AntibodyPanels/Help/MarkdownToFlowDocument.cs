using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using DocumentList = System.Windows.Documents.List;

namespace AntibodyPanels.Help;

public static class MarkdownToFlowDocument
{
    private static readonly Regex HeadingPattern = new(@"^(#{1,3})\s+(.*)$", RegexOptions.Compiled);
    private static readonly Regex NumberedPattern = new(@"^\d+\.\s+(.*)$", RegexOptions.Compiled);
    private static readonly Regex BulletPattern = new(@"^[-*]\s+(.*)$", RegexOptions.Compiled);

    public static FlowDocument Convert(string? markdown)
    {
        var document = CreateDocument();
        AppendMarkdown(document, markdown, breakBefore: false);
        return document;
    }

    public static FlowDocument ConvertManual(IEnumerable<HelpTopic> topics)
    {
        var document = CreateDocument();
        var first = true;
        foreach (var topic in topics)
        {
            AppendMarkdown(document, topic.Markdown, breakBefore: !first);
            first = false;
        }
        return document;
    }

    private static FlowDocument CreateDocument()
    {
        return new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 14,
            PagePadding = new Thickness(16),
            TextAlignment = TextAlignment.Left
        };
    }

    private static void AppendMarkdown(FlowDocument document, string? markdown, bool breakBefore)
    {
        var lines = (markdown ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        List<string>? listItems = null;
        var numbered = false;
        var paragraph = new StringBuilder();
        var pendingBreak = breakBefore;

        void FlushParagraph()
        {
            if (paragraph.Length == 0) return;
            document.Blocks.Add(MakeParagraph(paragraph.ToString().Trim(), 14, FontWeights.Normal, pendingBreak));
            pendingBreak = false;
            paragraph.Clear();
        }

        void FlushList()
        {
            if (listItems == null) return;
            document.Blocks.Add(MakeList(listItems, numbered, pendingBreak));
            pendingBreak = false;
            listItems = null;
        }

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            if (string.IsNullOrWhiteSpace(line))
            {
                FlushList();
                FlushParagraph();
                continue;
            }

            var heading = HeadingPattern.Match(line.Trim());
            if (heading.Success)
            {
                FlushList();
                FlushParagraph();
                var level = heading.Groups[1].Value.Length;
                var size = level switch { 1 => 22d, 2 => 18d, _ => 15d };
                document.Blocks.Add(MakeParagraph(heading.Groups[2].Value.Trim(), size, FontWeights.Bold, pendingBreak));
                pendingBreak = false;
                continue;
            }

            var numberedMatch = NumberedPattern.Match(line.Trim());
            if (numberedMatch.Success)
            {
                FlushParagraph();
                if (listItems != null && !numbered) FlushList();
                listItems ??= new List<string>();
                numbered = true;
                listItems.Add(numberedMatch.Groups[1].Value.Trim());
                continue;
            }

            var bulletMatch = BulletPattern.Match(line.Trim());
            if (bulletMatch.Success)
            {
                FlushParagraph();
                if (listItems != null && numbered) FlushList();
                listItems ??= new List<string>();
                numbered = false;
                listItems.Add(bulletMatch.Groups[1].Value.Trim());
                continue;
            }

            FlushList();
            if (paragraph.Length > 0) paragraph.Append(' ');
            paragraph.Append(line.Trim());
        }

        FlushList();
        FlushParagraph();
    }

    private static Paragraph MakeParagraph(string text, double fontSize, FontWeight weight, bool breakBefore)
    {
        var paragraph = new Paragraph
        {
            FontSize = fontSize,
            FontWeight = weight,
            Margin = new Thickness(0, 0, 0, 8),
            BreakPageBefore = breakBefore
        };
        AddInlines(paragraph.Inlines, text);
        return paragraph;
    }

    private static DocumentList MakeList(IReadOnlyList<string> items, bool numbered, bool breakBefore)
    {
        var list = new DocumentList
        {
            MarkerStyle = numbered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
            Margin = new Thickness(8, 0, 0, 8)
        };
        var first = true;
        foreach (var item in items)
        {
            var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 2) };
            if (first && breakBefore)
            {
                paragraph.BreakPageBefore = true;
                first = false;
            }
            AddInlines(paragraph.Inlines, item);
            list.ListItems.Add(new ListItem(paragraph));
        }
        return list;
    }

    private static void AddInlines(InlineCollection inlines, string text)
    {
        var parts = text.Split("**");
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length == 0) continue;
            var run = new Run(parts[i]);
            if (i % 2 == 1)
                inlines.Add(new Bold(run));
            else
                inlines.Add(run);
        }
    }
}
