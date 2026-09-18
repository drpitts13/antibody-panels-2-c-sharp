using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using AntibodyPanels.Data;
using AntibodyPanels.Services.Vendors;
using Microsoft.Win32;

namespace AntibodyPanels.Views.Dialogs
{
    public partial class DownloadFromVendorDialog : Window
    {
        private readonly DatabaseService _db;
        private readonly VendorCatalogService _catalog;
        private readonly VendorPanelImportService _importer;

        public int? ImportedPanelId { get; private set; }
        public string? ImportedPanelName { get; private set; }

        public DownloadFromVendorDialog(DatabaseService db)
        {
            InitializeComponent();
            _db = db;
            _catalog = new VendorCatalogService();
            _importer = new VendorPanelImportService(db);
            VendorBox.ItemsSource = _catalog.Sources.Select(s => s.DisplayName).ToList();
            VendorBox.SelectedIndex = 0;
            Closed += (_, _) => _catalog.Dispose();
        }

        private IVendorPanelSource? SelectedSource
        {
            get
            {
                if (VendorBox.SelectedItem is not string name) return null;
                return _catalog.Sources.FirstOrDefault(s => s.DisplayName == name);
            }
        }

        private void VendorBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            LotGrid.ItemsSource = null;
            var source = SelectedSource;
            if (source == null) return;
            HintText.Text = source.ListLotsUnavailableReason ?? "";
            RefreshButton.IsEnabled = source.CanListLots;
            ImportSelectedButton.IsEnabled = source.CanListLots;
            StatusText.Text = source.CanListLots
                ? "Click Refresh lots to load the public catalog."
                : source.ListLotsUnavailableReason;
        }

        private async void RefreshClick(object sender, RoutedEventArgs e)
        {
            var source = SelectedSource;
            if (source == null || !source.CanListLots) return;
            RefreshButton.IsEnabled = false;
            StatusText.Text = "Loading public lot catalog…";
            try
            {
                var lots = await source.ListLotsAsync().ConfigureAwait(true);
                LotGrid.ItemsSource = lots;
                StatusText.Text = lots.Count == 0
                    ? "No public lots were listed. Import a vendor file instead."
                    : $"Found {lots.Count} public lot(s). Select one and import.";
            }
            catch (Exception ex)
            {
                StatusText.Text = "Could not list lots: " + ex.Message;
            }
            finally
            {
                RefreshButton.IsEnabled = true;
            }
        }

        private async void ImportSelectedClick(object sender, RoutedEventArgs e)
        {
            if (LotGrid.SelectedItem is not VendorLotListing lot)
            {
                MessageBox.Show("Select a lot from the catalog.", "Download",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (string.IsNullOrWhiteSpace(lot.DownloadUrl))
            {
                MessageBox.Show("That listing has no public download URL. Import a vendor file instead.",
                    "Download", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            ImportSelectedButton.IsEnabled = false;
            StatusText.Text = $"Downloading {lot.LotNumber}…";
            try
            {
                var parsed = await _catalog.DownloadAndParseAsync(lot).ConfigureAwait(true);
                PersistParsed(parsed);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Download", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                ImportSelectedButton.IsEnabled = true;
            }
        }

        private void ImportFileClick(object sender, RoutedEventArgs e)
        {
            var source = SelectedSource;
            if (source == null) return;
            var open = new OpenFileDialog
            {
                Title = "Import vendor panel file",
                Filter = "Vendor files|*.pdf;*.csv|PDF|*.pdf|CSV|*.csv|All files|*.*"
            };
            if (open.ShowDialog() != true) return;
            try
            {
                var parsed = _catalog.ImportFile(source.VendorId, open.FileName);
                parsed.SourceUrl ??= open.FileName;
                PersistParsed(parsed);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Import", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void PersistParsed(VendorParseResult parsed)
        {
            if (!parsed.Success)
            {
                MessageBox.Show(
                    "Could not parse the vendor file:\n" + string.Join("\n", parsed.Errors.Take(12)),
                    "Import", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var existing = _db.FindPanelByVendorLot(parsed.Vendor, parsed.LotNumber);
            var replace = false;
            if (existing != null)
            {
                var answer = MessageBox.Show(
                    $"Lot {parsed.LotNumber} from {parsed.Vendor} is already stored as '{existing.Name}'. Replace it?",
                    "Duplicate lot", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (answer != MessageBoxResult.Yes) return;
                replace = true;
            }

            ImportedPanelId = _importer.Persist(parsed, replace);
            ImportedPanelName = parsed.Name;
            StatusText.Text = $"Imported '{parsed.Name}' ({parsed.Cells.Count} cells).";
            DialogResult = true;
        }
    }
}
