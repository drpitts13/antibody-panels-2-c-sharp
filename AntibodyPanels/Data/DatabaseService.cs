using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using AntibodyPanels.Models;

namespace AntibodyPanels.Data
{
    /// <summary>
    /// Full CRUD layer over antibody_panels.db (mirrors database.py).
    /// </summary>
    public class DatabaseService : IDisposable
    {
        private readonly SqliteConnection _conn;

        public string DbPath { get; }

        public DatabaseService(string? dbPath = null)
        {
            DbPath = dbPath ?? DefaultDbPath();
            _conn = new SqliteConnection($"Data Source={DbPath};Pooling=False");
            _conn.Open();
            EnableForeignKeys();
            CreateTables();
            MigrateSpecimenTimestamps();
            MigratePanelStartCell();
            MigrateActiveFlag();
            MigrateReactionsToRuns();
            MigrateSpecimenClinical();
            MigrateSpecimenFinalCall();
            MigrateWarehouseAntigens();
            DeactivateExpiredSpecimens();
            DeactivateExpiredPanels();
        }

        private static string DefaultDbPath()
        {
            var envPath = Environment.GetEnvironmentVariable("ANTIBODY_PANELS_DB");
            if (!string.IsNullOrEmpty(envPath))
                return envPath;

            // Prefer an existing database beside the executable (dev / portable runs).
            var exeDir = AppDomain.CurrentDomain.BaseDirectory;
            var local = Path.Combine(exeDir, "antibody_panels.db");
            if (File.Exists(local))
                return local;

            // Installed / first-run: store user data in AppData (Program Files is not writable).
            var appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AntibodyPanels");
            Directory.CreateDirectory(appData);
            return Path.Combine(appData, "antibody_panels.db");
        }

        private void EnableForeignKeys()
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "PRAGMA foreign_keys = ON;";
            cmd.ExecuteNonQuery();
        }

        // ── Schema ──────────────────────────────────────────────────────────

        private void CreateTables()
        {
            var antigenCols = string.Join(", ",
                GetAllAntigenColumns(col => $"{col} TEXT DEFAULT '-'"));

            ExecNonQuery(@"
                CREATE TABLE IF NOT EXISTS specimens (
                    accession_number TEXT PRIMARY KEY,
                    type TEXT NOT NULL DEFAULT 'serum',
                    expiration_date TEXT,
                    created_date TEXT NOT NULL
                )");

            ExecNonQuery(@"
                CREATE TABLE IF NOT EXISTS specimen_antibodies (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    specimen_id TEXT NOT NULL,
                    antibody TEXT NOT NULL,
                    probability REAL NOT NULL,
                    FOREIGN KEY (specimen_id) REFERENCES specimens(accession_number) ON DELETE CASCADE
                )");

            ExecNonQuery(@"
                CREATE TABLE IF NOT EXISTS specimen_ruleouts (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    specimen_id TEXT NOT NULL,
                    antibody TEXT NOT NULL,
                    ruleout_count INTEGER NOT NULL DEFAULT 0,
                    FOREIGN KEY (specimen_id) REFERENCES specimens(accession_number) ON DELETE CASCADE
                )");

            ExecNonQuery(@"
                CREATE TABLE IF NOT EXISTS panels (
                    panel_id INTEGER PRIMARY KEY AUTOINCREMENT,
                    name TEXT NOT NULL,
                    lot_number TEXT,
                    vendor TEXT,
                    num_cells INTEGER NOT NULL,
                    expiration_date TEXT,
                    include_ac INTEGER DEFAULT 0
                )");

            ExecNonQuery($@"
                CREATE TABLE IF NOT EXISTS panel_cells (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    panel_id INTEGER NOT NULL,
                    cell_number TEXT NOT NULL,
                    {antigenCols},
                    FOREIGN KEY (panel_id) REFERENCES panels(panel_id) ON DELETE CASCADE
                )");

            ExecNonQuery(@"
                CREATE TABLE IF NOT EXISTS specimen_panels (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    specimen_id TEXT NOT NULL,
                    panel_id INTEGER NOT NULL,
                    FOREIGN KEY (specimen_id) REFERENCES specimens(accession_number) ON DELETE CASCADE,
                    FOREIGN KEY (panel_id) REFERENCES panels(panel_id) ON DELETE CASCADE,
                    UNIQUE(specimen_id, panel_id)
                )");

            // panel_runs: one row per (specimen, panel, cell-treatment, serum-treatment) combination.
            // This replaces the old implicit single-run assumption in the reactions table.
            ExecNonQuery(@"
                CREATE TABLE IF NOT EXISTS panel_runs (
                    run_id INTEGER PRIMARY KEY AUTOINCREMENT,
                    specimen_id TEXT NOT NULL,
                    panel_id INTEGER NOT NULL,
                    cell_treatment TEXT NOT NULL DEFAULT 'None',
                    serum_treatment TEXT NOT NULL DEFAULT 'None',
                    label TEXT,
                    created_date TEXT NOT NULL,
                    FOREIGN KEY (specimen_id) REFERENCES specimens(accession_number) ON DELETE CASCADE,
                    FOREIGN KEY (panel_id) REFERENCES panels(panel_id) ON DELETE CASCADE,
                    UNIQUE(specimen_id, panel_id, cell_treatment, serum_treatment)
                )");

            // New reactions schema keyed on run_id instead of (specimen_id, panel_id).
            // MigrateReactionsToRuns handles upgrading existing databases.
            ExecNonQuery(@"
                CREATE TABLE IF NOT EXISTS reactions (
                    reaction_id INTEGER PRIMARY KEY AUTOINCREMENT,
                    run_id INTEGER NOT NULL,
                    cell_number TEXT NOT NULL,
                    ""IS"" TEXT DEFAULT 'NT',
                    C37 TEXT DEFAULT 'NT',
                    AHG TEXT DEFAULT 'NT',
                    CC TEXT DEFAULT 'NT',
                    FOREIGN KEY (run_id) REFERENCES panel_runs(run_id) ON DELETE CASCADE,
                    UNIQUE(run_id, cell_number)
                )");

            ExecNonQuery(@"
                CREATE TABLE IF NOT EXISTS rules (
                    rule_id INTEGER PRIMARY KEY AUTOINCREMENT,
                    name TEXT NOT NULL,
                    description TEXT,
                    antibody TEXT NOT NULL,
                    exception_antigen TEXT,
                    heterozygous_ok INTEGER DEFAULT 0,
                    min_ruleout_count INTEGER DEFAULT 3
                )");
        }

        private void MigrateSpecimenTimestamps()
        {
            var cols = GetColumnNames("specimens");
            if (!cols.Contains("reactions_updated_at"))
                ExecNonQuery("ALTER TABLE specimens ADD COLUMN reactions_updated_at TEXT");
            if (!cols.Contains("last_analyzed_at"))
                ExecNonQuery("ALTER TABLE specimens ADD COLUMN last_analyzed_at TEXT");
        }

        private void MigratePanelStartCell()
        {
            var cols = GetColumnNames("panels");
            if (!cols.Contains("start_cell"))
                ExecNonQuery("ALTER TABLE panels ADD COLUMN start_cell INTEGER NOT NULL DEFAULT 1");
        }

        private void MigrateActiveFlag()
        {
            var specCols = GetColumnNames("specimens");
            if (!specCols.Contains("is_active"))
                ExecNonQuery("ALTER TABLE specimens ADD COLUMN is_active INTEGER NOT NULL DEFAULT 1");

            var panelCols = GetColumnNames("panels");
            if (!panelCols.Contains("is_active"))
                ExecNonQuery("ALTER TABLE panels ADD COLUMN is_active INTEGER NOT NULL DEFAULT 1");
        }

        private void MigrateSpecimenClinical()
        {
            var cols = GetColumnNames("specimens");
            if (!cols.Contains("notes"))
                ExecNonQuery("ALTER TABLE specimens ADD COLUMN notes TEXT");
            if (!cols.Contains("phenotype"))
                ExecNonQuery("ALTER TABLE specimens ADD COLUMN phenotype TEXT");
            if (!cols.Contains("previous_antibodies"))
                ExecNonQuery("ALTER TABLE specimens ADD COLUMN previous_antibodies TEXT");
            if (!cols.Contains("dat_result"))
                ExecNonQuery("ALTER TABLE specimens ADD COLUMN dat_result TEXT");
        }

        private void MigrateSpecimenFinalCall()
        {
            var cols = GetColumnNames("specimens");
            if (!cols.Contains("final_antibodies"))
                ExecNonQuery("ALTER TABLE specimens ADD COLUMN final_antibodies TEXT");
            if (!cols.Contains("final_comment"))
                ExecNonQuery("ALTER TABLE specimens ADD COLUMN final_comment TEXT");
            if (!cols.Contains("identified_by"))
                ExecNonQuery("ALTER TABLE specimens ADD COLUMN identified_by TEXT");
            if (!cols.Contains("identified_at"))
                ExecNonQuery("ALTER TABLE specimens ADD COLUMN identified_at TEXT");
        }

        /// <summary>
        /// Warehouse antigens are stored per-panel rather than as wide columns so
        /// existing panels are not silently typed as antigen-negative.
        /// </summary>
        private void MigrateWarehouseAntigens()
        {
            ExecNonQuery(@"
                CREATE TABLE IF NOT EXISTS panel_extra_antigens (
                    panel_id INTEGER NOT NULL,
                    antigen_name TEXT NOT NULL,
                    PRIMARY KEY (panel_id, antigen_name),
                    FOREIGN KEY (panel_id) REFERENCES panels(panel_id) ON DELETE CASCADE
                )");

            ExecNonQuery(@"
                CREATE TABLE IF NOT EXISTS panel_cell_extra_antigens (
                    cell_id INTEGER NOT NULL,
                    antigen_name TEXT NOT NULL,
                    value TEXT NOT NULL DEFAULT '-',
                    PRIMARY KEY (cell_id, antigen_name),
                    FOREIGN KEY (cell_id) REFERENCES panel_cells(id) ON DELETE CASCADE
                )");
        }

        private void DeactivateExpiredSpecimens()
        {
            var today = DateTime.Now.ToString("yyyy-MM-dd");
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE specimens SET is_active = 0
                WHERE expiration_date IS NOT NULL AND expiration_date < $today AND is_active = 1";
            cmd.Parameters.AddWithValue("$today", today);
            cmd.ExecuteNonQuery();
        }

        private void DeactivateExpiredPanels()
        {
            var today = DateTime.Now.ToString("yyyy-MM-dd");
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE panels SET is_active = 0
                WHERE expiration_date IS NOT NULL AND expiration_date < $today AND is_active = 1";
            cmd.Parameters.AddWithValue("$today", today);
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// Upgrades existing databases that used the old reactions schema
        /// (specimen_id, panel_id, cell_number unique) to the new panel_runs-based schema.
        /// Safe to call on already-migrated databases (no-op when run_id column exists).
        /// </summary>
        private void MigrateReactionsToRuns()
        {
            var reactionCols = GetColumnNames("reactions");
            if (reactionCols.Contains("run_id")) return; // already migrated

            // The old reactions table has (specimen_id, panel_id, cell_number) columns.
            // We need to:
            //   1. Create panel_runs rows for every unique (specimen_id, panel_id) pair.
            //   2. Rebuild the reactions table with run_id as the FK.

            using var tx = _conn.BeginTransaction();
            try
            {
                // 1. Seed default (untreated) runs for every existing specimen-panel combo.
                ExecNonQuery(@"
                    INSERT OR IGNORE INTO panel_runs
                        (specimen_id, panel_id, cell_treatment, serum_treatment, label, created_date)
                    SELECT DISTINCT specimen_id, panel_id, 'None', 'None', 'Untreated', date('now')
                    FROM reactions");

                // 2. Create new reactions table.
                ExecNonQuery(@"
                    CREATE TABLE reactions_v2 (
                        reaction_id INTEGER PRIMARY KEY AUTOINCREMENT,
                        run_id INTEGER NOT NULL,
                        cell_number TEXT NOT NULL,
                        ""IS"" TEXT DEFAULT 'NT',
                        C37 TEXT DEFAULT 'NT',
                        AHG TEXT DEFAULT 'NT',
                        CC TEXT DEFAULT 'NT',
                        FOREIGN KEY (run_id) REFERENCES panel_runs(run_id) ON DELETE CASCADE,
                        UNIQUE(run_id, cell_number)
                    )");

                // 3. Copy rows; join on the untreated run we just created.
                ExecNonQuery(@"
                    INSERT INTO reactions_v2 (run_id, cell_number, ""IS"", C37, AHG, CC)
                    SELECT pr.run_id, r.cell_number, r.""IS"", r.C37, r.AHG, r.CC
                    FROM reactions r
                    JOIN panel_runs pr
                        ON pr.specimen_id = r.specimen_id
                       AND pr.panel_id    = r.panel_id
                       AND pr.cell_treatment  = 'None'
                       AND pr.serum_treatment = 'None'");

                // 4. Swap tables.
                ExecNonQuery("DROP TABLE reactions");
                ExecNonQuery("ALTER TABLE reactions_v2 RENAME TO reactions");

                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }

        private HashSet<string> GetColumnNames(string table)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = $"PRAGMA table_info({table})";
            using var r = cmd.ExecuteReader();
            while (r.Read()) set.Add(r.GetString(1));
            return set;
        }

        // ── Specimens ────────────────────────────────────────────────────────

        public void AddSpecimen(string accessionNumber, string type = "serum", string? expirationDate = null, bool? isActive = null,
            string? notes = null, string? phenotype = null, string? previousAntibodies = null, string? datResult = null,
            string? createdDate = null)
        {
            var today = DateTime.Now.ToString("yyyy-MM-dd");
            var created = string.IsNullOrWhiteSpace(createdDate) ? today : createdDate.Trim();
            bool active = isActive ?? (expirationDate == null || string.Compare(expirationDate, today) >= 0);
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO specimens (accession_number, type, expiration_date, created_date, is_active,
                    notes, phenotype, previous_antibodies, dat_result)
                VALUES ($acc, $type, $exp, $created, $active, $notes, $pheno, $prev, $dat)";
            cmd.Parameters.AddWithValue("$acc", accessionNumber);
            cmd.Parameters.AddWithValue("$type", type);
            cmd.Parameters.AddWithValue("$exp", (object?)expirationDate ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$created", created);
            cmd.Parameters.AddWithValue("$active", active ? 1 : 0);
            cmd.Parameters.AddWithValue("$notes", (object?)notes ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$pheno", (object?)phenotype ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$prev", (object?)previousAntibodies ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$dat", (object?)datResult ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }

        public Specimen? GetSpecimen(string accessionNumber)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM specimens WHERE accession_number = $acc";
            cmd.Parameters.AddWithValue("$acc", accessionNumber);
            using var r = cmd.ExecuteReader();
            return r.Read() ? ReadSpecimen(r) : null;
        }

        public List<Specimen> GetAllSpecimens()
        {
            var list = new List<Specimen>();
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM specimens ORDER BY created_date DESC";
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(ReadSpecimen(r));
            return list;
        }

        public List<Specimen> GetActiveSpecimens()
        {
            var list = new List<Specimen>();
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM specimens WHERE is_active = 1 ORDER BY created_date DESC";
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(ReadSpecimen(r));
            return list;
        }

        public void UpdateSpecimen(string accessionNumber, string type, string? expirationDate, bool isActive = true,
            string? notes = null, string? phenotype = null, string? previousAntibodies = null, string? datResult = null)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE specimens SET type = $type, expiration_date = $exp, is_active = $active,
                    notes = $notes, phenotype = $pheno, previous_antibodies = $prev, dat_result = $dat
                WHERE accession_number = $acc";
            cmd.Parameters.AddWithValue("$type", type);
            cmd.Parameters.AddWithValue("$exp", (object?)expirationDate ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$active", isActive ? 1 : 0);
            cmd.Parameters.AddWithValue("$notes", (object?)notes ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$pheno", (object?)phenotype ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$prev", (object?)previousAntibodies ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$dat", (object?)datResult ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$acc", accessionNumber);
            cmd.ExecuteNonQuery();
        }

        public void SetSpecimenActive(string accessionNumber, bool active)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "UPDATE specimens SET is_active = $active WHERE accession_number = $acc";
            cmd.Parameters.AddWithValue("$active", active ? 1 : 0);
            cmd.Parameters.AddWithValue("$acc", accessionNumber);
            cmd.ExecuteNonQuery();
        }

        public void SetSpecimenFinalCall(string accessionNumber,
            string? antibodies, string? comment, string? identifiedBy)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE specimens SET
                    final_antibodies = $ab,
                    final_comment = $comment,
                    identified_by = $by,
                    identified_at = $at
                WHERE accession_number = $acc";
            cmd.Parameters.AddWithValue("$ab", (object?)antibodies ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$comment", (object?)comment ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$by", (object?)identifiedBy ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$at", antibodies == null
                ? DBNull.Value
                : DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            cmd.Parameters.AddWithValue("$acc", accessionNumber);
            cmd.ExecuteNonQuery();
        }

        public void ClearSpecimenFinalCall(string accessionNumber) =>
            SetSpecimenFinalCall(accessionNumber, null, null, null);

        public void DeleteSpecimen(string accessionNumber)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "DELETE FROM specimens WHERE accession_number = $acc";
            cmd.Parameters.AddWithValue("$acc", accessionNumber);
            cmd.ExecuteNonQuery();
        }

        public void TouchSpecimenReactionsUpdated(string specimenId)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE specimens SET reactions_updated_at = $now WHERE accession_number = $id";
            cmd.Parameters.AddWithValue("$now", DateTime.Now.ToString("o")[..19]);
            cmd.Parameters.AddWithValue("$id", specimenId);
            cmd.ExecuteNonQuery();
        }

        public void SetSpecimenLastAnalyzed(string specimenId)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE specimens SET last_analyzed_at = $now WHERE accession_number = $id";
            cmd.Parameters.AddWithValue("$now", DateTime.Now.ToString("o")[..19]);
            cmd.Parameters.AddWithValue("$id", specimenId);
            cmd.ExecuteNonQuery();
        }

        public bool IsSpecimenAnalysisStale(string specimenId)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
                SELECT reactions_updated_at, last_analyzed_at FROM specimens
                WHERE accession_number = $id";
            cmd.Parameters.AddWithValue("$id", specimenId);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return false;
            var ru = r.IsDBNull(0) ? null : r.GetString(0);
            var la = r.IsDBNull(1) ? null : r.GetString(1);
            if (ru == null && la == null) return false;
            if (ru != null && la == null) return true;
            if (ru == null) return false;
            return string.Compare(ru, la, StringComparison.Ordinal) > 0;
        }

        // ── Specimen Antibodies ───────────────────────────────────────────────

        public void AddSpecimenAntibody(string specimenId, string antibody, double probability)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "DELETE FROM specimen_antibodies WHERE specimen_id = $id AND antibody = $ab";
            cmd.Parameters.AddWithValue("$id", specimenId);
            cmd.Parameters.AddWithValue("$ab", antibody);
            cmd.ExecuteNonQuery();

            cmd.CommandText = @"
                INSERT INTO specimen_antibodies (specimen_id, antibody, probability)
                VALUES ($id, $ab, $prob)";
            cmd.Parameters.AddWithValue("$prob", probability);
            cmd.ExecuteNonQuery();
        }

        public List<SpecimenAntibody> GetSpecimenAntibodies(string specimenId)
        {
            var list = new List<SpecimenAntibody>();
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
                SELECT id, specimen_id, antibody, probability FROM specimen_antibodies
                WHERE specimen_id = $id ORDER BY probability DESC";
            cmd.Parameters.AddWithValue("$id", specimenId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new SpecimenAntibody
                {
                    Id = r.GetInt32(0),
                    SpecimenId = r.GetString(1),
                    Antibody = r.GetString(2),
                    Probability = r.GetDouble(3)
                });
            return list;
        }

        public void ClearSpecimenAntibodies(string specimenId)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "DELETE FROM specimen_antibodies WHERE specimen_id = $id";
            cmd.Parameters.AddWithValue("$id", specimenId);
            cmd.ExecuteNonQuery();
        }

        // ── Specimen Rule-outs ────────────────────────────────────────────────

        public void AddSpecimenRuleout(string specimenId, string antibody, int count)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
                SELECT id FROM specimen_ruleouts WHERE specimen_id = $id AND antibody = $ab";
            cmd.Parameters.AddWithValue("$id", specimenId);
            cmd.Parameters.AddWithValue("$ab", antibody);
            var existingId = cmd.ExecuteScalar();
            if (existingId != null)
            {
                cmd.CommandText = "UPDATE specimen_ruleouts SET ruleout_count = $cnt WHERE id = $eid";
                cmd.Parameters.AddWithValue("$cnt", count);
                cmd.Parameters.AddWithValue("$eid", existingId);
            }
            else
            {
                cmd.CommandText = @"
                    INSERT INTO specimen_ruleouts (specimen_id, antibody, ruleout_count)
                    VALUES ($id, $ab, $cnt)";
                cmd.Parameters.AddWithValue("$cnt", count);
            }
            cmd.ExecuteNonQuery();
        }

        public List<SpecimenRuleout> GetSpecimenRuleouts(string specimenId)
        {
            var list = new List<SpecimenRuleout>();
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
                SELECT id, specimen_id, antibody, ruleout_count FROM specimen_ruleouts
                WHERE specimen_id = $id ORDER BY antibody";
            cmd.Parameters.AddWithValue("$id", specimenId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new SpecimenRuleout
                {
                    Id = r.GetInt32(0),
                    SpecimenId = r.GetString(1),
                    Antibody = r.GetString(2),
                    RuleoutCount = r.GetInt32(3)
                });
            return list;
        }

        public void ClearSpecimenRuleouts(string specimenId)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "DELETE FROM specimen_ruleouts WHERE specimen_id = $id";
            cmd.Parameters.AddWithValue("$id", specimenId);
            cmd.ExecuteNonQuery();
        }

        // ── Panels ────────────────────────────────────────────────────────────

        public int AddPanel(string name, string? lotNumber, string? vendor,
            int numCells, string? expirationDate, bool includeAc, int startCell = 1, bool? isActive = null)
        {
            var today = DateTime.Now.ToString("yyyy-MM-dd");
            bool active = isActive ?? (expirationDate == null || string.Compare(expirationDate, today) >= 0);
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO panels (name, lot_number, vendor, num_cells, expiration_date, include_ac, start_cell, is_active)
                VALUES ($name, $lot, $vendor, $num, $exp, $ac, $sc, $active);
                SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("$name", name);
            cmd.Parameters.AddWithValue("$lot", (object?)lotNumber ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$vendor", (object?)vendor ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$num", numCells);
            cmd.Parameters.AddWithValue("$exp", (object?)expirationDate ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$ac", includeAc ? 1 : 0);
            cmd.Parameters.AddWithValue("$sc", startCell);
            cmd.Parameters.AddWithValue("$active", active ? 1 : 0);
            var panelId = Convert.ToInt32(cmd.ExecuteScalar());

            for (int i = startCell; i < startCell + numCells; i++)
                AddPanelCell(panelId, i.ToString());
            if (includeAc)
                AddPanelCell(panelId, "AC");

            return panelId;
        }

        public Panel? GetPanel(int panelId)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM panels WHERE panel_id = $id";
            cmd.Parameters.AddWithValue("$id", panelId);
            using var r = cmd.ExecuteReader();
            return r.Read() ? ReadPanel(r) : null;
        }

        public List<Panel> GetAllPanels()
        {
            var list = new List<Panel>();
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM panels ORDER BY name";
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(ReadPanel(r));
            return list;
        }

        public List<Panel> GetActivePanels()
        {
            var list = new List<Panel>();
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM panels WHERE is_active = 1 ORDER BY name";
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(ReadPanel(r));
            return list;
        }

        public void UpdatePanel(int panelId, string name, string? lotNumber, string? vendor,
            int numCells, string? expirationDate, bool includeAc, int startCell = 1, bool isActive = true)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE panels SET name=$name, lot_number=$lot, vendor=$vendor,
                num_cells=$num, expiration_date=$exp, include_ac=$ac, start_cell=$sc, is_active=$active
                WHERE panel_id=$id";
            cmd.Parameters.AddWithValue("$name", name);
            cmd.Parameters.AddWithValue("$lot", (object?)lotNumber ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$vendor", (object?)vendor ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$num", numCells);
            cmd.Parameters.AddWithValue("$exp", (object?)expirationDate ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$ac", includeAc ? 1 : 0);
            cmd.Parameters.AddWithValue("$sc", startCell);
            cmd.Parameters.AddWithValue("$active", isActive ? 1 : 0);
            cmd.Parameters.AddWithValue("$id", panelId);
            cmd.ExecuteNonQuery();

            DeletePanelCells(panelId);
            for (int i = startCell; i < startCell + numCells; i++)
                AddPanelCell(panelId, i.ToString());
            if (includeAc)
                AddPanelCell(panelId, "AC");
        }

        public void SetPanelActive(int panelId, bool active)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "UPDATE panels SET is_active = $active WHERE panel_id = $id";
            cmd.Parameters.AddWithValue("$active", active ? 1 : 0);
            cmd.Parameters.AddWithValue("$id", panelId);
            cmd.ExecuteNonQuery();
        }

        public void DeletePanel(int panelId)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "DELETE FROM panels WHERE panel_id = $id";
            cmd.Parameters.AddWithValue("$id", panelId);
            cmd.ExecuteNonQuery();
        }

        // ── Panel Cells ───────────────────────────────────────────────────────

        public void AddPanelCell(int panelId, string cellNumber)
        {
            var cols = string.Join(", ", AntigenMapper.AllColumns);
            var vals = string.Join(", ", GetAllAntigenColumns(_ => "'-'"));
            using (var cmd = _conn.CreateCommand())
            {
                cmd.CommandText = $@"
                    INSERT INTO panel_cells (panel_id, cell_number, {cols})
                    VALUES ($pid, $cn, {vals})";
                cmd.Parameters.AddWithValue("$pid", panelId);
                cmd.Parameters.AddWithValue("$cn", cellNumber);
                cmd.ExecuteNonQuery();
            }

            long cellId;
            using (var cmd = _conn.CreateCommand())
            {
                cmd.CommandText = "SELECT last_insert_rowid()";
                cellId = (long)(cmd.ExecuteScalar() ?? 0L);
            }
            SeedExtraAntigensForCell(panelId, (int)cellId);
        }

        public List<PanelCell> GetPanelCells(int panelId)
        {
            var list = new List<PanelCell>();
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
                SELECT * FROM panel_cells WHERE panel_id = $id
                ORDER BY CASE WHEN cell_number = 'AC' THEN 999
                              ELSE CAST(cell_number AS INTEGER) END";
            cmd.Parameters.AddWithValue("$id", panelId);
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(ReadPanelCell(r));
            AttachExtraAntigens(list);
            return list;
        }

        public void UpdatePanelCellAntigen(int cellId, string antigen, string value)
        {
            if (AntigenConstants.IsWarehouse(antigen))
            {
                UpsertExtraCellAntigen(cellId, antigen, value);
                return;
            }

            var col = AntigenMapper.GetColumn(antigen);
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = $"UPDATE panel_cells SET {col} = $val WHERE id = $id";
            cmd.Parameters.AddWithValue("$val", value);
            cmd.Parameters.AddWithValue("$id", cellId);
            cmd.ExecuteNonQuery();
        }

        public void UpdatePanelCell(PanelCell cell)
        {
            foreach (var ag in AntigenConstants.Antigens)
                UpdatePanelCellAntigen(cell.Id, ag, cell.GetAntigen(ag));
            foreach (var ag in AntigenConstants.WarehouseAntigens)
            {
                if (!cell.HasTypedAntigen(ag)) continue;
                UpdatePanelCellAntigen(cell.Id, ag, cell.GetAntigen(ag));
            }
        }

        public void DeletePanelCells(int panelId)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "DELETE FROM panel_cells WHERE panel_id = $id";
            cmd.Parameters.AddWithValue("$id", panelId);
            cmd.ExecuteNonQuery();
        }

        public void ReplacePanelCells(int panelId, IReadOnlyList<PanelCell> cells)
        {
            DeletePanelCells(panelId);
            int numeric = 0;
            bool includeAc = false;
            int startCell = 1;
            bool startSet = false;
            foreach (var imported in cells)
            {
                AddPanelCell(panelId, imported.CellNumber);
                if (string.Equals(imported.CellNumber, "AC", StringComparison.OrdinalIgnoreCase))
                {
                    includeAc = true;
                    continue;
                }
                numeric++;
                if (int.TryParse(imported.CellNumber, out var n) && !startSet)
                {
                    startCell = n;
                    startSet = true;
                }
            }

            var created = GetPanelCells(panelId).ToDictionary(c => c.CellNumber, StringComparer.OrdinalIgnoreCase);
            var extrasOnImport = cells
                .SelectMany(c => c.Antigens.Keys)
                .Where(AntigenConstants.IsWarehouse)
                .Distinct()
                .ToList();
            foreach (var ag in extrasOnImport)
                AddPanelExtraAntigen(panelId, ag);

            foreach (var imported in cells)
            {
                if (!created.TryGetValue(imported.CellNumber, out var cell)) continue;
                foreach (var ag in AntigenConstants.Antigens)
                    cell.SetAntigen(ag, imported.GetAntigen(ag));
                foreach (var ag in extrasOnImport)
                    cell.SetAntigen(ag, imported.GetAntigen(ag));
                UpdatePanelCell(cell);
            }

            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE panels SET num_cells = $num, include_ac = $ac, start_cell = $sc
                WHERE panel_id = $id";
            cmd.Parameters.AddWithValue("$num", numeric);
            cmd.Parameters.AddWithValue("$ac", includeAc ? 1 : 0);
            cmd.Parameters.AddWithValue("$sc", startCell);
            cmd.Parameters.AddWithValue("$id", panelId);
            cmd.ExecuteNonQuery();
        }

        public void CopyPanelCells(int sourcePanelId, int targetPanelId)
        {
            DeletePanelCells(targetPanelId);
            var agCols = string.Join(", ", AntigenMapper.AllColumns);
            using (var cmd = _conn.CreateCommand())
            {
                cmd.CommandText = $@"
                    INSERT INTO panel_cells (panel_id, cell_number, {agCols})
                    SELECT {targetPanelId}, cell_number, {agCols}
                    FROM panel_cells WHERE panel_id = $src";
                cmd.Parameters.AddWithValue("$src", sourcePanelId);
                cmd.ExecuteNonQuery();
            }

            using (var cmd = _conn.CreateCommand())
            {
                cmd.CommandText = @"
                    DELETE FROM panel_extra_antigens WHERE panel_id = $tgt;
                    INSERT INTO panel_extra_antigens (panel_id, antigen_name)
                    SELECT $tgt, antigen_name FROM panel_extra_antigens WHERE panel_id = $src";
                cmd.Parameters.AddWithValue("$tgt", targetPanelId);
                cmd.Parameters.AddWithValue("$src", sourcePanelId);
                cmd.ExecuteNonQuery();
            }

            using (var cmd = _conn.CreateCommand())
            {
                cmd.CommandText = @"
                    INSERT INTO panel_cell_extra_antigens (cell_id, antigen_name, value)
                    SELECT t.id, x.antigen_name, x.value
                    FROM panel_cell_extra_antigens x
                    JOIN panel_cells s ON s.id = x.cell_id AND s.panel_id = $src
                    JOIN panel_cells t ON t.panel_id = $tgt AND t.cell_number = s.cell_number";
                cmd.Parameters.AddWithValue("$src", sourcePanelId);
                cmd.Parameters.AddWithValue("$tgt", targetPanelId);
                cmd.ExecuteNonQuery();
            }
        }

        // ── Warehouse / extra antigens ────────────────────────────────────────

        public List<string> GetPanelExtraAntigens(int panelId)
        {
            var assigned = new HashSet<string>(StringComparer.Ordinal);
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
                SELECT antigen_name FROM panel_extra_antigens WHERE panel_id = $id";
            cmd.Parameters.AddWithValue("$id", panelId);
            using var r = cmd.ExecuteReader();
            while (r.Read()) assigned.Add(r.GetString(0));
            return AntigenConstants.WarehouseAntigens.Where(assigned.Contains).ToList();
        }

        public List<string> GetExtraAntigensForPanels(IEnumerable<int> panelIds)
        {
            var ids = panelIds.Distinct().ToList();
            if (ids.Count == 0) return new();
            var assigned = new HashSet<string>(StringComparer.Ordinal);
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT DISTINCT antigen_name FROM panel_extra_antigens
                WHERE panel_id IN ({string.Join(",", ids)})";
            using var r = cmd.ExecuteReader();
            while (r.Read()) assigned.Add(r.GetString(0));
            return AntigenConstants.WarehouseAntigens.Where(assigned.Contains).ToList();
        }

        public bool PanelHasExtraAntigen(int panelId, string antigen)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
                SELECT 1 FROM panel_extra_antigens
                WHERE panel_id = $id AND antigen_name = $ag LIMIT 1";
            cmd.Parameters.AddWithValue("$id", panelId);
            cmd.Parameters.AddWithValue("$ag", antigen);
            return cmd.ExecuteScalar() != null;
        }

        public void AddPanelExtraAntigen(int panelId, string antigen)
        {
            if (!AntigenConstants.IsWarehouse(antigen)) return;
            using (var cmd = _conn.CreateCommand())
            {
                cmd.CommandText = @"
                    INSERT OR IGNORE INTO panel_extra_antigens (panel_id, antigen_name)
                    VALUES ($pid, $ag)";
                cmd.Parameters.AddWithValue("$pid", panelId);
                cmd.Parameters.AddWithValue("$ag", antigen);
                cmd.ExecuteNonQuery();
            }

            foreach (var cell in GetPanelCells(panelId))
            {
                if (cell.HasTypedAntigen(antigen)) continue;
                UpsertExtraCellAntigen(cell.Id, antigen, "-");
            }
        }

        public void RemovePanelExtraAntigen(int panelId, string antigen)
        {
            using (var cmd = _conn.CreateCommand())
            {
                cmd.CommandText = @"
                    DELETE FROM panel_cell_extra_antigens
                    WHERE antigen_name = $ag
                      AND cell_id IN (SELECT id FROM panel_cells WHERE panel_id = $pid)";
                cmd.Parameters.AddWithValue("$ag", antigen);
                cmd.Parameters.AddWithValue("$pid", panelId);
                cmd.ExecuteNonQuery();
            }
            using (var cmd = _conn.CreateCommand())
            {
                cmd.CommandText = @"
                    DELETE FROM panel_extra_antigens
                    WHERE panel_id = $pid AND antigen_name = $ag";
                cmd.Parameters.AddWithValue("$pid", panelId);
                cmd.Parameters.AddWithValue("$ag", antigen);
                cmd.ExecuteNonQuery();
            }
        }

        private void SeedExtraAntigensForCell(int panelId, int cellId)
        {
            foreach (var ag in GetPanelExtraAntigens(panelId))
                UpsertExtraCellAntigen(cellId, ag, "-");
        }

        private void UpsertExtraCellAntigen(int cellId, string antigen, string value)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO panel_cell_extra_antigens (cell_id, antigen_name, value)
                VALUES ($id, $ag, $val)
                ON CONFLICT(cell_id, antigen_name) DO UPDATE SET value = $val";
            cmd.Parameters.AddWithValue("$id", cellId);
            cmd.Parameters.AddWithValue("$ag", antigen);
            cmd.Parameters.AddWithValue("$val", value);
            cmd.ExecuteNonQuery();
        }

        private void AttachExtraAntigens(IReadOnlyList<PanelCell> cells)
        {
            if (cells.Count == 0) return;
            var byId = cells.ToDictionary(c => c.Id);
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT cell_id, antigen_name, value
                FROM panel_cell_extra_antigens
                WHERE cell_id IN ({string.Join(",", byId.Keys)})";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var cellId = r.GetInt32(0);
                if (!byId.TryGetValue(cellId, out var cell)) continue;
                cell.Antigens[r.GetString(1)] = r.GetString(2);
            }
        }

        // ── Specimen-Panel Linking ─────────────────────────────────────────────

        public void LinkSpecimenPanel(string specimenId, int panelId)
        {
            try
            {
                using var cmd = _conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO specimen_panels (specimen_id, panel_id) VALUES ($sid, $pid)";
                cmd.Parameters.AddWithValue("$sid", specimenId);
                cmd.Parameters.AddWithValue("$pid", panelId);
                cmd.ExecuteNonQuery();
            }
            catch (SqliteException) { /* already linked */ }
        }

        public void UnlinkSpecimenPanel(string specimenId, int panelId)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
                DELETE FROM specimen_panels WHERE specimen_id = $sid AND panel_id = $pid";
            cmd.Parameters.AddWithValue("$sid", specimenId);
            cmd.Parameters.AddWithValue("$pid", panelId);
            cmd.ExecuteNonQuery();
        }

        public List<Panel> GetSpecimenPanels(string specimenId)
        {
            var list = new List<Panel>();
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
                SELECT p.* FROM panels p
                JOIN specimen_panels sp ON p.panel_id = sp.panel_id
                WHERE sp.specimen_id = $sid ORDER BY p.name";
            cmd.Parameters.AddWithValue("$sid", specimenId);
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(ReadPanel(r));
            return list;
        }

        // ── Panel Runs ────────────────────────────────────────────────────────

        /// <summary>
        /// Creates a new panel run and returns its run_id.
        /// Throws <see cref="SqliteException"/> if a run with the same
        /// (specimen, panel, cell_treatment, serum_treatment) already exists.
        /// </summary>
        public int AddPanelRun(string specimenId, int panelId,
            CellTreatment cellTreatment,
            SerumTreatment serumTreatment,
            string label = "")
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO panel_runs (specimen_id, panel_id, cell_treatment, serum_treatment, label, created_date)
                VALUES ($sid, $pid, $ct, $st, $lbl, $dt);
                SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("$sid", specimenId);
            cmd.Parameters.AddWithValue("$pid", panelId);
            cmd.Parameters.AddWithValue("$ct", cellTreatment.ToString());
            cmd.Parameters.AddWithValue("$st", serumTreatment.ToString());
            cmd.Parameters.AddWithValue("$lbl", (object?)label ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$dt", DateTime.Now.ToString("yyyy-MM-dd"));
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        /// <summary>
        /// Returns the run_id of the untreated (default) run for a specimen/panel pair,
        /// creating one if it does not yet exist.
        /// </summary>
        public int GetOrCreateDefaultRun(string specimenId, int panelId)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
                SELECT run_id FROM panel_runs
                WHERE specimen_id = $sid AND panel_id = $pid
                  AND cell_treatment = 'None' AND serum_treatment = 'None'";
            cmd.Parameters.AddWithValue("$sid", specimenId);
            cmd.Parameters.AddWithValue("$pid", panelId);
            var existing = cmd.ExecuteScalar();
            if (existing != null && existing != DBNull.Value)
                return Convert.ToInt32(existing);

            return AddPanelRun(specimenId, panelId,
                CellTreatment.None,
                SerumTreatment.None,
                "Untreated");
        }

        public PanelRun? GetPanelRun(int runId)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
                SELECT pr.*, p.name AS panel_name
                FROM panel_runs pr
                JOIN panels p ON pr.panel_id = p.panel_id
                WHERE pr.run_id = $id";
            cmd.Parameters.AddWithValue("$id", runId);
            using var r = cmd.ExecuteReader();
            return r.Read() ? ReadPanelRun(r) : null;
        }

        public List<PanelRun> GetPanelRuns(string specimenId, int panelId)
        {
            var list = new List<PanelRun>();
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
                SELECT pr.*, p.name AS panel_name
                FROM panel_runs pr
                JOIN panels p ON pr.panel_id = p.panel_id
                WHERE pr.specimen_id = $sid AND pr.panel_id = $pid
                ORDER BY pr.run_id";
            cmd.Parameters.AddWithValue("$sid", specimenId);
            cmd.Parameters.AddWithValue("$pid", panelId);
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(ReadPanelRun(r));
            return list;
        }

        public List<PanelRun> GetAllSpecimenRuns(string specimenId)
        {
            var list = new List<PanelRun>();
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
                SELECT pr.*, p.name AS panel_name
                FROM panel_runs pr
                JOIN panels p ON pr.panel_id = p.panel_id
                WHERE pr.specimen_id = $sid
                ORDER BY p.name, pr.run_id";
            cmd.Parameters.AddWithValue("$sid", specimenId);
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(ReadPanelRun(r));
            return list;
        }

        public void DeletePanelRun(int runId)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "DELETE FROM panel_runs WHERE run_id = $id";
            cmd.Parameters.AddWithValue("$id", runId);
            cmd.ExecuteNonQuery();
        }

        public int CopyReactions(int sourceRunId, int targetRunId)
        {
            var source = GetReactions(sourceRunId);
            foreach (var rxn in source)
                SaveReaction(targetRunId, rxn.CellNumber, rxn.IS, rxn.C37, rxn.AHG, rxn.CC);
            return source.Count;
        }

        // ── Reactions ─────────────────────────────────────────────────────────

        /// <summary>Primary overload: saves a reaction for a specific run.</summary>
        public void SaveReaction(int runId, string cellNumber,
            string is_, string c37, string ahg, string cc)
        {
            // Need specimen_id to call TouchSpecimenReactionsUpdated
            var run = GetPanelRun(runId);
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
                INSERT OR REPLACE INTO reactions (run_id, cell_number, ""IS"", C37, AHG, CC)
                VALUES ($rid, $cn, $is, $c37, $ahg, $cc)";
            cmd.Parameters.AddWithValue("$rid", runId);
            cmd.Parameters.AddWithValue("$cn", cellNumber);
            cmd.Parameters.AddWithValue("$is", is_);
            cmd.Parameters.AddWithValue("$c37", c37);
            cmd.Parameters.AddWithValue("$ahg", ahg);
            cmd.Parameters.AddWithValue("$cc", cc);
            cmd.ExecuteNonQuery();
            if (run != null) TouchSpecimenReactionsUpdated(run.SpecimenId);
        }

        /// <summary>
        /// Compatibility overload: resolves (or creates) the untreated default run,
        /// then saves the reaction.
        /// </summary>
        public void SaveReaction(string specimenId, int panelId, string cellNumber,
            string is_, string c37, string ahg, string cc)
        {
            var runId = GetOrCreateDefaultRun(specimenId, panelId);
            SaveReaction(runId, cellNumber, is_, c37, ahg, cc);
        }

        /// <summary>Returns reactions for a specific run.</summary>
        public List<Reaction> GetReactions(int runId)
        {
            var list = new List<Reaction>();
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
                SELECT rxn.reaction_id, rxn.run_id, rxn.cell_number,
                       rxn.""IS"", rxn.C37, rxn.AHG, rxn.CC,
                       pr.specimen_id, pr.panel_id, pr.cell_treatment, pr.serum_treatment
                FROM reactions rxn
                JOIN panel_runs pr ON rxn.run_id = pr.run_id
                WHERE rxn.run_id = $rid
                ORDER BY CASE WHEN rxn.cell_number = 'AC' THEN 999
                              ELSE CAST(rxn.cell_number AS INTEGER) END";
            cmd.Parameters.AddWithValue("$rid", runId);
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(ReadReactionFull(r));
            return list;
        }

        /// <summary>
        /// Compatibility overload: returns reactions for the untreated default run of
        /// a (specimen, panel) pair. Returns empty if no default run exists.
        /// </summary>
        public List<Reaction> GetReactions(string specimenId, int panelId)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
                SELECT run_id FROM panel_runs
                WHERE specimen_id = $sid AND panel_id = $pid
                  AND cell_treatment = 'None' AND serum_treatment = 'None'";
            cmd.Parameters.AddWithValue("$sid", specimenId);
            cmd.Parameters.AddWithValue("$pid", panelId);
            var runId = cmd.ExecuteScalar();
            if (runId == null || runId == DBNull.Value) return new();
            return GetReactions(Convert.ToInt32(runId));
        }

        /// <summary>Returns all reactions for a specimen across every run.</summary>
        public List<Reaction> GetAllSpecimenReactions(string specimenId)
        {
            var list = new List<Reaction>();
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
                SELECT rxn.reaction_id, rxn.run_id, rxn.cell_number,
                       rxn.""IS"", rxn.C37, rxn.AHG, rxn.CC,
                       pr.specimen_id, pr.panel_id, pr.cell_treatment, pr.serum_treatment
                FROM reactions rxn
                JOIN panel_runs pr ON rxn.run_id = pr.run_id
                JOIN panels p ON pr.panel_id = p.panel_id
                WHERE pr.specimen_id = $sid
                ORDER BY p.name, pr.run_id,
                    CASE WHEN rxn.cell_number = 'AC' THEN 999
                         ELSE CAST(rxn.cell_number AS INTEGER) END";
            cmd.Parameters.AddWithValue("$sid", specimenId);
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(ReadReactionFull(r));
            return list;
        }

        /// <summary>Deletes all reactions for a specific run.</summary>
        public void DeleteReactions(int runId)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "DELETE FROM reactions WHERE run_id = $rid";
            cmd.Parameters.AddWithValue("$rid", runId);
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// Compatibility overload: deletes reactions for the untreated default run.
        /// </summary>
        public void DeleteReactions(string specimenId, int panelId)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
                DELETE FROM reactions WHERE run_id IN (
                    SELECT run_id FROM panel_runs
                    WHERE specimen_id = $sid AND panel_id = $pid
                      AND cell_treatment = 'None' AND serum_treatment = 'None'
                )";
            cmd.Parameters.AddWithValue("$sid", specimenId);
            cmd.Parameters.AddWithValue("$pid", panelId);
            cmd.ExecuteNonQuery();
        }

        // ── Worklist ──────────────────────────────────────────────────────────

        public List<WorklistItem> GetWorklistItems(int expirationWarningDays = 14)
        {
            var items = new List<WorklistItem>();
            var today = DateTime.Now.Date;
            var horizon = today.AddDays(expirationWarningDays).ToString("yyyy-MM-dd");
            var todayStr = today.ToString("yyyy-MM-dd");

            foreach (var s in GetAllSpecimens().Where(s => s.IsActive))
            {
                var panels = GetSpecimenPanels(s.AccessionNumber);
                if (panels.Count == 0)
                {
                    items.Add(new WorklistItem
                    {
                        Kind = WorklistKind.IncompleteReactions,
                        KindLabel = "Incomplete",
                        Title = s.AccessionNumber,
                        Detail = "No panel attached.",
                        AccessionNumber = s.AccessionNumber,
                        TargetTab = "Specimens"
                    });
                }
                else
                {
                    bool incomplete = false;
                    var missing = new List<string>();
                    foreach (var p in panels)
                    {
                        var runs = GetPanelRuns(s.AccessionNumber, p.PanelId);
                        var run = runs.FirstOrDefault(r => r.IsUntreated) ?? runs.FirstOrDefault();
                        if (run == null)
                        {
                            incomplete = true;
                            missing.Add(p.Name);
                            continue;
                        }
                        var cells = GetPanelCells(p.PanelId);
                        var rxns = GetReactions(run.RunId).ToDictionary(r => r.CellNumber);
                        int entered = 0;
                        foreach (var cell in cells)
                        {
                            if (!rxns.TryGetValue(cell.CellNumber, out var rxn)) continue;
                            if (HasEnteredGrade(rxn)) entered++;
                        }
                        if (entered == 0 || entered < cells.Count)
                        {
                            incomplete = true;
                            missing.Add($"{p.Name} ({entered}/{cells.Count})");
                        }
                    }
                    if (incomplete)
                    {
                        items.Add(new WorklistItem
                        {
                            Kind = WorklistKind.IncompleteReactions,
                            KindLabel = "Incomplete",
                            Title = s.AccessionNumber,
                            Detail = "Reactions not finished: " + string.Join("; ", missing),
                            AccessionNumber = s.AccessionNumber,
                            TargetTab = "Reactions"
                        });
                    }
                }

                if (s.IsAnalysisStale || (s.LastAnalyzedAt == null && panels.Count > 0))
                {
                    items.Add(new WorklistItem
                    {
                        Kind = WorklistKind.StaleAnalysis,
                        KindLabel = "Stale analysis",
                        Title = s.AccessionNumber,
                        Detail = s.LastAnalyzedAt == null
                            ? "Never analyzed."
                            : "Reactions changed since last analysis.",
                        AccessionNumber = s.AccessionNumber,
                        TargetTab = "Analysis"
                    });
                }

                if (s.ExpirationDate != null &&
                    string.Compare(s.ExpirationDate, todayStr, StringComparison.Ordinal) >= 0 &&
                    string.Compare(s.ExpirationDate, horizon, StringComparison.Ordinal) <= 0)
                {
                    items.Add(new WorklistItem
                    {
                        Kind = WorklistKind.ExpiringSpecimen,
                        KindLabel = "Expiring specimen",
                        Title = s.AccessionNumber,
                        Detail = $"Expires {s.ExpirationDate}.",
                        AccessionNumber = s.AccessionNumber,
                        TargetTab = "Specimens"
                    });
                }
            }

            foreach (var p in GetAllPanels().Where(p => p.IsActive))
            {
                if (p.ExpirationDate == null) continue;
                if (string.Compare(p.ExpirationDate, todayStr, StringComparison.Ordinal) >= 0 &&
                    string.Compare(p.ExpirationDate, horizon, StringComparison.Ordinal) <= 0)
                {
                    items.Add(new WorklistItem
                    {
                        Kind = WorklistKind.ExpiringPanel,
                        KindLabel = "Expiring panel",
                        Title = p.Name,
                        Detail = $"Lot {p.LotNumber ?? "N/A"} expires {p.ExpirationDate}.",
                        PanelId = p.PanelId,
                        TargetTab = "Panels"
                    });
                }
            }

            return items
                .OrderBy(i => i.Kind)
                .ThenBy(i => i.Title)
                .ToList();
        }

        private static bool HasEnteredGrade(Reaction rxn) =>
            IsEntered(rxn.IS) || IsEntered(rxn.C37) || IsEntered(rxn.AHG);

        private static bool IsEntered(string v) =>
            !string.IsNullOrEmpty(v) && v != "NT";

        // ── Rules ─────────────────────────────────────────────────────────────

        public int AddRule(string name, string? description, string antibody,
            string? exceptionAntigen, bool heterozygousOk, int minRuleoutCount)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO rules (name, description, antibody, exception_antigen,
                                   heterozygous_ok, min_ruleout_count)
                VALUES ($name, $desc, $ab, $exc, $hetok, $min);
                SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("$name", name);
            cmd.Parameters.AddWithValue("$desc", (object?)description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$ab", antibody);
            cmd.Parameters.AddWithValue("$exc", (object?)exceptionAntigen ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$hetok", heterozygousOk ? 1 : 0);
            cmd.Parameters.AddWithValue("$min", minRuleoutCount);
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        public List<Rule> GetAllRules()
        {
            var list = new List<Rule>();
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM rules ORDER BY name";
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(ReadRule(r));
            return list;
        }

        public void UpdateRule(int ruleId, string name, string? description, string antibody,
            string? exceptionAntigen, bool heterozygousOk, int minRuleoutCount)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE rules SET name=$name, description=$desc, antibody=$ab,
                exception_antigen=$exc, heterozygous_ok=$hetok, min_ruleout_count=$min
                WHERE rule_id=$id";
            cmd.Parameters.AddWithValue("$name", name);
            cmd.Parameters.AddWithValue("$desc", (object?)description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$ab", antibody);
            cmd.Parameters.AddWithValue("$exc", (object?)exceptionAntigen ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$hetok", heterozygousOk ? 1 : 0);
            cmd.Parameters.AddWithValue("$min", minRuleoutCount);
            cmd.Parameters.AddWithValue("$id", ruleId);
            cmd.ExecuteNonQuery();
        }

        public void DeleteRule(int ruleId)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "DELETE FROM rules WHERE rule_id = $id";
            cmd.Parameters.AddWithValue("$id", ruleId);
            cmd.ExecuteNonQuery();
        }

        // ── Search ────────────────────────────────────────────────────────────

        public List<(Panel panel, PanelCell cell)> SearchCellsByProfile(
            Dictionary<string, string> antigenCriteria,
            string? positiveZygosity = null)
        {
            var whereClauses = new List<string>();
            int param = 0;
            var parameters = new List<(string name, string value)>();
            var requireHomo = string.Equals(positiveZygosity, AntigenConstants.ZygosityHomozygous,
                StringComparison.OrdinalIgnoreCase);
            var requireHet = string.Equals(positiveZygosity, AntigenConstants.ZygosityHeterozygous,
                StringComparison.OrdinalIgnoreCase);

            foreach (var kvp in antigenCriteria)
            {
                if (kvp.Value != "+" && kvp.Value != "-") continue;
                if (!TryAddAntigenEqualsClause(kvp.Key, kvp.Value, whereClauses, parameters, ref param))
                    continue;

                if (kvp.Value == "+" &&
                    (requireHomo || requireHet) &&
                    AntigenConstants.AntitheticalPairs.TryGetValue(kvp.Key, out var antithetical))
                {
                    var antitheticalValue = requireHomo ? "-" : "+";
                    TryAddAntigenEqualsClause(antithetical, antitheticalValue, whereClauses, parameters, ref param);
                }
            }
            if (whereClauses.Count == 0) return new();

            var where = string.Join(" AND ", whereClauses);
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT p.*, pc.*
                FROM panel_cells pc
                JOIN panels p ON pc.panel_id = p.panel_id
                WHERE {where}
                ORDER BY p.name, pc.cell_number";
            foreach (var (name, value) in parameters)
                cmd.Parameters.AddWithValue(name, value);

            var results = new List<(Panel, PanelCell)>();
            var cells = new List<PanelCell>();
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    var panel = new Panel
                    {
                        PanelId = r.GetInt32(r.GetOrdinal("panel_id")),
                        Name = r.GetString(r.GetOrdinal("name")),
                        LotNumber = r.IsDBNull(r.GetOrdinal("lot_number")) ? null : r.GetString(r.GetOrdinal("lot_number")),
                        Vendor = r.IsDBNull(r.GetOrdinal("vendor")) ? null : r.GetString(r.GetOrdinal("vendor")),
                    };
                    var cell = ReadPanelCell(r);
                    results.Add((panel, cell));
                    cells.Add(cell);
                }
            }
            AttachExtraAntigens(cells);
            return results;
        }

        private static bool TryAddAntigenEqualsClause(
            string antigen,
            string value,
            List<string> whereClauses,
            List<(string name, string value)> parameters,
            ref int param)
        {
            if (AntigenConstants.IsStandard(antigen))
            {
                var pName = $"$v{param++}";
                parameters.Add((pName, value));
                var col = AntigenMapper.GetColumn(antigen);
                whereClauses.Add($"pc.{col} = {pName}");
                return true;
            }

            if (AntigenConstants.IsWarehouse(antigen))
            {
                var pName = $"$v{param++}";
                var agName = $"$ag{param++}";
                parameters.Add((pName, value));
                parameters.Add((agName, antigen));
                whereClauses.Add($@"
                        EXISTS (
                            SELECT 1 FROM panel_cell_extra_antigens x
                            WHERE x.cell_id = pc.id
                              AND x.antigen_name = {agName}
                              AND x.value = {pName}
                        )");
                return true;
            }

            return false;
        }

        // ── Readers ───────────────────────────────────────────────────────────

        private static Specimen ReadSpecimen(SqliteDataReader r) => new Specimen
        {
            AccessionNumber = r.GetString(r.GetOrdinal("accession_number")),
            Type = r.GetString(r.GetOrdinal("type")),
            ExpirationDate = r.IsDBNull(r.GetOrdinal("expiration_date")) ? null : r.GetString(r.GetOrdinal("expiration_date")),
            CreatedDate = r.GetString(r.GetOrdinal("created_date")),
            ReactionsUpdatedAt = SafeGetString(r, "reactions_updated_at"),
            LastAnalyzedAt = SafeGetString(r, "last_analyzed_at"),
            IsActive = SafeGetInt(r, "is_active", 1) != 0,
            Notes = SafeGetString(r, "notes"),
            Phenotype = SafeGetString(r, "phenotype"),
            PreviousAntibodies = SafeGetString(r, "previous_antibodies"),
            DatResult = SafeGetString(r, "dat_result"),
            FinalAntibodies = SafeGetString(r, "final_antibodies"),
            FinalComment = SafeGetString(r, "final_comment"),
            IdentifiedBy = SafeGetString(r, "identified_by"),
            IdentifiedAt = SafeGetString(r, "identified_at"),
        };

        private static Panel ReadPanel(SqliteDataReader r) => new Panel
        {
            PanelId = r.GetInt32(r.GetOrdinal("panel_id")),
            Name = r.GetString(r.GetOrdinal("name")),
            LotNumber = r.IsDBNull(r.GetOrdinal("lot_number")) ? null : r.GetString(r.GetOrdinal("lot_number")),
            Vendor = r.IsDBNull(r.GetOrdinal("vendor")) ? null : r.GetString(r.GetOrdinal("vendor")),
            NumCells = r.GetInt32(r.GetOrdinal("num_cells")),
            StartCell = SafeGetInt(r, "start_cell", 1),
            ExpirationDate = r.IsDBNull(r.GetOrdinal("expiration_date")) ? null : r.GetString(r.GetOrdinal("expiration_date")),
            IncludeAc = r.GetInt32(r.GetOrdinal("include_ac")) != 0,
            IsActive = SafeGetInt(r, "is_active", 1) != 0,
        };

        private static PanelCell ReadPanelCell(SqliteDataReader r)
        {
            var cell = new PanelCell
            {
                Id = r.GetInt32(r.GetOrdinal("id")),
                PanelId = r.GetInt32(r.GetOrdinal("panel_id")),
                CellNumber = r.GetString(r.GetOrdinal("cell_number")),
            };
            foreach (var ag in AntigenConstants.Antigens)
            {
                var col = AntigenMapper.GetColumn(ag);
                var ordinal = r.GetOrdinal(col);
                cell.Antigens[ag] = r.IsDBNull(ordinal) ? "-" : r.GetString(ordinal);
            }
            return cell;
        }

        private static PanelRun ReadPanelRun(SqliteDataReader r)
        {
            var run = new PanelRun
            {
                RunId = r.GetInt32(r.GetOrdinal("run_id")),
                SpecimenId = r.GetString(r.GetOrdinal("specimen_id")),
                PanelId = r.GetInt32(r.GetOrdinal("panel_id")),
                Label = SafeGetString(r, "label") ?? string.Empty,
                CreatedDate = SafeGetString(r, "created_date") ?? string.Empty,
                PanelName = SafeGetString(r, "panel_name") ?? string.Empty,
            };
            var ctStr = SafeGetString(r, "cell_treatment") ?? "None";
            var stStr = SafeGetString(r, "serum_treatment") ?? "None";
            run.CellTreatment = Enum.TryParse<CellTreatment>(ctStr, out var ct) ? ct : CellTreatment.None;
            run.SerumTreatment = Enum.TryParse<SerumTreatment>(stStr, out var st) ? st : SerumTreatment.None;
            return run;
        }

        /// <summary>
        /// Reads a reaction from a result set that includes denormalized panel_run columns
        /// (specimen_id, panel_id, cell_treatment, serum_treatment).
        /// </summary>
        private static Reaction ReadReactionFull(SqliteDataReader r)
        {
            var rxn = new Reaction
            {
                ReactionId = r.GetInt32(r.GetOrdinal("reaction_id")),
                RunId = r.GetInt32(r.GetOrdinal("run_id")),
                CellNumber = r.GetString(r.GetOrdinal("cell_number")),
                IS = SafeGetString(r, "IS") ?? "NT",
                C37 = SafeGetString(r, "C37") ?? "NT",
                AHG = SafeGetString(r, "AHG") ?? "NT",
                CC = SafeGetString(r, "CC") ?? "NT",
                SpecimenId = SafeGetString(r, "specimen_id") ?? string.Empty,
                PanelId = SafeGetInt(r, "panel_id"),
            };
            var ctStr = SafeGetString(r, "cell_treatment") ?? "None";
            var stStr = SafeGetString(r, "serum_treatment") ?? "None";
            rxn.CellTreatment = Enum.TryParse<CellTreatment>(ctStr, out var ct) ? ct : CellTreatment.None;
            rxn.SerumTreatment = Enum.TryParse<SerumTreatment>(stStr, out var st) ? st : SerumTreatment.None;
            return rxn;
        }

        private static Rule ReadRule(SqliteDataReader r) => new Rule
        {
            RuleId = r.GetInt32(r.GetOrdinal("rule_id")),
            Name = r.GetString(r.GetOrdinal("name")),
            Description = r.IsDBNull(r.GetOrdinal("description")) ? null : r.GetString(r.GetOrdinal("description")),
            Antibody = r.GetString(r.GetOrdinal("antibody")),
            ExceptionAntigen = r.IsDBNull(r.GetOrdinal("exception_antigen")) ? null : r.GetString(r.GetOrdinal("exception_antigen")),
            HeterozygousOk = r.GetInt32(r.GetOrdinal("heterozygous_ok")) != 0,
            MinRuleoutCount = r.GetInt32(r.GetOrdinal("min_ruleout_count")),
        };

        // ── Helpers ───────────────────────────────────────────────────────────

        private void ExecNonQuery(string sql)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        private static IEnumerable<string> GetAllAntigenColumns(Func<string, string> transform)
        {
            foreach (var ag in AntigenConstants.Antigens)
                yield return transform(AntigenMapper.GetColumn(ag));
        }

        private static string? SafeGetString(SqliteDataReader r, string col)
        {
            try
            {
                var ord = r.GetOrdinal(col);
                return r.IsDBNull(ord) ? null : r.GetString(ord);
            }
            catch { return null; }
        }

        private static int SafeGetInt(SqliteDataReader r, string col, int defaultValue = 0)
        {
            try
            {
                var ord = r.GetOrdinal(col);
                return r.IsDBNull(ord) ? defaultValue : r.GetInt32(ord);
            }
            catch { return defaultValue; }
        }

        // ── Capacity / purge ──────────────────────────────────────────────────

        public static string CutoffForKeepDays(int days) =>
            DateTime.Today.AddDays(-Math.Max(0, days)).ToString("yyyy-MM-dd");

        public long GetFileSizeBytes()
        {
            try
            {
                var info = new FileInfo(DbPath);
                info.Refresh();
                return info.Exists ? info.Length : 0;
            }
            catch
            {
                return 0;
            }
        }

        public DatabaseCapacityStatus GetCapacityStatus(long maxBytes)
        {
            if (maxBytes <= 0) maxBytes = 1;
            var fileBytes = GetFileSizeBytes();
            var percent = fileBytes * 100.0 / maxBytes;
            return new DatabaseCapacityStatus
            {
                FileBytes = fileBytes,
                MaxBytes = maxBytes,
                PercentUsed = percent,
                IsNearCapacity = percent >= DatabaseCapacityStatus.WarningPercent
            };
        }

        public void ApplyMaxPageCount(long maxBytes)
        {
            if (maxBytes <= 0) return;
            var pageSize = PragmaLong("page_size");
            if (pageSize <= 0) return;
            var pageCount = PragmaLong("page_count");
            var maxPages = maxBytes / pageSize;
            if (maxPages < pageCount) maxPages = pageCount;
            if (maxPages < 1) maxPages = 1;
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = $"PRAGMA max_page_count = {maxPages};";
            cmd.ExecuteNonQuery();
        }

        public int CountSpecimensCreatedBefore(string cutoffDate)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM specimens WHERE created_date < $cutoff";
            cmd.Parameters.AddWithValue("$cutoff", cutoffDate);
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        public PurgeResult PurgeSpecimensCreatedBefore(string cutoffDate, string? archivePath = null)
        {
            if (string.IsNullOrWhiteSpace(cutoffDate))
                throw new ArgumentException("Cutoff date is required.", nameof(cutoffDate));

            var count = CountSpecimensCreatedBefore(cutoffDate);
            if (count == 0)
            {
                return new PurgeResult
                {
                    SpecimensDeleted = 0,
                    ArchivePath = null,
                    FileSizeBytesAfter = GetFileSizeBytes()
                };
            }

            string? createdArchive = null;
            if (!string.IsNullOrWhiteSpace(archivePath))
            {
                createdArchive = Path.GetFullPath(archivePath);
                if (string.Equals(createdArchive, Path.GetFullPath(DbPath), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Archive path cannot be the live database file.");
                CreateSpecimenArchive(cutoffDate, createdArchive);
            }

            using (var cmd = _conn.CreateCommand())
            {
                cmd.CommandText = "DELETE FROM specimens WHERE created_date < $cutoff";
                cmd.Parameters.AddWithValue("$cutoff", cutoffDate);
                cmd.ExecuteNonQuery();
            }

            Vacuum();

            return new PurgeResult
            {
                SpecimensDeleted = count,
                ArchivePath = createdArchive,
                FileSizeBytesAfter = GetFileSizeBytes()
            };
        }

        public ArchiveInspection InspectArchive(string archivePath)
        {
            var path = ResolveArchivePath(archivePath);
            var liveAccessions = new HashSet<string>(StringComparer.Ordinal);
            foreach (var s in GetAllSpecimens())
                liveAccessions.Add(s.AccessionNumber);

            using var conn = OpenArchiveReadOnly(path);
            EnsureArchiveSpecimensTable(conn);

            var specimens = new List<ArchiveSpecimenRow>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT accession_number, type, created_date
                    FROM specimens
                    ORDER BY created_date, accession_number";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var acc = reader.GetString(0);
                    specimens.Add(new ArchiveSpecimenRow
                    {
                        AccessionNumber = acc,
                        Type = reader.IsDBNull(1) ? "" : reader.GetString(1),
                        CreatedDate = reader.IsDBNull(2) ? "" : reader.GetString(2),
                        ExistsInLive = liveAccessions.Contains(acc)
                    });
                }
            }

            var panelCount = 0;
            if (TableExists(conn, "panels"))
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM panels";
                panelCount = Convert.ToInt32(cmd.ExecuteScalar());
            }

            string? earliest = null;
            string? latest = null;
            if (specimens.Count > 0)
            {
                earliest = specimens.Min(s => s.CreatedDate);
                latest = specimens.Max(s => s.CreatedDate);
            }

            var info = new FileInfo(path);
            return new ArchiveInspection
            {
                Path = path,
                FileBytes = info.Exists ? info.Length : 0,
                SpecimenCount = specimens.Count,
                PanelCount = panelCount,
                EarliestCreatedDate = earliest,
                LatestCreatedDate = latest,
                AlreadyInLiveCount = specimens.Count(s => s.ExistsInLive),
                Specimens = specimens
            };
        }

        public RestoreResult RestoreArchive(string archivePath)
        {
            var path = ResolveArchivePath(archivePath);
            using (var probe = OpenArchiveReadOnly(path))
                EnsureArchiveSpecimensTable(probe);

            var skip = new HashSet<string>(StringComparer.Ordinal);
            foreach (var s in GetAllSpecimens())
                skip.Add(s.AccessionNumber);

            var panelsBefore = CountRows("panels");
            var archiveSpecimenCount = 0;
            var restored = 0;

            try
            {
                AttachArchive(path);
                FillRestoreSkipTable(skip);

                using (var cmd = _conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM archive.specimens";
                    archiveSpecimenCount = Convert.ToInt32(cmd.ExecuteScalar());
                }

                CopyFromArchive("panels");
                CopyFromArchive("panel_cells");
                if (AttachedTableExists("panel_extra_antigens"))
                    CopyFromArchive("panel_extra_antigens", "panel_id IN (SELECT panel_id FROM panels)");
                if (AttachedTableExists("panel_cell_extra_antigens"))
                    CopyFromArchive("panel_cell_extra_antigens", "cell_id IN (SELECT id FROM panel_cells)");

                restored = CopyFromArchive(
                    "specimens",
                    "accession_number NOT IN (SELECT accession_number FROM restore_skip)");
                CopyFromArchive(
                    "specimen_antibodies",
                    "specimen_id NOT IN (SELECT accession_number FROM restore_skip)");
                CopyFromArchive(
                    "specimen_ruleouts",
                    "specimen_id NOT IN (SELECT accession_number FROM restore_skip)");
                CopyFromArchive(
                    "specimen_panels",
                    "specimen_id NOT IN (SELECT accession_number FROM restore_skip)");
                CopyFromArchive(
                    "panel_runs",
                    "specimen_id NOT IN (SELECT accession_number FROM restore_skip)");
                CopyFromArchive(
                    "reactions",
                    "run_id IN (SELECT run_id FROM panel_runs)");
            }
            finally
            {
                try { ExecNonQuery("DROP TABLE IF EXISTS restore_skip"); } catch { /* ignore */ }
                try { DetachArchive(); } catch { /* ignore if not attached */ }
            }

            BumpSequence("panels", "panel_id");
            BumpSequence("panel_cells", "id");
            BumpSequence("specimen_antibodies", "id");
            BumpSequence("specimen_ruleouts", "id");
            BumpSequence("specimen_panels", "id");
            BumpSequence("panel_runs", "run_id");
            BumpSequence("reactions", "reaction_id");

            return new RestoreResult
            {
                SpecimensRestored = restored,
                SpecimensSkipped = Math.Max(0, archiveSpecimenCount - restored),
                PanelsRestored = Math.Max(0, CountRows("panels") - panelsBefore),
                FileSizeBytesAfter = GetFileSizeBytes()
            };
        }

        private string ResolveArchivePath(string archivePath)
        {
            if (string.IsNullOrWhiteSpace(archivePath))
                throw new ArgumentException("Archive path is required.", nameof(archivePath));
            var path = Path.GetFullPath(archivePath);
            if (!File.Exists(path))
                throw new FileNotFoundException("Archive file not found.", path);
            if (string.Equals(path, Path.GetFullPath(DbPath), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("That file is the live database, not an archive.");
            return path;
        }

        private static SqliteConnection OpenArchiveReadOnly(string path)
        {
            var conn = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
            try
            {
                conn.Open();
                return conn;
            }
            catch
            {
                conn.Dispose();
                throw;
            }
        }

        private static void EnsureArchiveSpecimensTable(SqliteConnection conn)
        {
            if (!TableExists(conn, "specimens"))
                throw new InvalidOperationException("This file is not a valid Antibody Panels archive.");
        }

        private static bool TableExists(SqliteConnection conn, string table)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $n LIMIT 1";
            cmd.Parameters.AddWithValue("$n", table);
            return cmd.ExecuteScalar() != null;
        }

        private bool AttachedTableExists(string table)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT 1 FROM archive.sqlite_master WHERE type = 'table' AND name = $n LIMIT 1";
            cmd.Parameters.AddWithValue("$n", table);
            return cmd.ExecuteScalar() != null;
        }

        private int CountRows(string table)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM {table}";
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        private int CopyFromArchive(string table, string? extraWhere = null)
        {
            var liveCols = GetColumnNamesOrdered(table);
            var archiveCols = new HashSet<string>(GetAttachedColumnNamesOrdered(table), StringComparer.OrdinalIgnoreCase);
            var cols = liveCols.Where(archiveCols.Contains).ToList();
            if (cols.Count == 0) return 0;
            var quoted = string.Join(", ", cols.Select(QuoteIdent));
            var sql = $"INSERT OR IGNORE INTO {table} ({quoted}) SELECT {quoted} FROM archive.{table}";
            if (!string.IsNullOrEmpty(extraWhere))
                sql += " WHERE " + extraWhere;
            return ExecNonQueryCount(sql);
        }

        private List<string> GetColumnNamesOrdered(string table)
        {
            var list = new List<string>();
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = $"PRAGMA table_info({table})";
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(r.GetString(1));
            return list;
        }

        private List<string> GetAttachedColumnNamesOrdered(string table)
        {
            var list = new List<string>();
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = $"PRAGMA archive.table_info({table})";
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(r.GetString(1));
            return list;
        }

        private static string QuoteIdent(string name) => "\"" + name.Replace("\"", "\"\"") + "\"";

        private void FillRestoreSkipTable(HashSet<string> skip)
        {
            ExecNonQuery("DROP TABLE IF EXISTS restore_skip");
            ExecNonQuery("CREATE TEMP TABLE restore_skip (accession_number TEXT PRIMARY KEY)");
            foreach (var acc in skip)
            {
                using var cmd = _conn.CreateCommand();
                cmd.CommandText = "INSERT INTO restore_skip (accession_number) VALUES ($a)";
                cmd.Parameters.AddWithValue("$a", acc);
                cmd.ExecuteNonQuery();
            }
        }

        private int ExecNonQueryCount(string sql)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = sql;
            return cmd.ExecuteNonQuery();
        }

        private void BumpSequence(string table, string idColumn)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = $@"
                INSERT INTO sqlite_sequence(name, seq)
                SELECT '{table}', COALESCE(MAX({idColumn}), 0) FROM {table}
                WHERE NOT EXISTS (SELECT 1 FROM sqlite_sequence WHERE name = '{table}');
                UPDATE sqlite_sequence
                SET seq = MAX(seq, (SELECT COALESCE(MAX({idColumn}), 0) FROM {table}))
                WHERE name = '{table}';";
            cmd.ExecuteNonQuery();
        }

        private void CreateSpecimenArchive(string cutoffDate, string archivePath)
        {
            var dir = Path.GetDirectoryName(archivePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            if (File.Exists(archivePath))
                File.Delete(archivePath);

            using (new DatabaseService(archivePath))
            {
                // Creates the same schema, then releases the file for ATTACH.
            }

            try
            {
                AttachArchive(archivePath);
                CopyArchiveTable(@"
                    INSERT INTO archive.panels
                    SELECT * FROM panels
                    WHERE panel_id IN (
                        SELECT panel_id FROM specimen_panels
                        WHERE specimen_id IN (SELECT accession_number FROM specimens WHERE created_date < $cutoff)
                        UNION
                        SELECT panel_id FROM panel_runs
                        WHERE specimen_id IN (SELECT accession_number FROM specimens WHERE created_date < $cutoff)
                    )", cutoffDate);
                ExecNonQuery(@"
                    INSERT INTO archive.panel_cells
                    SELECT * FROM panel_cells
                    WHERE panel_id IN (SELECT panel_id FROM archive.panels)");
                ExecNonQuery(@"
                    INSERT INTO archive.panel_extra_antigens
                    SELECT * FROM panel_extra_antigens
                    WHERE panel_id IN (SELECT panel_id FROM archive.panels)");
                ExecNonQuery(@"
                    INSERT INTO archive.panel_cell_extra_antigens
                    SELECT * FROM panel_cell_extra_antigens
                    WHERE cell_id IN (SELECT id FROM archive.panel_cells)");
                CopyArchiveTable(
                    "INSERT INTO archive.specimens SELECT * FROM specimens WHERE created_date < $cutoff",
                    cutoffDate);
                ExecNonQuery(@"
                    INSERT INTO archive.specimen_antibodies
                    SELECT * FROM specimen_antibodies
                    WHERE specimen_id IN (SELECT accession_number FROM archive.specimens)");
                ExecNonQuery(@"
                    INSERT INTO archive.specimen_ruleouts
                    SELECT * FROM specimen_ruleouts
                    WHERE specimen_id IN (SELECT accession_number FROM archive.specimens)");
                ExecNonQuery(@"
                    INSERT INTO archive.specimen_panels
                    SELECT * FROM specimen_panels
                    WHERE specimen_id IN (SELECT accession_number FROM archive.specimens)");
                ExecNonQuery(@"
                    INSERT INTO archive.panel_runs
                    SELECT * FROM panel_runs
                    WHERE specimen_id IN (SELECT accession_number FROM archive.specimens)");
                ExecNonQuery(@"
                    INSERT INTO archive.reactions
                    SELECT * FROM reactions
                    WHERE run_id IN (SELECT run_id FROM archive.panel_runs)");
            }
            finally
            {
                DetachArchive();
            }
        }

        private void CopyArchiveTable(string sql, string cutoffDate)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("$cutoff", cutoffDate);
            cmd.ExecuteNonQuery();
        }

        private void AttachArchive(string path)
        {
            var escaped = path.Replace("'", "''");
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = $"ATTACH DATABASE '{escaped}' AS archive";
            cmd.ExecuteNonQuery();
        }

        private void DetachArchive()
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "DETACH DATABASE archive";
            cmd.ExecuteNonQuery();
        }

        private void Vacuum()
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "VACUUM";
            cmd.ExecuteNonQuery();
        }

        private long PragmaLong(string name)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = $"PRAGMA {name};";
            var value = cmd.ExecuteScalar();
            return value == null || value is DBNull ? 0 : Convert.ToInt64(value);
        }

        public void Dispose() => _conn?.Dispose();
    }
}
