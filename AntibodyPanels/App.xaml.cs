using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using AntibodyPanels.Help;
using AntibodyPanels.Services.Vendors;
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
                AppLog.Error("Unhandled exception", args.Exception);
                MessageBox.Show(args.Exception.Message, "Antibody Panels",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
            };
            AppLog.Info("Application starting");
            EventManager.RegisterClassHandler(
                typeof(Window),
                Keyboard.PreviewKeyDownEvent,
                new KeyEventHandler(OnPreviewKeyDown));

            if (e.Args.Any(a => string.Equals(a, "--seed-clinical", StringComparison.OrdinalIgnoreCase)))
            {
                using var db = new DatabaseService();
                ClinicalDataSeeder.SeedIfNeeded(db, new AntibodyAnalyzer(db));
                DemoDataSeeder.SeedIfNeeded(db);
                Shutdown();
                return;
            }

            if (e.Args.Any(a => string.Equals(a, "--import-vendor-samples", StringComparison.OrdinalIgnoreCase)))
            {
                using var db = new DatabaseService();
                var results = AntibodyPanels.Services.Vendors.VendorLiveImport
                    .ImportRandomLotPerVendorAsync(db).GetAwaiter().GetResult();
                foreach (var row in results)
                    Console.WriteLine($"{row.Vendor}: {(row.Persisted ? "OK" : "SKIP")} {row.LotNumber} {row.Message}");
                Shutdown(results.Any(r => r.DownloadSupported && !r.Persisted) ? 2 : 0);
                return;
            }

            base.OnStartup(e);
        }

        private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.F1 || e.Handled) return;
            if (sender is Views.Dialogs.HelpWindow) return;

            e.Handled = true;
            var origin = Keyboard.FocusedElement as DependencyObject ?? sender as DependencyObject;
            HelpService.Show(HelpLocator.From(origin));
        }
    }
}
