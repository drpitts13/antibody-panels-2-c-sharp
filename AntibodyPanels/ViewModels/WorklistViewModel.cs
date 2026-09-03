using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using AntibodyPanels.Data;
using AntibodyPanels.Models;
using AntibodyPanels.Services;

namespace AntibodyPanels.ViewModels
{
    public class WorklistViewModel : BaseViewModel
    {
        private readonly DatabaseService _db;
        private readonly MainViewModel _main;
        private List<WorklistItem> _allItems = new();

        public ObservableCollection<WorklistItem> Items { get; } = new();

        private WorklistItem? _selectedItem;
        public WorklistItem? SelectedItem
        {
            get => _selectedItem;
            set => SetField(ref _selectedItem, value);
        }

        private int _incompleteCount;
        public int IncompleteCount
        {
            get => _incompleteCount;
            private set => SetField(ref _incompleteCount, value);
        }

        private int _staleCount;
        public int StaleCount
        {
            get => _staleCount;
            private set => SetField(ref _staleCount, value);
        }

        private int _expiringCount;
        public int ExpiringCount
        {
            get => _expiringCount;
            private set => SetField(ref _expiringCount, value);
        }

        private int _expiredCount;
        public int ExpiredCount
        {
            get => _expiredCount;
            private set => SetField(ref _expiredCount, value);
        }

        public int TotalCount => _allItems.Count;

        public bool ShowIncomplete
        {
            get => AppSettings.Current.WorklistShowIncomplete;
            set
            {
                if (AppSettings.Current.WorklistShowIncomplete == value) return;
                AppSettings.Current.WorklistShowIncomplete = value;
                OnPropertyChanged();
                PersistFilters();
            }
        }

        public bool ShowStale
        {
            get => AppSettings.Current.WorklistShowStale;
            set
            {
                if (AppSettings.Current.WorklistShowStale == value) return;
                AppSettings.Current.WorklistShowStale = value;
                OnPropertyChanged();
                PersistFilters();
            }
        }

        public bool ShowExpiring
        {
            get => AppSettings.Current.WorklistShowExpiring;
            set
            {
                if (AppSettings.Current.WorklistShowExpiring == value) return;
                AppSettings.Current.WorklistShowExpiring = value;
                OnPropertyChanged();
                PersistFilters();
            }
        }

        public bool ShowExpired
        {
            get => AppSettings.Current.WorklistShowExpired;
            set
            {
                if (AppSettings.Current.WorklistShowExpired == value) return;
                AppSettings.Current.WorklistShowExpired = value;
                OnPropertyChanged();
                PersistFilters();
            }
        }

        private string _listFilter = string.Empty;
        public string ListFilter
        {
            get => _listFilter;
            set { if (SetField(ref _listFilter, value)) ApplyFilter(); }
        }

        public ICommand RefreshCommand { get; }
        public ICommand OpenCommand { get; }
        public ICommand ShowAllCommand { get; }
        public ICommand IsolateKindCommand { get; }

        public WorklistViewModel(DatabaseService db, MainViewModel main)
        {
            _db = db;
            _main = main;
            RefreshCommand = new RelayCommand(Refresh);
            OpenCommand = new RelayCommand(OpenSelected, () => SelectedItem != null);
            ShowAllCommand = new RelayCommand(ShowAllCategories);
            IsolateKindCommand = new RelayCommand(IsolateOrRestoreKind);
            Refresh();
        }

        public void Refresh()
        {
            var title = SelectedItem?.Title;
            var kind = SelectedItem?.Kind;
            _allItems = _db.GetWorklistItems(AppSettings.Current.ExpirationWarningDays);
            IncompleteCount = _allItems.Count(i => i.Kind == WorklistKind.IncompleteReactions);
            StaleCount = _allItems.Count(i => i.Kind == WorklistKind.StaleAnalysis);
            ExpiringCount = _allItems.Count(i =>
                i.Kind is WorklistKind.ExpiringSpecimen or WorklistKind.ExpiringPanel);
            ExpiredCount = _allItems.Count(i =>
                i.Kind is WorklistKind.ExpiredSpecimen or WorklistKind.ExpiredPanel);
            OnPropertyChanged(nameof(TotalCount));
            NotifyFilterProperties();
            ApplyFilter(title, kind);
        }

        private void ApplyFilter(string? title = null, WorklistKind? kind = null)
        {
            title ??= SelectedItem?.Title;
            kind ??= SelectedItem?.Kind;
            var settings = AppSettings.Current;
            Items.Clear();
            foreach (var item in _allItems.Where(i =>
                         settings.ShowsWorklistKind(i.Kind) && i.MatchesFilter(_listFilter)))
                Items.Add(item);
            SelectedItem = Items.FirstOrDefault(i => i.Title == title && i.Kind == kind)
                ?? Items.FirstOrDefault();

            if (_allItems.Count == 0)
                _main.SetStatus("Worklist is clear.");
            else if (Items.Count == 0)
                _main.SetStatus("No worklist items match the selected filters.");
            else if (Items.Count == _allItems.Count)
                _main.SetStatus($"Worklist: {Items.Count} item(s) need attention.");
            else
                _main.SetStatus($"Worklist: {Items.Count} of {_allItems.Count} item(s) shown.");
        }

        private void PersistFilters()
        {
            SettingsService.Save();
            ApplyFilter();
        }

        private void ShowAllCategories()
        {
            var s = AppSettings.Current;
            if (s.WorklistShowIncomplete && s.WorklistShowStale &&
                s.WorklistShowExpiring && s.WorklistShowExpired)
            {
                ApplyFilter();
                return;
            }

            s.WorklistShowIncomplete = true;
            s.WorklistShowStale = true;
            s.WorklistShowExpiring = true;
            s.WorklistShowExpired = true;
            NotifyFilterProperties();
            PersistFilters();
        }

        private void IsolateOrRestoreKind(object? parameter)
        {
            var key = parameter as string;
            if (string.IsNullOrEmpty(key)) return;
            var s = AppSettings.Current;
            bool onlyThis = key switch
            {
                "Incomplete" => s.WorklistShowIncomplete && !s.WorklistShowStale &&
                                !s.WorklistShowExpiring && !s.WorklistShowExpired,
                "Stale" => s.WorklistShowStale && !s.WorklistShowIncomplete &&
                           !s.WorklistShowExpiring && !s.WorklistShowExpired,
                "Expiring" => s.WorklistShowExpiring && !s.WorklistShowIncomplete &&
                              !s.WorklistShowStale && !s.WorklistShowExpired,
                "Expired" => s.WorklistShowExpired && !s.WorklistShowIncomplete &&
                             !s.WorklistShowStale && !s.WorklistShowExpiring,
                _ => false
            };

            if (onlyThis)
            {
                s.WorklistShowIncomplete = true;
                s.WorklistShowStale = true;
                s.WorklistShowExpiring = true;
                s.WorklistShowExpired = true;
            }
            else
            {
                s.WorklistShowIncomplete = key == "Incomplete";
                s.WorklistShowStale = key == "Stale";
                s.WorklistShowExpiring = key == "Expiring";
                s.WorklistShowExpired = key == "Expired";
            }

            NotifyFilterProperties();
            PersistFilters();
        }

        private void NotifyFilterProperties()
        {
            OnPropertyChanged(nameof(ShowIncomplete));
            OnPropertyChanged(nameof(ShowStale));
            OnPropertyChanged(nameof(ShowExpiring));
            OnPropertyChanged(nameof(ShowExpired));
        }

        private void OpenSelected()
        {
            if (SelectedItem == null) return;
            _main.NavigateToWorklistItem(SelectedItem);
        }
    }
}
