using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AntibodyPanels.Help;

public static class HelpLocator
{
    public static string? From(DependencyObject? origin)
    {
        if (origin == null) return null;

        var root = AncestorOrSelf<Window>(origin) ?? (DependencyObject)origin;
        var tabTopic = DeepestSelectedTabTopic(root);
        if (!string.IsNullOrEmpty(tabTopic))
            return tabTopic;

        return NearestLocalTopic(origin);
    }

    public static string? NearestLocalTopic(DependencyObject? origin)
    {
        for (var node = origin; node != null; node = ParentOf(node))
        {
            if (node.ReadLocalValue(Help.TopicProperty) is string id && !string.IsNullOrWhiteSpace(id))
                return id;
        }
        return null;
    }

    private static string? DeepestSelectedTabTopic(DependencyObject root)
    {
        string? found = null;
        Walk(root);
        return found;

        void Walk(DependencyObject node)
        {
            if (node is TabControl tabs && tabs.SelectedItem is TabItem item)
            {
                if (item.ReadLocalValue(Help.TopicProperty) is string tabId && !string.IsNullOrWhiteSpace(tabId))
                    found = tabId;
                else if (item.Content is DependencyObject content
                         && content.ReadLocalValue(Help.TopicProperty) is string contentId
                         && !string.IsNullOrWhiteSpace(contentId))
                    found = contentId;

                if (item.Content is DependencyObject nested)
                    Walk(nested);
            }

            var count = VisualTreeHelper.GetChildrenCount(node);
            for (var i = 0; i < count; i++)
                Walk(VisualTreeHelper.GetChild(node, i));
        }
    }

    private static T? AncestorOrSelf<T>(DependencyObject origin) where T : DependencyObject
    {
        for (var node = origin; node != null; node = ParentOf(node))
        {
            if (node is T match) return match;
        }
        return null;
    }

    private static DependencyObject? ParentOf(DependencyObject node)
        => VisualTreeHelper.GetParent(node) ?? LogicalTreeHelper.GetParent(node);
}
