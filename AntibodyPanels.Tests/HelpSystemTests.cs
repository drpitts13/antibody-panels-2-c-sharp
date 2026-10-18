using System.Windows;
using System.Windows.Documents;
using AntibodyPanels.Help;
using DocumentList = System.Windows.Documents.List;

namespace AntibodyPanels.Tests;

public class HelpSystemTests
{
    [Fact]
    public void EveryTopicLoadsNonEmptyMarkdown()
    {
        Assert.NotEmpty(HelpCatalog.All);
        foreach (var topic in HelpCatalog.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(topic.Id));
            Assert.False(string.IsNullOrWhiteSpace(topic.Title));
            Assert.False(string.IsNullOrWhiteSpace(topic.Markdown));
            Assert.StartsWith("# ", topic.Markdown);
        }
    }

    [Fact]
    public void RequiredTopicsCoverEverySection()
    {
        var ids = HelpCatalog.All.Select(topic => topic.Id).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(9, HelpCatalog.MainTabIds.Count);
        Assert.Equal(10, HelpCatalog.AnalysisTabIds.Count);
        Assert.Equal(15, HelpCatalog.DialogIds.Count);

        foreach (var id in HelpCatalog.MainTabIds
                     .Concat(HelpCatalog.AnalysisTabIds)
                     .Concat(HelpCatalog.DialogIds))
        {
            Assert.Contains(id, ids);
        }

        Assert.Contains(HelpCatalog.ManualRootId, ids);
        Assert.Contains(HelpCatalog.ShortcutsId, ids);

        var analysis = HelpCatalog.Tree().Single(node => node.Id == "analysis");
        Assert.Equal(10, analysis.Children.Count(child => child.Id.StartsWith("analysis-", StringComparison.Ordinal)));
        Assert.Contains(analysis.Children, child => child.Id == "reason-dialog");

        var readingOrder = HelpCatalog.InReadingOrder().Select(topic => topic.Id).ToList();
        Assert.Equal(ids.Count, readingOrder.Distinct().Count());
    }

    [Fact]
    public void RendererTurnsHeadingBulletAndBoldIntoFlowDocument()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var document = MarkdownToFlowDocument.Convert(
                    """
                    # Getting started

                    This is **important**.

                    - Worklist
                    - Specimens

                    1. Add a specimen
                    2. Enter reactions
                    """);

                var heading = Assert.IsType<Paragraph>(document.Blocks.FirstBlock);
                Assert.Equal(FontWeights.Bold, heading.FontWeight);
                Assert.True(heading.FontSize > 14);
                var headingRun = Assert.IsType<Run>(heading.Inlines.FirstInline);
                Assert.Equal("Getting started", headingRun.Text);

                Assert.Contains(document.Blocks.OfType<Paragraph>(), paragraph => paragraph.Inlines.OfType<Bold>().Any());

                var lists = document.Blocks.OfType<DocumentList>().ToList();
                Assert.Equal(2, lists.Count);
                Assert.Equal(TextMarkerStyle.Disc, lists[0].MarkerStyle);
                Assert.Equal(2, lists[0].ListItems.Count);
                Assert.Equal(TextMarkerStyle.Decimal, lists[1].MarkerStyle);
                Assert.Equal(2, lists[1].ListItems.Count);
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null)
            throw new Xunit.Sdk.XunitException(error.ToString());
    }
}
