using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using AntibodyPanels.Data;
using AntibodyPanels.Models;

namespace AntibodyPanels.ViewModels
{
    public class SpecimensViewModel : BaseViewModel
    {
        private readonly DatabaseService _db;
        private readonly MainViewModel _main;

        public ObservableCollection<Specimen> Specimens { get; } = new();
        public ObservableCollection<Panel> AllPanels { get; } = new();
        public ObservableCollection<Panel> LinkedPanels { get; } = new();
        public ObservableCollection<SpecimenAntibody> Antibodies { get; } = new();
        public ObservableCollection<SpecimenRuleout> Ruleouts { get; } = new();

        private Specimen? _selectedSpecimen;
        public Specimen? SelectedSpecimen
        {
            get => _selectedSpecimen;
            set
            {
                if (SetField(ref _selectedSpecimen, value))
                    LoadSpecimenDetails();
            }
        }

        private Panel? _selectedLinkedPanel;
        public Panel? SelectedLinkedPanel
        {
            get => _selectedLinkedPanel;
            set => SetField(ref _selectedLinkedPanel, value);
        }

        private bool _showInactive = false;
        public bool ShowInactive
        {
            get => _showInactive;
            set { if (SetField(ref _showInactive, value)) ApplyFilter(); }
        }

        private string _listFilter = string.Empty;
        public string ListFilter
        {
            get => _listFilter;
            set { if (SetField(ref _listFilter, value)) ApplyFilter(); }
        }

        public ICommand AddCommand { get; }
        public ICommand EditCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand AttachPanelCommand { get; }
        public ICommand DetachPanelCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand OpenReactionsCommand { get; }
        public ICommand OpenAnalysisCommand { get; }
        public ICommand OpenReportCommand { get; }

        public SpecimensViewModel(DatabaseService db, MainViewModel main)
        {
            _db = db;
            _main = main;

            AddCommand = new RelayCommand(AddSpecimen);
            EditCommand = new RelayCommand(EditSpecimen, () => SelectedSpecimen != null);
            DeleteCommand = new RelayCommand(DeleteSpecimen, () => SelectedSpecimen != null);
            AttachPanelCommand = new RelayCommand(AttachPanel, () => SelectedSpecimen != null);
            DetachPanelCommand = new RelayCommand(DetachPanel,
                () => SelectedSpecimen != null && SelectedLinkedPanel != null);
            RefreshCommand = new RelayCommand(Refresh);
            OpenReactionsCommand = new RelayCommand(OpenReactions, () => SelectedSpecimen != null);
            OpenAnalysisCommand = new RelayCommand(OpenAnalysis, () => SelectedSpecimen != null);
            OpenReportCommand = new RelayCommand(OpenReport, () => SelectedSpecimen != null);

            Refresh();
        }

        private List<Specimen> _allSpecimens = new();

        public void Refresh()
        {
            var selected = SelectedSpecimen?.AccessionNumber;
            _allSpecimens = _db.GetAllSpecimens();
            ApplyFilter(selected);
        }

        public void SelectSpecimen(string accessionNumber)
        {
            EnsureVisible(accessionNumber);
            SelectedSpecimen = Specimens.FirstOrDefault(s => s.AccessionNumber == accessionNumber)
                ?? SelectedSpecimen;
        }

        private void ApplyFilter(string? preferredAccession = null)
        {
            preferredAccession ??= SelectedSpecimen?.AccessionNumber;
            Specimens.Clear();
            foreach (var s in _allSpecimens)
            {
                if (!_showInactive && !s.IsActive) continue;
                if (!s.MatchesFilter(_listFilter)) continue;
                Specimens.Add(s);
            }
            SelectedSpecimen = Specimens.FirstOrDefault(s => s.AccessionNumber == preferredAccession)
                ?? Specimens.FirstOrDefault();
        }

        private void EnsureVisible(string accessionNumber)
        {
            var match = _allSpecimens.FirstOrDefault(s => s.AccessionNumber == accessionNumber);
            if (match == null) return;
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
            ApplyFilter(accessionNumber);
        }

        private void LoadSpecimenDetails()
        {
            LinkedPanels.Clear();
            Antibodies.Clear();
            Ruleouts.Clear();

            if (_selectedSpecimen == null) return;
            var sid = _selectedSpecimen.AccessionNumber;

            foreach (var p in _db.GetSpecimenPanels(sid)) LinkedPanels.Add(p);
            foreach (var a in _db.GetSpecimenAntibodies(sid)) Antibodies.Add(a);
            foreach (var r in _db.GetSpecimenRuleouts(sid)) Ruleouts.Add(r);
        }

        /// <summary>
        /// After any specimen CRUD, refresh both this tab and any other tabs that hold a specimen list.
        /// </summary>
        private void NotifySpecimensChanged()
        {
            Refresh();
            _main.ReactionsVM.RefreshSpecimens();
            _main.AnalysisVM.Refresh();
            _main.ReportsVM.Refresh();
            _main.WorklistVM.Refresh();
        }

        private void AddSpecimen()
        {
            var dlg = new Views.Dialogs.SpecimenDialog();
            if (dlg.ShowDialog() != true) return;
            var existing = _db.GetSpecimen(dlg.AccessionNumber);
            if (existing != null)
            {
                var open = MessageBox.Show(
                    $"Accession {dlg.AccessionNumber} already exists. Open that specimen instead?",
                    "Duplicate accession", MessageBoxButton.YesNo, MessageBoxImage.Information);
                if (open == MessageBoxResult.Yes)
                    SelectSpecimen(dlg.AccessionNumber);
                return;
            }
            try
            {
                _db.AddSpecimen(dlg.AccessionNumber, dlg.SpecimenType, dlg.ExpirationDate, dlg.ItemIsActive,
                    dlg.Notes, dlg.Phenotype, dlg.PreviousAntibodies, dlg.DatResult);
                _main.SetStatus($"Specimen {dlg.AccessionNumber} added.");
                NotifySpecimensChanged();
                SelectedSpecimen = Specimens.FirstOrDefault(s => s.AccessionNumber == dlg.AccessionNumber);
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Error adding specimen: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void EditSpecimen()
        {
            if (SelectedSpecimen == null) return;
            var dlg = new Views.Dialogs.SpecimenDialog(SelectedSpecimen);
            if (dlg.ShowDialog() != true) return;
            try
            {
                _db.UpdateSpecimen(SelectedSpecimen.AccessionNumber, dlg.SpecimenType, dlg.ExpirationDate, dlg.ItemIsActive,
                    dlg.Notes, dlg.Phenotype, dlg.PreviousAntibodies, dlg.DatResult);
                _main.SetStatus($"Specimen {SelectedSpecimen.AccessionNumber} updated.");
                NotifySpecimensChanged();
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Error updating specimen: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DeleteSpecimen()
        {
            if (SelectedSpecimen == null) return;
            var acc = SelectedSpecimen.AccessionNumber;
            if (MessageBox.Show($"Delete specimen {acc}? This will also delete all reactions and analysis results.",
                "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            _db.DeleteSpecimen(acc);
            _main.SetStatus($"Specimen {acc} deleted.");
            NotifySpecimensChanged();
        }

        private void OpenReactions()
        {
            if (SelectedSpecimen == null) return;
            _main.OpenSpecimenReactions(SelectedSpecimen.AccessionNumber);
        }

        private void OpenAnalysis()
        {
            if (SelectedSpecimen == null) return;
            _main.OpenSpecimenAnalysis(SelectedSpecimen.AccessionNumber);
        }

        private void OpenReport()
        {
            if (SelectedSpecimen == null) return;
            _main.OpenSpecimenReport(SelectedSpecimen.AccessionNumber);
        }

        private void AttachPanel()
        {
            if (SelectedSpecimen == null) return;
            var allPanels = _db.GetAllPanels();
            var linked = _db.GetSpecimenPanels(SelectedSpecimen.AccessionNumber)
                .Select(p => p.PanelId).ToHashSet();
            var available = allPanels.Where(p => !linked.Contains(p.PanelId) && p.IsActive).ToList();

            var dlg = new Views.Dialogs.SelectPanelDialog(available);
            if (dlg.ShowDialog() != true || dlg.SelectedPanel == null) return;
            _db.LinkSpecimenPanel(SelectedSpecimen.AccessionNumber, dlg.SelectedPanel.PanelId);
            _main.SetStatus($"Panel '{dlg.SelectedPanel.Name}' attached.");
            LoadSpecimenDetails();
            _main.ReactionsVM.RefreshSpecimens();
        }

        private void DetachPanel()
        {
            if (SelectedSpecimen == null || SelectedLinkedPanel == null) return;
            if (MessageBox.Show($"Detach panel '{SelectedLinkedPanel.Name}'?",
                "Confirm", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            _db.UnlinkSpecimenPanel(SelectedSpecimen.AccessionNumber, SelectedLinkedPanel.PanelId);
            _main.SetStatus("Panel detached.");
            LoadSpecimenDetails();
            _main.ReactionsVM.RefreshSpecimens();
        }
    }
}
