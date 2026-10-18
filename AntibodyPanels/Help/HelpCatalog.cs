using System.IO;
using System.Reflection;
using System.Text;

namespace AntibodyPanels.Help;

public static class HelpCatalog
{
    public const string ManualRootId = "getting-started";
    public const string ShortcutsId = "shortcuts";

    public static readonly IReadOnlyList<string> MainTabIds =
    [
        "worklist", "specimens", "panels", "reactions", "analysis",
        "reports", "analytics", "search", "rules"
    ];

    public static readonly IReadOnlyList<string> AnalysisTabIds =
    [
        "analysis-explain", "analysis-suspected", "analysis-ruled-out",
        "analysis-patterns", "analysis-combinations", "analysis-dosage",
        "analysis-special", "analysis-typing", "analysis-selected", "analysis-summary"
    ];

    public static readonly IReadOnlyList<string> DialogIds =
    [
        "specimen-dialog", "select-panel", "panel-dialog", "download-vendor",
        "import-review", "artifact-viewer", "panel-compare", "warehouse-antigen",
        "panel-run", "reason-dialog", "rule-dialog", "preferences", "purge",
        "archive", "audit-log"
    ];

    private static readonly (string Id, string Title, string? ParentId)[] Definitions =
    [
        ("getting-started", "Getting started", null),
        ("worklist", "Worklist", null),
        ("specimens", "Specimens", null),
        ("specimen-dialog", "Specimen record", "specimens"),
        ("select-panel", "Attach a panel", "specimens"),
        ("panels", "Panels", null),
        ("panel-dialog", "Panel details", "panels"),
        ("download-vendor", "Download from vendor", "panels"),
        ("import-review", "Review an import", "panels"),
        ("artifact-viewer", "Import artifact", "panels"),
        ("panel-compare", "Compare panels", "panels"),
        ("warehouse-antigen", "Warehouse antigens", "panels"),
        ("reactions", "Reactions", null),
        ("panel-run", "Add a run", "reactions"),
        ("analysis", "Analysis", null),
        ("analysis-explain", "Explain", "analysis"),
        ("analysis-suspected", "Suspected Antibodies", "analysis"),
        ("analysis-ruled-out", "Ruled Out", "analysis"),
        ("analysis-patterns", "Pattern Matches", "analysis"),
        ("analysis-combinations", "Combinations", "analysis"),
        ("analysis-dosage", "Dosage", "analysis"),
        ("analysis-special", "Special Panels", "analysis"),
        ("analysis-typing", "Patient Typing", "analysis"),
        ("analysis-selected", "Selected Cells", "analysis"),
        ("analysis-summary", "Summary", "analysis"),
        ("reason-dialog", "Clear a confirmed ID", "analysis"),
        ("reports", "Reports", null),
        ("analytics", "Analytics", null),
        ("search", "Search", null),
        ("rules", "Rules", null),
        ("rule-dialog", "Rule editor", "rules"),
        ("preferences", "Preferences", null),
        ("purge", "Purge database", null),
        ("archive", "Open archive", null),
        ("audit-log", "Audit log", null),
        ("demo-data", "Demo data", null),
        ("shortcuts", "Keyboard shortcuts", null)
    ];

    private static readonly IReadOnlyList<HelpTopic> Topics = LoadAll();

    public static IReadOnlyList<HelpTopic> All => Topics;

    public static HelpTopic? Find(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        foreach (var topic in Topics)
        {
            if (string.Equals(topic.Id, id, StringComparison.Ordinal))
                return topic;
        }
        return null;
    }

    public static IReadOnlyList<HelpTopicNode> Tree() => BuildTree(Topics);

    public static IReadOnlyList<HelpTopicNode> Filter(string? query)
    {
        var roots = Tree();
        if (string.IsNullOrWhiteSpace(query)) return roots;

        var matches = new List<HelpTopicNode>();
        foreach (var root in roots)
        {
            var match = FilterNode(root, query.Trim());
            if (match != null) matches.Add(match);
        }
        return matches;
    }

    public static IEnumerable<HelpTopic> InReadingOrder()
    {
        foreach (var root in Tree())
        {
            foreach (var node in Flatten(root))
                yield return node.Topic;
        }
    }

    private static IEnumerable<HelpTopicNode> Flatten(HelpTopicNode node)
    {
        yield return node;
        foreach (var child in node.Children)
        {
            foreach (var nested in Flatten(child))
                yield return nested;
        }
    }

    private static HelpTopicNode? FilterNode(HelpTopicNode node, string query)
    {
        var self = node.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                   || node.Markdown.Contains(query, StringComparison.OrdinalIgnoreCase);
        var matchedChildren = new List<HelpTopicNode>();
        foreach (var child in node.Children)
        {
            var match = FilterNode(child, query);
            if (match != null) matchedChildren.Add(match);
        }

        if (!self && matchedChildren.Count == 0) return null;

        var copy = new HelpTopicNode(node.Topic);
        if (self)
        {
            foreach (var child in node.Children)
                copy.Children.Add(CopyTree(child));
        }
        else
        {
            copy.Children.AddRange(matchedChildren);
        }
        return copy;
    }

    private static HelpTopicNode CopyTree(HelpTopicNode node)
    {
        var copy = new HelpTopicNode(node.Topic);
        foreach (var child in node.Children)
            copy.Children.Add(CopyTree(child));
        return copy;
    }

    private static IReadOnlyList<HelpTopicNode> BuildTree(IReadOnlyList<HelpTopic> topics)
    {
        var nodes = new Dictionary<string, HelpTopicNode>(StringComparer.Ordinal);
        foreach (var topic in topics)
            nodes[topic.Id] = new HelpTopicNode(topic);

        var roots = new List<HelpTopicNode>();
        foreach (var topic in topics)
        {
            var node = nodes[topic.Id];
            if (topic.ParentId == null)
                roots.Add(node);
            else
                nodes[topic.ParentId].Children.Add(node);
        }
        return roots;
    }

    private static IReadOnlyList<HelpTopic> LoadAll()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var topics = new List<HelpTopic>(Definitions.Length);
        foreach (var (id, title, parentId) in Definitions)
        {
            if (!seen.Add(id))
                throw new InvalidOperationException($"Duplicate help topic '{id}'.");
            topics.Add(new HelpTopic(id, title, parentId, LoadMarkdown(id)));
        }

        var ids = topics.Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var topic in topics)
        {
            if (topic.ParentId != null && !ids.Contains(topic.ParentId))
                throw new InvalidOperationException(
                    $"Help topic '{topic.Id}' names missing parent '{topic.ParentId}'.");
        }

        return topics;
    }

    private static string LoadMarkdown(string id)
    {
        var name = $"AntibodyPanels.Help.Topics.{id}.md";
        var asm = typeof(HelpCatalog).Assembly;
        using var stream = asm.GetManifestResourceStream(name);
        if (stream == null)
        {
            var have = string.Join(", ", asm.GetManifestResourceNames());
            throw new InvalidOperationException($"Missing help topic '{name}'. Embedded resources: {have}");
        }

        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd().Trim();
    }
}
