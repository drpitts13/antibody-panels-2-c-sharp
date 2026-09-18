using System.IO;
using System.Text;
using System.Windows;
using AntibodyPanels.Data;
using Microsoft.Win32;

namespace AntibodyPanels.Views.Dialogs
{
    public partial class AuditLogDialog : Window
    {
        private readonly DatabaseService _db;

        public AuditLogDialog(DatabaseService db)
        {
            InitializeComponent();
            _db = db;
            EventsGrid.ItemsSource = _db.GetAuditEvents();
        }

        private void ExportClick(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv",
                FileName = "audit-log.csv"
            };
            if (dlg.ShowDialog() != true) return;

            var sb = new StringBuilder();
            sb.AppendLine("OccurredAtUtc,Operator,Action,EntityType,EntityId,Reason,BeforeJson,AfterJson");
            foreach (var ev in _db.GetAuditEvents(5000))
            {
                sb.AppendLine(string.Join(",",
                    Csv(ev.OccurredAtUtc), Csv(ev.Operator), Csv(ev.Action),
                    Csv(ev.EntityType), Csv(ev.EntityId), Csv(ev.Reason),
                    Csv(ev.BeforeJson), Csv(ev.AfterJson)));
            }
            File.WriteAllText(dlg.FileName, sb.ToString());
        }

        private static string Csv(string? value)
        {
            var t = value ?? "";
            return "\"" + t.Replace("\"", "\"\"") + "\"";
        }
    }
}
