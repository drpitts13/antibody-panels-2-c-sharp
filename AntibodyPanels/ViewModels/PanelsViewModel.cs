using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using AntibodyPanels.Data;
using AntibodyPanels.Models;

namespace AntibodyPanels.ViewModels
{
    public class PanelsViewModel : BaseViewModel
    {
        private readonly DatabaseService _db;
        private readonly MainViewModel _main;

        public ObservableCollection<Panel> Panels { get; } = new();
        public ObservableCollection<PanelCellRow> CellRows { get; } = new();

        private Panel? _selectedPanel;
        public Panel? SelectedPanel
        {
            get => _selectedPanel;
            set
            {
                if (SetField(ref _selectedPanel, value))
                    LoadCells();
            }
        }

        public ICommand AddCommand { get; }
        public ICommand EditCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand CopyCommand { get; }
        public ICommand SaveCellsCommand { get; }
        public ICommand RefreshCommand { get; }

        public PanelsViewModel(DatabaseService db, MainViewModel main)
        {
            _db = db;
            _main = main;
            AddCommand = new RelayCommand(AddPanel);
            EditCommand = new RelayCommand(EditPanel, () => SelectedPanel != null);
            DeleteCommand = new RelayCommand(DeletePanel, () => SelectedPanel != null);
            CopyCommand = new RelayCommand(CopyPanel, () => SelectedPanel != null);
            SaveCellsCommand = new RelayCommand(SaveAllCells, () => SelectedPanel != null);
            RefreshCommand = new RelayCommand(Refresh);
            Refresh();
        }

        public void Refresh()
        {
            var sid = SelectedPanel?.PanelId;
            Panels.Clear();
            foreach (var p in _db.GetAllPanels()) Panels.Add(p);
            SelectedPanel = Panels.FirstOrDefault(p => p.PanelId == sid)
                ?? Panels.FirstOrDefault();
        }

        private void LoadCells()
        {
            CellRows.Clear();
            if (_selectedPanel == null) return;
            foreach (var c in _db.GetPanelCells(_selectedPanel.PanelId))
                CellRows.Add(new PanelCellRow(c));
        }

        /// <summary>
        /// After any panel CRUD, refresh this tab and all other tabs that hold a panel list.
        /// </summary>
        private void NotifyPanelsChanged()
        {
            Refresh();
            _main.ReactionsVM.RefreshSpecimens();
            _main.ReportsVM.Refresh();
        }

        private void AddPanel()
        {
            var dlg = new Views.Dialogs.PanelDialog();
            if (dlg.ShowDialog() != true) return;
            var id = _db.AddPanel(dlg.PanelName, dlg.LotNumber, dlg.Vendor,
                dlg.NumCells, dlg.ExpirationDate, dlg.IncludeAc, dlg.StartCell);
            _main.SetStatus($"Panel '{dlg.PanelName}' created (cells {dlg.StartCell}–{dlg.StartCell + dlg.NumCells - 1}).");
            NotifyPanelsChanged();
            SelectedPanel = Panels.FirstOrDefault(p => p.PanelId == id);
        }

        private void EditPanel()
        {
            if (SelectedPanel == null) return;
            var dlg = new Views.Dialogs.PanelDialog(SelectedPanel);
            if (dlg.ShowDialog() != true) return;
            _db.UpdatePanel(SelectedPanel.PanelId, dlg.PanelName, dlg.LotNumber,
                dlg.Vendor, dlg.NumCells, dlg.ExpirationDate, dlg.IncludeAc, dlg.StartCell);
            _main.SetStatus($"Panel '{dlg.PanelName}' updated.");
            NotifyPanelsChanged();
        }

        private void DeletePanel()
        {
            if (SelectedPanel == null) return;
            if (MessageBox.Show($"Delete panel '{SelectedPanel.Name}'?",
                "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            _db.DeletePanel(SelectedPanel.PanelId);
            _main.SetStatus("Panel deleted.");
            NotifyPanelsChanged();
        }

        private void CopyPanel()
        {
            if (SelectedPanel == null) return;
            // Pre-populate from source so the user sees the original settings
            var dlg = new Views.Dialogs.PanelDialog(new Panel
            {
                Name = SelectedPanel.Name + " (copy)",
                LotNumber = SelectedPanel.LotNumber,
                Vendor = SelectedPanel.Vendor,
                NumCells = SelectedPanel.NumCells,
                StartCell = SelectedPanel.StartCell,
                IncludeAc = SelectedPanel.IncludeAc,
            });
            dlg.Title = "Copy Panel — Edit Details";
            if (dlg.ShowDialog() != true) return;
            // AddPanel creates placeholder cells; CopyPanelCells then replaces them with the real data.
            var newId = _db.AddPanel(dlg.PanelName, dlg.LotNumber, dlg.Vendor,
                dlg.NumCells, dlg.ExpirationDate, dlg.IncludeAc, dlg.StartCell);
            _db.CopyPanelCells(SelectedPanel.PanelId, newId);
            _main.SetStatus($"Panel copied as '{dlg.PanelName}'.");
            NotifyPanelsChanged();
            SelectedPanel = Panels.FirstOrDefault(p => p.PanelId == newId);
        }

        public void SaveAllCells()
        {
            if (SelectedPanel == null) return;
            foreach (var row in CellRows)
                _db.UpdatePanelCell(row.Cell);
            _main.SetStatus($"Panel '{SelectedPanel.Name}' cells saved.");
        }
    }

    /// <summary>
    /// Wraps a PanelCell for DataGrid editing; exposes each antigen as a named property.
    /// </summary>
    public class PanelCellRow : BaseViewModel
    {
        public PanelCell Cell { get; }
        public string CellNumber => Cell.CellNumber;

        public PanelCellRow(PanelCell cell) => Cell = cell;

        public string GetAntigen(string ag) => Cell.GetAntigen(ag);
        public void SetAntigen(string ag, string val) { Cell.SetAntigen(ag, val); OnPropertyChanged(ag); }

        // Individual antigen properties for DataGrid column bindings
        public string D { get => Cell.GetAntigen("D"); set { Cell.SetAntigen("D", value); OnPropertyChanged(); } }
        public string C { get => Cell.GetAntigen("C"); set { Cell.SetAntigen("C", value); OnPropertyChanged(); } }
        public string c { get => Cell.GetAntigen("c"); set { Cell.SetAntigen("c", value); OnPropertyChanged(); } }
        public string E { get => Cell.GetAntigen("E"); set { Cell.SetAntigen("E", value); OnPropertyChanged(); } }
        public string e { get => Cell.GetAntigen("e"); set { Cell.SetAntigen("e", value); OnPropertyChanged(); } }
        public string f { get => Cell.GetAntigen("f"); set { Cell.SetAntigen("f", value); OnPropertyChanged(); } }
        public string Cw { get => Cell.GetAntigen("Cw"); set { Cell.SetAntigen("Cw", value); OnPropertyChanged(); } }
        public string V { get => Cell.GetAntigen("V"); set { Cell.SetAntigen("V", value); OnPropertyChanged(); } }
        public string K { get => Cell.GetAntigen("K"); set { Cell.SetAntigen("K", value); OnPropertyChanged(); } }
        public string k { get => Cell.GetAntigen("k"); set { Cell.SetAntigen("k", value); OnPropertyChanged(); } }
        public string Kpa { get => Cell.GetAntigen("Kpa"); set { Cell.SetAntigen("Kpa", value); OnPropertyChanged(); } }
        public string Kpb { get => Cell.GetAntigen("Kpb"); set { Cell.SetAntigen("Kpb", value); OnPropertyChanged(); } }
        public string Jsa { get => Cell.GetAntigen("Jsa"); set { Cell.SetAntigen("Jsa", value); OnPropertyChanged(); } }
        public string Jsb { get => Cell.GetAntigen("Jsb"); set { Cell.SetAntigen("Jsb", value); OnPropertyChanged(); } }
        public string Jka { get => Cell.GetAntigen("Jka"); set { Cell.SetAntigen("Jka", value); OnPropertyChanged(); } }
        public string Jkb { get => Cell.GetAntigen("Jkb"); set { Cell.SetAntigen("Jkb", value); OnPropertyChanged(); } }
        public string Fya { get => Cell.GetAntigen("Fya"); set { Cell.SetAntigen("Fya", value); OnPropertyChanged(); } }
        public string Fyb { get => Cell.GetAntigen("Fyb"); set { Cell.SetAntigen("Fyb", value); OnPropertyChanged(); } }
        public string Lea { get => Cell.GetAntigen("Lea"); set { Cell.SetAntigen("Lea", value); OnPropertyChanged(); } }
        public string Leb { get => Cell.GetAntigen("Leb"); set { Cell.SetAntigen("Leb", value); OnPropertyChanged(); } }
        public string M { get => Cell.GetAntigen("M"); set { Cell.SetAntigen("M", value); OnPropertyChanged(); } }
        public string N { get => Cell.GetAntigen("N"); set { Cell.SetAntigen("N", value); OnPropertyChanged(); } }
        public string S { get => Cell.GetAntigen("S"); set { Cell.SetAntigen("S", value); OnPropertyChanged(); } }
        public string s { get => Cell.GetAntigen("s"); set { Cell.SetAntigen("s", value); OnPropertyChanged(); } }
        public string Lua { get => Cell.GetAntigen("Lua"); set { Cell.SetAntigen("Lua", value); OnPropertyChanged(); } }
        public string Lub { get => Cell.GetAntigen("Lub"); set { Cell.SetAntigen("Lub", value); OnPropertyChanged(); } }
        public string Xga { get => Cell.GetAntigen("Xga"); set { Cell.SetAntigen("Xga", value); OnPropertyChanged(); } }
        public string P1 { get => Cell.GetAntigen("P1"); set { Cell.SetAntigen("P1", value); OnPropertyChanged(); } }
    }
}
