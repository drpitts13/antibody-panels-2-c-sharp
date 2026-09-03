using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using AntibodyPanels.Data;
using AntibodyPanels.Models;
using AntibodyPanels.Services;

namespace AntibodyPanels.ViewModels
{
    public class PanelsViewModel : BaseViewModel
    {
        private readonly DatabaseService _db;
        private readonly MainViewModel _main;

        public ObservableCollection<Panel> Panels { get; } = new();
        public ObservableCollection<PanelCellRow> CellRows { get; } = new();
        public ObservableCollection<string> ExtraAntigens { get; } = new();
        public ObservableCollection<string> AntigenDisplayOrder { get; } = new();

        private int _antigenColumnsRevision;
        public int AntigenColumnsRevision
        {
            get => _antigenColumnsRevision;
            private set => SetField(ref _antigenColumnsRevision, value);
        }

        private Panel? _selectedPanel;
        public Panel? SelectedPanel
        {
            get => _selectedPanel;
            set
            {
                if (IsEditingAntigens && !ReferenceEquals(_selectedPanel, value))
                {
                    _main.SetStatus("Save or Cancel antigen edits before selecting another panel.");
                    OnPropertyChanged(nameof(SelectedPanel));
                    return;
                }
                if (SetField(ref _selectedPanel, value))
                    LoadCells();
            }
        }

        private bool _showInactive = false;
        public bool ShowInactive
        {
            get => _showInactive;
            set
            {
                if (IsEditingAntigens) { OnPropertyChanged(nameof(ShowInactive)); return; }
                if (SetField(ref _showInactive, value)) ApplyFilter();
            }
        }

        private string _listFilter = string.Empty;
        public string ListFilter
        {
            get => _listFilter;
            set
            {
                if (IsEditingAntigens) { OnPropertyChanged(nameof(ListFilter)); return; }
                if (SetField(ref _listFilter, value)) ApplyFilter();
            }
        }

        private bool _isEditingAntigens;
        public bool IsEditingAntigens
        {
            get => _isEditingAntigens;
            private set
            {
                if (SetField(ref _isEditingAntigens, value))
                    CommandManager.InvalidateRequerySuggested();
            }
        }

        public ICommand AddCommand { get; }
        public ICommand EditCommand { get; }
        public ICommand EditDetailsCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand CopyCommand { get; }
        public ICommand ImportCsvCommand { get; }
        public ICommand ExportCsvCommand { get; }
        public ICommand PrintAntigramCommand { get; }
        public ICommand AddExtraAntigenCommand { get; }
        public ICommand RemoveExtraAntigenCommand { get; }
        public ICommand SaveCellsCommand { get; }
        public ICommand CancelEditCommand { get; }
        public ICommand RefreshCommand { get; }

        public PanelsViewModel(DatabaseService db, MainViewModel main)
        {
            _db = db;
            _main = main;
            AddCommand = new RelayCommand(AddPanel, () => !IsEditingAntigens);
            EditCommand = new RelayCommand(BeginEditAntigens, () => SelectedPanel != null && !IsEditingAntigens);
            EditDetailsCommand = new RelayCommand(EditPanel, () => SelectedPanel != null && !IsEditingAntigens);
            DeleteCommand = new RelayCommand(DeletePanel, () => SelectedPanel != null && !IsEditingAntigens);
            CopyCommand = new RelayCommand(CopyPanel, () => SelectedPanel != null && !IsEditingAntigens);
            ImportCsvCommand = new RelayCommand(ImportCsv, () => !IsEditingAntigens);
            ExportCsvCommand = new RelayCommand(ExportCsv, () => SelectedPanel != null && !IsEditingAntigens);
            PrintAntigramCommand = new RelayCommand(PrintAntigram, () => SelectedPanel != null && !IsEditingAntigens);
            AddExtraAntigenCommand = new RelayCommand(AddExtraAntigen, () => SelectedPanel != null && !IsEditingAntigens);
            RemoveExtraAntigenCommand = new RelayCommand(RemoveExtraAntigen, () => SelectedPanel != null && !IsEditingAntigens && ExtraAntigens.Count > 0);
            SaveCellsCommand = new RelayCommand(SaveAllCells, () => IsEditingAntigens);
            CancelEditCommand = new RelayCommand(CancelEdit, () => IsEditingAntigens);
            RefreshCommand = new RelayCommand(Refresh);
            Refresh();
        }

        private List<Panel> _allPanels = new();

        public void Refresh()
        {
            var sid = SelectedPanel?.PanelId;
            _allPanels = _db.GetAllPanels();
            ApplyFilter(sid);
        }

        public void SelectPanel(int panelId)
        {
            var match = _allPanels.FirstOrDefault(p => p.PanelId == panelId);
            if (match != null && !IsEditingAntigens)
            {
                if (!_showInactive && !match.IsActive)
                {
                    _showInactive = true;
                    OnPropertyChanged(nameof(ShowInactive));
                }
                if (!match.MatchesFilter(_listFilter))
                {
                    _listFilter = string.Empty;
                    OnPropertyChanged(nameof(ListFilter));
                }
                ApplyFilter(panelId);
            }
            SelectedPanel = Panels.FirstOrDefault(p => p.PanelId == panelId) ?? SelectedPanel;
        }

        private void ApplyFilter(int? preferredId = null)
        {
            if (IsEditingAntigens) return;
            preferredId ??= SelectedPanel?.PanelId;
            Panels.Clear();
            foreach (var p in _allPanels)
            {
                if (!_showInactive && !p.IsActive) continue;
                if (!p.MatchesFilter(_listFilter)) continue;
                Panels.Add(p);
            }
            SelectedPanel = Panels.FirstOrDefault(p => p.PanelId == preferredId)
                ?? Panels.FirstOrDefault();
        }

        private void LoadCells()
        {
            CellRows.Clear();
            ExtraAntigens.Clear();
            AntigenDisplayOrder.Clear();
            if (_selectedPanel == null)
            {
                AntigenColumnsRevision++;
                return;
            }
            foreach (var ag in _db.GetPanelExtraAntigens(_selectedPanel.PanelId))
                ExtraAntigens.Add(ag);
            foreach (var ag in _db.GetPanelDisplayAntigens(_selectedPanel.PanelId))
                AntigenDisplayOrder.Add(ag);
            foreach (var c in _db.GetPanelCells(_selectedPanel.PanelId))
                CellRows.Add(new PanelCellRow(c));
            AntigenColumnsRevision++;
            CommandManager.InvalidateRequerySuggested();
        }

        /// <summary>
        /// Updates the in-memory column order from a header drag. Does not persist
        /// until Save, and does not rebuild the grid.
        /// </summary>
        public void ReplaceAntigenDisplayOrder(IReadOnlyList<string> order)
        {
            if (!IsEditingAntigens) return;
            AntigenDisplayOrder.Clear();
            foreach (var ag in order)
            {
                if (AntigenConstants.IsKnown(ag))
                    AntigenDisplayOrder.Add(ag);
            }
        }

        /// <summary>
        /// After any panel CRUD, refresh this tab and all other tabs that hold a panel list.
        /// </summary>
        private void NotifyPanelsChanged()
        {
            Refresh();
            _main.ReactionsVM.RefreshSpecimens();
            _main.ReportsVM.Refresh();
            _main.WorklistVM.Refresh();
        }

        private void AddPanel()
        {
            var dlg = new Views.Dialogs.PanelDialog();
            if (dlg.ShowDialog() != true) return;
            var id = _db.AddPanel(dlg.PanelName, dlg.LotNumber, dlg.Vendor,
                dlg.NumCells, dlg.ExpirationDate, dlg.IncludeAc, dlg.StartCell, dlg.ItemIsActive);
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
                dlg.Vendor, dlg.NumCells, dlg.ExpirationDate, dlg.IncludeAc, dlg.StartCell, dlg.ItemIsActive);
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
                dlg.NumCells, dlg.ExpirationDate, dlg.IncludeAc, dlg.StartCell, dlg.ItemIsActive);
            _db.CopyPanelCells(SelectedPanel.PanelId, newId);
            _main.SetStatus($"Panel copied as '{dlg.PanelName}'.");
            NotifyPanelsChanged();
            SelectedPanel = Panels.FirstOrDefault(p => p.PanelId == newId);
        }

        private void ImportCsv()
        {
            var open = new OpenFileDialog
            {
                Filter = "CSV Files|*.csv|All Files|*.*",
                Title = "Import panel from CSV"
            };
            if (open.ShowDialog() != true) return;

            var imported = PanelCsvService.Import(open.FileName);
            if (!imported.Success)
            {
                MessageBox.Show(
                    "Could not import CSV:\n" + string.Join("\n", imported.Errors.Take(12)),
                    "Import", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var includeAc = imported.Cells.Any(c =>
                string.Equals(c.CellNumber, "AC", System.StringComparison.OrdinalIgnoreCase));
            var numCells = imported.Cells.Count(c =>
                !string.Equals(c.CellNumber, "AC", System.StringComparison.OrdinalIgnoreCase));
            int startCell = 1;
            foreach (var c in imported.Cells)
            {
                if (int.TryParse(c.CellNumber, out var n)) { startCell = n; break; }
            }

            var dlg = new Views.Dialogs.PanelDialog(new Panel
            {
                Name = System.IO.Path.GetFileNameWithoutExtension(open.FileName),
                NumCells = numCells,
                StartCell = startCell,
                IncludeAc = includeAc,
            });
            dlg.Title = "Import Panel — Details";
            if (dlg.ShowDialog() != true) return;

            var id = _db.AddPanel(dlg.PanelName, dlg.LotNumber, dlg.Vendor,
                dlg.NumCells, dlg.ExpirationDate, dlg.IncludeAc, dlg.StartCell, dlg.ItemIsActive);
            var cells = imported.Cells.Select(c =>
            {
                var cell = new PanelCell { CellNumber = c.CellNumber };
                foreach (var ag in AntigenConstants.Antigens)
                    cell.SetAntigen(ag, c.Antigens.TryGetValue(ag, out var v) ? v : "-");
                foreach (var ag in AntigenConstants.WarehouseAntigens)
                {
                    if (!c.Antigens.ContainsKey(ag)) continue;
                    cell.SetAntigen(ag, c.Antigens[ag]);
                }
                return cell;
            }).ToList();
            _db.ReplacePanelCells(id, cells);
            if (imported.AntigenHeaderOrder.Count > 0)
                _db.SetPanelAntigenOrder(id, imported.AntigenHeaderOrder);
            _main.SetStatus($"Imported panel '{dlg.PanelName}' ({cells.Count} cells).");
            NotifyPanelsChanged();
            SelectedPanel = Panels.FirstOrDefault(p => p.PanelId == id);
        }

        private void ExportCsv()
        {
            if (SelectedPanel == null) return;
            var save = new SaveFileDialog
            {
                Filter = "CSV Files|*.csv",
                DefaultExt = "csv",
                FileName = $"{SelectedPanel.Name}.csv"
            };
            if (save.ShowDialog() != true) return;
            PanelCsvService.Export(
                _db.GetPanelCells(SelectedPanel.PanelId),
                save.FileName,
                _db.GetPanelAntigenOrder(SelectedPanel.PanelId));
            _main.SetStatus($"Panel CSV exported: {save.FileName}");
            MessageBox.Show("Panel CSV exported.", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void PrintAntigram()
        {
            if (SelectedPanel == null) return;
            var save = new SaveFileDialog
            {
                Filter = "PDF Files|*.pdf",
                DefaultExt = "pdf",
                FileName = $"{SelectedPanel.Name} antigram.pdf"
            };
            if (save.ShowDialog() != true) return;
            try
            {
                new ReportService(_db).ExportToPdf(ReportType.PanelAntigram, save.FileName,
                    panelId: SelectedPanel.PanelId);
                _main.SetStatus($"Antigram PDF saved: {save.FileName}");
                MessageBox.Show("Antigram PDF saved.", "Print antigram",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Could not save antigram:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AddExtraAntigen()
        {
            if (SelectedPanel == null) return;
            var already = new HashSet<string>(ExtraAntigens);
            var available = AntigenConstants.WarehouseCatalog
                .Where(d => !already.Contains(d.Name))
                .ToList();
            if (available.Count == 0)
            {
                MessageBox.Show("All warehouse antigens are already on this panel.", "Add antigen",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new Views.Dialogs.SelectWarehouseAntigenDialog(
                available,
                "Add antigen",
                "Select non-standard antigens to add to this panel. Type +/− on each cell after adding.");
            if (dlg.ShowDialog() != true) return;

            foreach (var ag in dlg.SelectedAntigens)
                _db.AddPanelExtraAntigen(SelectedPanel.PanelId, ag);
            LoadCells();
            _main.SetStatus($"Added {string.Join(", ", dlg.SelectedAntigens)} to '{SelectedPanel.Name}'.");
        }

        private void RemoveExtraAntigen()
        {
            if (SelectedPanel == null || ExtraAntigens.Count == 0) return;
            var assigned = AntigenConstants.WarehouseCatalog
                .Where(d => ExtraAntigens.Contains(d.Name))
                .ToList();
            var dlg = new Views.Dialogs.SelectWarehouseAntigenDialog(
                assigned,
                "Remove antigen",
                "Remove extra antigens from this panel. Typed values for those columns will be deleted.");
            if (dlg.ShowDialog() != true) return;
            if (MessageBox.Show(
                    $"Remove {string.Join(", ", dlg.SelectedAntigens)} from '{SelectedPanel.Name}'?",
                    "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            foreach (var ag in dlg.SelectedAntigens)
                _db.RemovePanelExtraAntigen(SelectedPanel.PanelId, ag);
            LoadCells();
            _main.SetStatus($"Removed extra antigen(s) from '{SelectedPanel.Name}'.");
        }

        private void BeginEditAntigens()
        {
            if (SelectedPanel == null) return;
            IsEditingAntigens = true;
            _main.SetStatus($"Editing antigens for '{SelectedPanel.Name}'. Save or Cancel when done.");
        }

        public void SaveAllCells()
        {
            if (SelectedPanel == null || !IsEditingAntigens) return;
            foreach (var row in CellRows)
                _db.UpdatePanelCell(row.Cell);
            _db.SetPanelAntigenOrder(SelectedPanel.PanelId, AntigenDisplayOrder.ToList());
            IsEditingAntigens = false;
            _main.SetStatus($"Panel '{SelectedPanel.Name}' cells saved.");
        }

        private void CancelEdit()
        {
            IsEditingAntigens = false;
            LoadCells();
            _main.SetStatus("Antigen edits cancelled.");
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

        public IReadOnlyDictionary<string, string> AntigenValues => Cell.Antigens;

        public string GetAntigen(string ag) => Cell.GetAntigen(ag);
        public void SetAntigen(string ag, string val)
        {
            Cell.SetAntigen(ag, val);
            OnPropertyChanged(ag);
            OnPropertyChanged($"AntigenValues[{ag}]");
        }

        public void ToggleAntigen(string ag)
        {
            SetAntigen(ag, GetAntigen(ag) == "+" ? "-" : "+");
        }

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
