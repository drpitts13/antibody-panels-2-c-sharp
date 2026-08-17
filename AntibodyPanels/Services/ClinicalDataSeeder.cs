using System;
using System.Collections.Generic;
using AntibodyPanels.Data;
using AntibodyPanels.Models;

namespace AntibodyPanels.Services
{
    /// <summary>
    /// Seeds a realistic blood-bank workload: 5 shared panels, 10 specimens,
    /// and about 10 result sets including ficin (enzyme) and allogeneic absorption runs.
    /// Idempotent — skips if specimen 2026-001 already exists.
    /// </summary>
    public static class ClinicalDataSeeder
    {
        public const string MarkerAccession = "2026-001";

        public static readonly string[] SpecimenIds =
        {
            "2026-001", "2026-002", "2026-003", "2026-004", "2026-005",
            "2026-006", "2026-007", "2026-008", "2026-009", "2026-010"
        };

        public static void SeedIfNeeded(DatabaseService db, AntibodyAnalyzer analyzer)
        {
            if (db.GetSpecimen(MarkerAccession) != null) return;
            Seed(db, analyzer);
        }

        public static void Seed(DatabaseService db, AntibodyAnalyzer analyzer)
        {
            if (db.GetSpecimen(MarkerAccession) != null) return;

            var panels = SeedPanels(db);
            SeedSpecimensAndResults(db, panels);

            foreach (var id in SpecimenIds)
                analyzer.AnalyzeSpecimen(id, updateDb: true);
        }

        private static int[] SeedPanels(DatabaseService db)
        {
            return new[]
            {
                CreatePanel(db, "Ortho Resolve Panel A", "ORT2026A", "Ortho Clinical Diagnostics",
                    11, DaysFromNow(180), includeAc: true, Panel1Cells),
                CreatePanel(db, "Immucor CAPTURE-R Ready-ID", "IMM2026B", "Immucor",
                    10, DaysFromNow(200), includeAc: false, Panel2Cells),
                CreatePanel(db, "Bio-Rad ID-DiaPanel", "BIO2026C", "Bio-Rad",
                    11, DaysFromNow(220), includeAc: true, Panel3Cells),
                CreatePanel(db, "Quotient BioResearch MicroTyping", "QUO2026D", "Quotient",
                    8, DaysFromNow(150), includeAc: false, Panel4Cells),
                CreatePanel(db, "Grifols DG Gel ID-Panels", "GRI2026E", "Grifols",
                    11, DaysFromNow(190), includeAc: true, Panel5Cells),
            };
        }

        private static void SeedSpecimensAndResults(DatabaseService db, int[] panels)
        {
            int p1 = panels[0], p2 = panels[1], p3 = panels[2], p4 = panels[3], p5 = panels[4];

            // 1. Anti-E — untreated panel A
            AddSpecimen(db, "2026-001", "serum", 30, p1);
            var r001 = db.GetOrCreateDefaultRun("2026-001", p1);
            SaveByAntigen(db, r001, p1, "E", ahg: "2+");

            // 2. Anti-Fya — untreated + ficin (Fya destroyed on enzyme cells)
            AddSpecimen(db, "2026-002", "plasma", 45, p1);
            var r002u = db.GetOrCreateDefaultRun("2026-002", p1);
            SaveByAntigen(db, r002u, p1, "Fya", ahg: "2+");
            var r002e = db.AddPanelRun("2026-002", p1, CellTreatment.Ficin, SerumTreatment.None, "Ficin panel");
            SaveAllNegative(db, r002e, p1);

            // 3. Anti-K — untreated panel B
            AddSpecimen(db, "2026-003", "serum", 60, p2);
            var r003 = db.GetOrCreateDefaultRun("2026-003", p2);
            SaveByAntigen(db, r003, p2, "K", ahg: "2+");

            // 4. Warm auto + underlying allo anti-c — untreated panreactive + rr absorption
            AddSpecimen(db, "2026-004", "serum", 30, p2);
            var r004u = db.GetOrCreateDefaultRun("2026-004", p2);
            SavePanreactive(db, r004u, p2, c37: "2+", ahg: "3+");
            var r004a = db.AddPanelRun("2026-004", p2, CellTreatment.None, SerumTreatment.AlloAdsorptionRr, "Absorbed: rr");
            SaveByAntigen(db, r004a, p2, "c", ahg: "2+", isPhase: "NT", c37: "0");

            // 5. Cold anti-M — IS-only, then ficin (M destroyed)
            AddSpecimen(db, "2026-005", "plasma", 40, p3);
            var r005u = db.GetOrCreateDefaultRun("2026-005", p3);
            SaveByAntigen(db, r005u, p3, "M", ahg: "0", isPhase: "2+", c37: "0", ccWhenNeg: "2+");
            var r005e = db.AddPanelRun("2026-005", p3, CellTreatment.Ficin, SerumTreatment.None, "Ficin panel");
            SaveAllNegative(db, r005e, p3);

            // 6. Anti-K + anti-Jka — untreated panel C
            AddSpecimen(db, "2026-006", "serum", 50, p3);
            var r006 = db.GetOrCreateDefaultRun("2026-006", p3);
            SaveByAnyAntigen(db, r006, p3, new[] { "K", "Jka" }, ahg: "2+");

            // 7. Anti-e — untreated panel D
            AddSpecimen(db, "2026-007", "eluate", 20, p4);
            var r007 = db.GetOrCreateDefaultRun("2026-007", p4);
            SaveByAntigen(db, r007, p4, "e", ahg: "2+");

            // 8. No antibody — all-negative panel D
            AddSpecimen(db, "2026-008", "serum", 90, p4);
            var r008 = db.GetOrCreateDefaultRun("2026-008", p4);
            SaveAllNegative(db, r008, p4);

            // 9. Anti-D — untreated panel E
            AddSpecimen(db, "2026-009", "serum", 35, p5);
            var r009 = db.GetOrCreateDefaultRun("2026-009", p5);
            SaveByAntigen(db, r009, p5, "D", ahg: "3+");

            // 10. Anti-Jka — untreated + ficin (Kidd enhanced by enzyme)
            AddSpecimen(db, "2026-010", "plasma", 45, p5);
            var r010u = db.GetOrCreateDefaultRun("2026-010", p5);
            SaveByAntigen(db, r010u, p5, "Jka", ahg: "1+");
            var r010e = db.AddPanelRun("2026-010", p5, CellTreatment.Ficin, SerumTreatment.None, "Ficin panel");
            SaveByAntigen(db, r010e, p5, "Jka", ahg: "3+");
        }

        // ── Reaction helpers ──────────────────────────────────────────────────

        private static void SaveByAntigen(DatabaseService db, int runId, int panelId, string antigen,
            string ahg, string isPhase = "NT", string c37 = "NT", string ccWhenNeg = "2+")
        {
            foreach (var cell in db.GetPanelCells(panelId))
            {
                if (cell.CellNumber == "AC")
                {
                    db.SaveReaction(runId, cell.CellNumber, "NT", "NT", "0", "0");
                    continue;
                }

                if (cell.GetAntigen(antigen) == "+")
                    db.SaveReaction(runId, cell.CellNumber, isPhase, c37, ahg, ahg == "0" ? ccWhenNeg : "NT");
                else
                    db.SaveReaction(runId, cell.CellNumber, "0", "0", "0", ccWhenNeg);
            }
        }

        private static void SaveByAnyAntigen(DatabaseService db, int runId, int panelId,
            IReadOnlyList<string> antigens, string ahg)
        {
            foreach (var cell in db.GetPanelCells(panelId))
            {
                if (cell.CellNumber == "AC")
                {
                    db.SaveReaction(runId, cell.CellNumber, "NT", "NT", "0", "0");
                    continue;
                }

                var positive = false;
                foreach (var ag in antigens)
                {
                    if (cell.GetAntigen(ag) == "+") { positive = true; break; }
                }

                if (positive)
                    db.SaveReaction(runId, cell.CellNumber, "NT", "NT", ahg, "NT");
                else
                    db.SaveReaction(runId, cell.CellNumber, "0", "0", "0", "2+");
            }
        }

        private static void SavePanreactive(DatabaseService db, int runId, int panelId, string c37, string ahg)
        {
            foreach (var cell in db.GetPanelCells(panelId))
            {
                if (cell.CellNumber == "AC")
                    db.SaveReaction(runId, cell.CellNumber, "NT", "NT", "0", "0");
                else
                    db.SaveReaction(runId, cell.CellNumber, "NT", c37, ahg, "NT");
            }
        }

        private static void SaveAllNegative(DatabaseService db, int runId, int panelId)
        {
            foreach (var cell in db.GetPanelCells(panelId))
            {
                if (cell.CellNumber == "AC")
                    db.SaveReaction(runId, cell.CellNumber, "NT", "NT", "0", "0");
                else
                    db.SaveReaction(runId, cell.CellNumber, "0", "0", "0", "2+");
            }
        }

        private static void AddSpecimen(DatabaseService db, string accession, string type, int days, int panelId)
        {
            db.AddSpecimen(accession, type, DaysFromNow(days));
            db.LinkSpecimenPanel(accession, panelId);
        }

        private static int CreatePanel(DatabaseService db, string name, string lot, string vendor,
            int numCells, string expiration, bool includeAc, string[][] profiles)
        {
            var panelId = db.AddPanel(name, lot, vendor, numCells, expiration, includeAc, 1);
            var cells = db.GetPanelCells(panelId);
            for (int i = 0; i < profiles.Length && i < cells.Count; i++)
            {
                if (cells[i].CellNumber == "AC") continue;
                var profile = profiles[i];
                for (int j = 0; j < AntigenConstants.Antigens.Count && j < profile.Length; j++)
                    db.UpdatePanelCellAntigen(cells[i].Id, AntigenConstants.Antigens[j], profile[j]);
            }
            return panelId;
        }

        private static string DaysFromNow(int days) =>
            DateTime.Now.AddDays(days).ToString("yyyy-MM-dd");

        // Antigen order: D,C,c,E,e,f,Cw,V,K,k,Kpa,Kpb,Jsa,Jsb,Jka,Jkb,Fya,Fyb,Lea,Leb,M,N,S,s,Lua,Lub,Xga,P1
        private static string[] Row(params string[] values) => values;

        private static readonly string[][] Panel1Cells =
        {
            Row("+","+","-","-","+","+","-","-","-","+","-","+","-","+","+","+","+","-","-","+","+","+","+","-","-","+","+","+"),
            Row("+","-","+","+","-","-","-","-","-","+","-","+","-","+","+","-","+","+","-","+","+","-","-","+","-","+","-","+"),
            Row("-","-","+","-","+","+","-","+","+","+","-","+","-","+","+","+","-","+","+","-","-","+","-","+","-","+","+","-"),
            Row("+","+","-","+","-","-","+","-","-","+","-","+","-","+","+","+","+","-","-","+","+","+","+","-","-","+","-","+"),
            Row("+","-","+","-","+","+","-","-","-","+","-","+","-","+","-","+","+","+","+","-","+","-","+","-","-","+","+","+"),
            Row("-","-","+","+","-","-","-","+","-","+","-","+","+","-","+","-","-","+","-","+","-","+","+","-","-","+","+","-"),
            Row("+","+","+","-","+","+","-","-","+","-","+","-","-","+","+","+","+","-","+","-","+","+","-","+","-","+","-","+"),
            Row("-","-","+","-","+","+","-","-","-","+","-","+","-","+","+","+","+","+","-","+","+","+","+","+","-","+","+","+"),
            Row("+","-","+","+","+","+","-","-","-","+","-","+","-","+","+","+","-","+","+","-","-","+","-","+","-","+","+","-"),
            Row("-","+","-","+","-","-","-","-","-","+","-","+","-","+","-","+","+","-","-","+","+","-","+","-","-","+","-","+"),
            Row("+","+","-","-","+","+","-","+","-","+","-","+","-","+","+","-","+","+","+","-","+","+","+","-","-","+","+","+"),
        };

        private static readonly string[][] Panel2Cells =
        {
            Row("-","-","+","+","-","-","-","+","-","+","-","+","-","+","+","-","-","+","+","-","-","+","-","+","-","+","+","+"),
            Row("+","+","-","+","-","-","-","-","-","+","-","+","-","+","-","+","+","-","-","+","+","-","+","-","-","+","-","+"),
            Row("+","-","+","-","+","+","-","-","+","+","-","+","-","+","+","+","+","-","+","-","+","+","-","+","-","+","+","-"),
            Row("-","-","+","-","+","+","+","-","-","+","-","+","-","+","+","+","-","+","-","+","+","+","+","-","-","+","+","+"),
            Row("+","+","+","+","-","-","-","-","-","+","-","+","-","+","+","-","+","+","-","+","-","+","+","-","-","+","-","+"),
            Row("+","-","+","-","+","+","-","+","-","+","-","+","-","+","-","+","+","+","+","-","+","-","-","+","-","+","+","+"),
            Row("-","+","-","+","+","+","-","-","-","+","+","-","-","+","+","+","+","-","-","+","+","+","+","+","-","+","-","-"),
            Row("+","+","-","-","+","+","-","+","+","-","-","+","-","+","+","+","-","+","+","-","+","+","+","-","-","+","+","+"),
            Row("-","-","+","+","+","+","-","-","-","+","-","+","-","+","+","-","+","+","-","+","-","+","-","+","-","+","+","-"),
            Row("+","-","+","+","-","-","-","-","-","+","-","+","+","-","-","+","+","-","+","-","+","-","+","-","-","+","-","+"),
        };

        private static readonly string[][] Panel3Cells =
        {
            Row("+","-","+","+","+","+","-","-","-","+","-","+","-","+","+","+","-","+","-","+","+","-","+","-","-","+","+","+"),
            Row("-","+","-","-","+","+","+","-","-","+","-","+","-","+","+","+","+","-","+","-","-","+","-","+","-","+","-","+"),
            Row("+","+","-","+","-","-","-","-","+","-","-","+","-","+","-","+","+","+","-","+","+","+","+","-","-","+","+","-"),
            Row("-","-","+","+","-","-","-","+","-","+","-","+","-","+","+","-","-","+","+","-","+","-","-","+","-","+","+","+"),
            Row("+","+","+","-","+","+","-","-","-","+","-","+","-","+","+","+","+","+","+","-","-","+","+","+","-","+","-","+"),
            Row("+","-","+","-","+","+","-","+","-","+","+","-","-","+","+","-","+","-","-","+","+","+","-","+","-","+","+","-"),
            Row("-","-","+","+","+","+","-","-","+","+","-","+","-","+","-","+","+","+","+","-","+","-","+","-","-","+","+","+"),
            Row("+","+","-","+","-","-","-","-","-","+","-","+","-","+","+","+","-","+","-","+","-","+","+","-","-","+","-","+"),
            Row("-","+","+","-","+","+","+","-","-","+","-","+","+","-","+","+","+","-","+","-","+","+","+","+","-","+","+","-"),
            Row("+","-","+","+","-","-","-","-","-","+","-","+","-","+","+","-","+","+","-","+","+","-","-","+","-","+","+","+"),
            Row("-","-","+","-","+","+","-","+","-","+","-","+","-","+","-","+","-","+","+","-","-","+","-","+","-","+","-","+"),
        };

        private static readonly string[][] Panel4Cells =
        {
            Row("+","+","-","-","+","+","-","-","-","+","-","+","-","+","+","+","+","-","-","+","+","+","+","-","-","+","+","+"),
            Row("-","-","+","+","-","-","-","+","-","+","-","+","-","+","-","+","-","+","+","-","-","+","-","+","-","+","+","-"),
            Row("+","-","+","+","+","+","-","-","-","+","-","+","-","+","+","-","+","+","-","+","+","-","+","-","-","+","-","+"),
            Row("-","+","-","+","-","-","+","-","+","-","-","+","-","+","+","+","+","-","+","-","+","+","+","-","-","+","+","+"),
            Row("+","+","+","-","+","+","-","-","-","+","-","+","-","+","-","+","-","+","+","-","-","+","-","+","-","+","-","+"),
            Row("-","-","+","-","+","+","-","+","-","+","-","+","-","+","+","+","+","+","-","+","+","+","+","+","-","+","+","+"),
            Row("+","-","+","+","-","-","-","-","+","+","+","-","+","-","+","+","-","+","+","-","+","-","-","+","-","+","+","-"),
            Row("-","+","-","-","+","+","-","-","-","+","-","+","-","+","+","-","+","-","-","+","+","-","+","-","-","+","-","+"),
        };

        private static readonly string[][] Panel5Cells =
        {
            Row("+","+","+","-","+","+","-","-","-","+","-","+","-","+","+","+","+","+","+","-","+","+","-","+","-","+","+","-"),
            Row("-","-","+","+","+","+","-","+","-","+","-","+","-","+","-","+","-","+","-","+","-","+","+","-","-","+","+","+"),
            Row("+","-","+","-","+","+","-","-","+","+","-","+","-","+","+","-","+","-","+","-","+","-","+","+","-","+","-","+"),
            Row("-","+","-","+","-","-","+","-","-","+","-","+","-","+","+","+","+","+","-","+","+","+","-","+","-","+","+","+"),
            Row("+","+","-","+","-","-","-","-","-","+","-","+","+","-","-","+","-","+","+","-","+","-","+","-","-","+","-","-"),
            Row("-","-","+","-","+","+","-","+","-","+","+","-","-","+","+","+","+","-","+","-","-","+","-","+","-","+","+","+"),
            Row("+","-","+","+","+","+","-","-","-","+","-","+","-","+","+","-","+","+","-","+","+","+","+","-","-","+","+","+"),
            Row("-","+","+","+","-","-","-","-","+","-","-","+","-","+","+","+","-","+","+","-","-","+","+","+","-","+","-","-"),
            Row("+","+","-","-","+","+","-","+","-","+","-","+","-","+","-","+","+","-","-","+","+","-","-","+","-","+","+","+"),
            Row("-","-","+","+","-","-","-","-","-","+","-","+","-","+","+","+","+","+","+","-","+","+","+","-","-","+","+","-"),
            Row("+","-","+","+","+","+","+","-","-","+","-","+","-","+","+","-","-","+","-","+","-","+","-","+","-","+","-","+"),
        };
    }
}
