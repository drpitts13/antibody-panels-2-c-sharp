using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using AntibodyPanels.Models;

namespace AntibodyPanels.Views.Dialogs
{
    public partial class PanelRunDialog : Window
    {
        private static readonly Dictionary<string, CellTreatment> CellTreatmentOptions = new()
        {
            { "Untreated (None)", CellTreatment.None },
            { "Ficin-treated",    CellTreatment.Ficin  },
            { "Papain-treated",   CellTreatment.Papain },
            { "DTT-treated",      CellTreatment.DTT    },
        };

        private static readonly Dictionary<string, SerumTreatment> SerumTreatmentOptions = new()
        {
            { "None",                    SerumTreatment.None               },
            { "Prewarmed serum",         SerumTreatment.Prewarmed           },
            { "Absorbed: R1R1 (DCe/DCe)", SerumTreatment.AlloAdsorptionR1R1  },
            { "Absorbed: R2R2 (DcE/DcE)", SerumTreatment.AlloAdsorptionR2R2  },
            { "Absorbed: rr (ce/ce)",    SerumTreatment.AlloAdsorptionRr    },
            { "Autoadsorption",          SerumTreatment.AutoAdsorption       },
        };

        public CellTreatment SelectedCellTreatment =>
            CellTreatmentOptions.TryGetValue(CellTreatmentBox.SelectedItem?.ToString() ?? "", out var ct)
                ? ct : CellTreatment.None;

        public SerumTreatment SelectedSerumTreatment =>
            SerumTreatmentOptions.TryGetValue(SerumTreatmentBox.SelectedItem?.ToString() ?? "", out var st)
                ? st : SerumTreatment.None;

        public string RunLabel => LabelBox.Text.Trim();

        public PanelRunDialog()
        {
            InitializeComponent();

            CellTreatmentBox.ItemsSource = CellTreatmentOptions.Keys.ToList();
            CellTreatmentBox.SelectedIndex = 0;
            SerumTreatmentBox.ItemsSource = SerumTreatmentOptions.Keys.ToList();
            SerumTreatmentBox.SelectedIndex = 0;

            CellTreatmentBox.SelectionChanged += (_, _) => UpdateInfoText();
            SerumTreatmentBox.SelectionChanged += (_, _) => UpdateInfoText();
            UpdateInfoText();
        }

        private void UpdateInfoText()
        {
            var ct = SelectedCellTreatment;
            var st = SelectedSerumTreatment;

            var lines = new List<string>();

            if (ct == CellTreatment.Ficin || ct == CellTreatment.Papain)
                lines.Add("Cells: destroys M, N, S, s, Fya, Fyb, Xga, Lea, Leb.  Enhances Rh, Kidd, P1.");
            else if (ct == CellTreatment.DTT)
                lines.Add("Cells: destroys K, k, Kpa, Kpb, Jsa, Jsb, Lua, Lub.");

            if (st == SerumTreatment.Prewarmed)
                lines.Add("Serum: IS phase not interpretable (cold/IgM reactors suppressed).");
            else if (st == SerumTreatment.AlloAdsorptionR1R1)
                lines.Add("Serum: absorbed with R1R1 — removes anti-D, anti-C, anti-e.");
            else if (st == SerumTreatment.AlloAdsorptionR2R2)
                lines.Add("Serum: absorbed with R2R2 — removes anti-D, anti-c, anti-E.");
            else if (st == SerumTreatment.AlloAdsorptionRr)
                lines.Add("Serum: absorbed with rr — removes anti-c, anti-e.");
            else if (st == SerumTreatment.AutoAdsorption)
                lines.Add("Serum: autoadsorbed — autoantibody removed, alloantibodies retained.");

            InfoText.Text = string.Join("\n", lines);
        }

        private void SaveClick(object sender, RoutedEventArgs e) => DialogResult = true;
    }
}
