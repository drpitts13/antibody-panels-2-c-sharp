using AntibodyPanels.Views.Dialogs;

namespace AntibodyPanels.Help;

public static class HelpService
{
    private static HelpWindow? _window;

    public static void ShowManual() => Show(HelpCatalog.ManualRootId);

    public static void Show(string? topicId)
    {
        var id = HelpCatalog.Find(topicId) != null ? topicId! : HelpCatalog.ManualRootId;
        if (_window == null)
        {
            _window = new HelpWindow();
            _window.Closed += (_, _) => _window = null;
        }

        _window.ShowTopic(id);
        if (!_window.IsVisible)
            _window.Show();
        _window.Activate();
    }

    public static void Close()
    {
        _window?.Close();
        _window = null;
    }
}
