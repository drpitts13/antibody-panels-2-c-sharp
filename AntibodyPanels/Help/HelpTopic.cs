namespace AntibodyPanels.Help;

public sealed class HelpTopic
{
    public HelpTopic(string id, string title, string? parentId, string markdown)
    {
        Id = id;
        Title = title;
        ParentId = parentId;
        Markdown = markdown;
    }

    public string Id { get; }
    public string Title { get; }
    public string? ParentId { get; }
    public string Markdown { get; }
}

public sealed class HelpTopicNode
{
    public HelpTopicNode(HelpTopic topic)
    {
        Id = topic.Id;
        Title = topic.Title;
        Markdown = topic.Markdown;
        Topic = topic;
    }

    public string Id { get; }
    public string Title { get; }
    public string Markdown { get; }
    public HelpTopic Topic { get; }
    public List<HelpTopicNode> Children { get; } = new();
}
