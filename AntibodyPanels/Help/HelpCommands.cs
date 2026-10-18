using System.Windows;
using System.Windows.Input;
using AntibodyPanels.ViewModels;

namespace AntibodyPanels.Help;

public static class HelpCommands
{
    public static ICommand ShowTopic { get; } = new RelayCommand(parameter =>
        HelpService.Show(parameter as string));

    public static ICommand ShowContext { get; } = new RelayCommand(parameter =>
        HelpService.Show(HelpLocator.From(parameter as DependencyObject)));
}
