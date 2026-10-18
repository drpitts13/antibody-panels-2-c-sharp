using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using AntibodyPanels.Help;

namespace AntibodyPanels.Views.Dialogs;

public partial class HelpWindow : Window
{
    private bool _suppressSelection;
    private string _pendingId = HelpCatalog.ManualRootId;
    private HelpTopicNode? _current;

    public HelpWindow()
    {
        InitializeComponent();
    }

    public void ShowTopic(string topicId)
    {
        _pendingId = string.IsNullOrWhiteSpace(topicId) ? HelpCatalog.ManualRootId : topicId;
        var topic = HelpCatalog.Find(_pendingId) ?? HelpCatalog.Find(HelpCatalog.ManualRootId);
        if (topic != null)
        {
            _pendingId = topic.Id;
            Title = $"User Manual — {topic.Title}";
            if (Viewer != null)
                Viewer.Document = MarkdownToFlowDocument.Convert(topic.Markdown);
        }

        if (!IsLoaded) return;
        SearchBox.Text = "";
        ApplyTree(HelpCatalog.Tree(), _pendingId);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e) =>
        ApplyTree(HelpCatalog.Tree(), _pendingId);

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        var keep = (TopicTree.SelectedItem as HelpTopicNode)?.Id ?? _pendingId;
        ApplyTree(HelpCatalog.Filter(SearchBox.Text), keep);
    }

    private void TopicTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_suppressSelection) return;
        if (e.NewValue is not HelpTopicNode node) return;
        _current = node;
        _pendingId = node.Id;
        Title = $"User Manual — {node.Title}";
        Viewer.Document = MarkdownToFlowDocument.Convert(node.Markdown);
    }

    private void PrintChapterClick(object sender, RoutedEventArgs e)
    {
        var topic = _current?.Topic ?? HelpCatalog.Find(_pendingId) ?? HelpCatalog.Find(HelpCatalog.ManualRootId);
        if (topic == null) return;
        Print(MarkdownToFlowDocument.Convert(topic.Markdown), topic.Title);
    }

    private void PrintManualClick(object sender, RoutedEventArgs e) =>
        Print(MarkdownToFlowDocument.ConvertManual(HelpCatalog.InReadingOrder()), "User Manual");

    private void ApplyTree(IReadOnlyList<HelpTopicNode> roots, string preferredId)
    {
        _suppressSelection = true;
        TopicTree.ItemsSource = roots;
        _suppressSelection = false;
        Dispatcher.BeginInvoke(() =>
        {
            if (!SelectNode(preferredId) && TopicTree.Items.Count > 0 && TopicTree.Items[0] is HelpTopicNode first)
                SelectNode(first.Id);
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private bool SelectNode(string id)
    {
        var item = FindContainer(TopicTree, id);
        if (item == null) return false;
        item.IsSelected = true;
        item.BringIntoView();
        return true;
    }

    private static TreeViewItem? FindContainer(ItemsControl parent, string id)
    {
        foreach (var entry in parent.Items)
        {
            if (parent.ItemContainerGenerator.ContainerFromItem(entry) is not TreeViewItem container)
                continue;

            if (entry is HelpTopicNode node && node.Id == id)
                return container;

            if (entry is HelpTopicNode branch && Contains(branch, id))
            {
                container.IsExpanded = true;
                container.UpdateLayout();
                var nested = FindContainer(container, id);
                if (nested != null) return nested;
            }
        }
        return null;
    }

    private static bool Contains(HelpTopicNode node, string id)
    {
        if (node.Id == id) return true;
        foreach (var child in node.Children)
        {
            if (Contains(child, id)) return true;
        }
        return false;
    }

    private static void Print(FlowDocument document, string jobName)
    {
        var dialog = new PrintDialog();
        if (dialog.ShowDialog() != true) return;

        document.PageHeight = dialog.PrintableAreaHeight;
        document.PageWidth = dialog.PrintableAreaWidth;
        document.PagePadding = new Thickness(48);
        document.ColumnGap = 0;
        document.ColumnWidth = Math.Max(200, dialog.PrintableAreaWidth - 96);
        dialog.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator, jobName);
    }
}
