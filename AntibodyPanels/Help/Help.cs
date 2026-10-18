using System.Windows;

namespace AntibodyPanels.Help;

public static class Help
{
    public static readonly DependencyProperty TopicProperty = DependencyProperty.RegisterAttached(
        "Topic",
        typeof(string),
        typeof(Help),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits));

    public static void SetTopic(DependencyObject element, string? value) =>
        element.SetValue(TopicProperty, value);

    public static string? GetTopic(DependencyObject element) =>
        (string?)element.GetValue(TopicProperty);
}
