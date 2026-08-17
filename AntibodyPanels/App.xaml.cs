using System;
using System.Linq;
using System.Windows;
using AntibodyPanels.Data;
using AntibodyPanels.Services;

namespace AntibodyPanels
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            DispatcherUnhandledException += (_, args) =>
            {
                MessageBox.Show(args.Exception.Message, "Antibody Panels",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
            };

            if (e.Args.Any(a => string.Equals(a, "--seed-clinical", StringComparison.OrdinalIgnoreCase)))
            {
                using var db = new DatabaseService();
                ClinicalDataSeeder.SeedIfNeeded(db, new AntibodyAnalyzer(db));
                Shutdown();
                return;
            }

            base.OnStartup(e);
        }
    }
}
