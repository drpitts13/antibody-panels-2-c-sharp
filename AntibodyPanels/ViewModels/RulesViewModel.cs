using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using AntibodyPanels.Data;
using AntibodyPanels.Models;

namespace AntibodyPanels.ViewModels
{
    public class RulesViewModel : BaseViewModel
    {
        private readonly DatabaseService _db;
        private readonly MainViewModel _main;

        public ObservableCollection<Rule> Rules { get; } = new();

        private Rule? _selectedRule;
        public Rule? SelectedRule
        {
            get => _selectedRule;
            set => SetField(ref _selectedRule, value);
        }

        public ICommand AddCommand { get; }
        public ICommand EditCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand RefreshCommand { get; }

        public RulesViewModel(DatabaseService db, MainViewModel main)
        {
            _db = db;
            _main = main;
            AddCommand = new RelayCommand(AddRule);
            EditCommand = new RelayCommand(EditRule, () => SelectedRule != null);
            DeleteCommand = new RelayCommand(DeleteRule, () => SelectedRule != null);
            RefreshCommand = new RelayCommand(Refresh);
            Refresh();
        }

        public void Refresh()
        {
            var sid = SelectedRule?.RuleId;
            Rules.Clear();
            foreach (var r in _db.GetAllRules()) Rules.Add(r);
            SelectedRule = Rules.FirstOrDefault(r => r.RuleId == sid) ?? Rules.FirstOrDefault();
        }

        private void AddRule()
        {
            var dlg = new Views.Dialogs.RuleDialog();
            if (dlg.ShowDialog() != true) return;
            var id = _db.AddRule(dlg.RuleName, dlg.Description, dlg.Antibody,
                dlg.ExceptionAntigen, dlg.HeterozygousOk, dlg.MinRuleoutCount);
            _main.SetStatus($"Rule '{dlg.RuleName}' added.");
            Refresh();
            SelectedRule = Rules.FirstOrDefault(r => r.RuleId == id);
        }

        private void EditRule()
        {
            if (SelectedRule == null) return;
            var dlg = new Views.Dialogs.RuleDialog(SelectedRule);
            if (dlg.ShowDialog() != true) return;
            _db.UpdateRule(SelectedRule.RuleId, dlg.RuleName, dlg.Description, dlg.Antibody,
                dlg.ExceptionAntigen, dlg.HeterozygousOk, dlg.MinRuleoutCount);
            _main.SetStatus($"Rule '{dlg.RuleName}' updated.");
            Refresh();
        }

        private void DeleteRule()
        {
            if (SelectedRule == null) return;
            if (MessageBox.Show($"Delete rule '{SelectedRule.Name}'?",
                "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            _db.DeleteRule(SelectedRule.RuleId);
            _main.SetStatus("Rule deleted.");
            Refresh();
        }
    }
}
