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

        public ObservableCollection<WorklistItem> Items { get; } = new();

        private WorklistItem? _selectedItem;
        public WorklistItem? SelectedItem
        {
            get => _selectedItem;
            set => SetField(ref _selectedItem, value);
        }

        public ICommand RefreshCommand { get; }
        public ICommand OpenCommand { get; }

        public WorklistViewModel(DatabaseService db, MainViewModel main)
        {
            _db = db;
            _main = main;
            RefreshCommand = new RelayCommand(Refresh);
            OpenCommand = new RelayCommand(OpenSelected, () => SelectedItem != null);
            Refresh();
        }

        public void Refresh()
        {
            var title = SelectedItem?.Title;
            var kind = SelectedItem?.Kind;
            Items.Clear();
            foreach (var item in _db.GetWorklistItems(AppSettings.Current.ExpirationWarningDays))
                Items.Add(item);
            SelectedItem = Items.FirstOrDefault(i => i.Title == title && i.Kind == kind)
                ?? Items.FirstOrDefault();
            _main.SetStatus(Items.Count == 0
                ? "Worklist is clear."
                : $"Worklist: {Items.Count} item(s) need attention.");
        }

        private void OpenSelected()
        {
            if (SelectedItem == null) return;
            _main.NavigateToWorklistItem(SelectedItem);
        }
    }
}
