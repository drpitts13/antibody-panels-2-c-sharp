using AntibodyPanels.Data;
using AntibodyPanels.Models;

namespace AntibodyPanels.Tests.Infrastructure;

/// <summary>
/// Seeds realistic test data (ported from antibody-panels/seed_data.py).
/// Writes to a persistent SQLite file so data survives after test runs.
/// </summary>
public static class TestDataSeeder
{
    public const string SeedMarkerFile = ".seed_complete";

    public static void EnsureSeeded(string dbPath, bool forceReseed = false)
    {
        var markerPath = dbPath + SeedMarkerFile;
        if (!forceReseed && File.Exists(dbPath) && File.Exists(markerPath))
            return;

        if (File.Exists(dbPath))
            File.Delete(dbPath);

        using var db = new DatabaseService(dbPath);
        var panelIds = SeedPanels(db);
        SeedSpecimens(db, panelIds);
        SeedSampleReactions(db, panelIds);
        SeedRules(db);
        SeedAdditionalAnalysisScenarios(db, panelIds);

        File.WriteAllText(markerPath, DateTime.UtcNow.ToString("o"));
    }

    public static IReadOnlyList<int> SeedPanels(DatabaseService db)
    {
        var ids = new List<int>();

        ids.Add(CreatePanelWithCells(db,
            "Ortho Resolve Panel A", "ORT2024A", "Ortho Clinical Diagnostics",
            11, DaysFromNow(180), includeAc: true, startCell: 1, Panel1Cells));

        ids.Add(CreatePanelWithCells(db,
            "Immucor CAPTURE-R Ready-ID", "IMM2024B", "Immucor",
            10, DaysFromNow(200), includeAc: false, startCell: 1, Panel2Cells));

        ids.Add(CreatePanelWithCells(db,
            "Bio-Rad ID-DiaPanel", "BIO2024C", "Bio-Rad",
            11, DaysFromNow(220), includeAc: true, startCell: 1, Panel3Cells));

        ids.Add(CreatePanelWithCells(db,
            "Quotient BioResearch MicroTyping", "QUO2024D", "Quotient",
            8, DaysFromNow(150), includeAc: false, startCell: 1, Panel4Cells));

        ids.Add(CreatePanelWithCells(db,
            "Grifols DG Gel ID-Panels", "GRI2024E", "Grifols",
            11, DaysFromNow(190), includeAc: true, startCell: 1, Panel5Cells));

        return ids;
    }

    public static void SeedSpecimens(DatabaseService db, IReadOnlyList<int> panelIds)
    {
        var specimenData = new (string Accession, string Type, int DaysToExpire, int[] PanelIndices)[]
        {
            ("2024-001", "serum", 30, new[] { 0, 1 }),
            ("2024-002", "plasma", 45, new[] { 1, 2 }),
            ("2024-003", "serum", 60, new[] { 0 }),
            ("2024-004", "serum", 30, new[] { 2, 3 }),
            ("2024-005", "eluate", 15, new[] { 3 }),
            ("2024-006", "serum", 40, new[] { 0, 4 }),
            ("2024-007", "plasma", 35, new[] { 1, 4 }),
            ("2024-008", "serum", 50, new[] { 2 }),
            ("2024-009", "serum", 30, new[] { 0, 1, 2 }),
            ("2024-010", "serum", 45, new[] { 3, 4 }),
            ("2024-011", "plasma", 60, new[] { 0 }),
            ("2024-012", "serum", 30, new[] { 1 }),
            ("2024-013", "serum", 40, new[] { 2, 3 }),
            ("2024-014", "eluate", 20, new[] { 4 }),
            ("2024-015", "serum", 35, new[] { 0, 2 }),
            ("TEST-NO-AB", "serum", 90, new[] { 0 }),
            ("TEST-MULTI-AB", "serum", 90, new[] { 0 }),
        };

        foreach (var (accession, type, days, indices) in specimenData)
        {
            db.AddSpecimen(accession, type, DaysFromNow(days));
            foreach (var idx in indices)
            {
                if (idx < panelIds.Count)
                    db.LinkSpecimenPanel(accession, panelIds[idx]);
            }
        }
    }

    public static void SeedSampleReactions(DatabaseService db, IReadOnlyList<int> panelIds)
    {
        SeedAntigenPatternReactions(db, "2024-001", panelIds[0], "E",
            positiveStrengths: new[] { "2+", "3+", "3+" });
        SeedAntigenPatternReactions(db, "2024-003", panelIds[0], "K",
            positiveStrengths: new[] { "1+", "2+", "2+" });
        SeedAntigenPatternReactions(db, "2024-009", panelIds[0], "c",
            positiveStrengths: new[] { "2+", "3+", "4+" }, c37Strength: "1+");
    }

    public static void SeedAdditionalAnalysisScenarios(DatabaseService db, IReadOnlyList<int> panelIds)
    {
        // No antibody: all cells negative
        SeedAllNegativeReactions(db, "TEST-NO-AB", panelIds[0]);

        // Multiple antibodies: anti-E and anti-K on same panel
        var panelId = panelIds[0];
        var cells = db.GetPanelCells(panelId);
        foreach (var cell in cells)
        {
            if (cell.CellNumber == "AC")
            {
                db.SaveReaction("TEST-MULTI-AB", panelId, cell.CellNumber, "NT", "NT", "0", "0");
                continue;
            }

            var ePos = cell.GetAntigen("E") == "+";
            var kPos = cell.GetAntigen("K") == "+";
            if (ePos || kPos)
                db.SaveReaction("TEST-MULTI-AB", panelId, cell.CellNumber, "0", "0", "2+", "2+");
            else
                db.SaveReaction("TEST-MULTI-AB", panelId, cell.CellNumber, "0", "0", "0", "0");
        }
    }

    public static void SeedRules(DatabaseService db)
    {
        db.AddRule(
            "Anti-D C Exception",
            "When anti-D is suspected, allow C to be ruled out heterozygously due to Rh system complexity",
            "anti-D", "C", heterozygousOk: true, minRuleoutCount: 3);

        db.AddRule(
            "High-Incidence Antigen Check",
            "Require at least 5 rule-outs for high-incidence antigens to ensure reliability",
            "anti-k", null, heterozygousOk: false, minRuleoutCount: 5);

        db.AddRule(
            "Kell System Exception",
            "When anti-Kpa is suspected, allow Kpb heterozygous rule-out",
            "anti-Kpa", "Kpb", heterozygousOk: true, minRuleoutCount: 2);
    }

    private static int CreatePanelWithCells(DatabaseService db, string name, string lot,
        string vendor, int numCells, string expiration, bool includeAc, int startCell,
        string[][] cellProfiles)
    {
        var panelId = db.AddPanel(name, lot, vendor, numCells, expiration, includeAc, startCell);
        var cells = db.GetPanelCells(panelId);
        for (int i = 0; i < cellProfiles.Length && i < cells.Count; i++)
        {
            var profile = cellProfiles[i];
            for (int j = 0; j < AntigenConstants.Antigens.Count && j < profile.Length; j++)
                db.UpdatePanelCellAntigen(cells[i].Id, AntigenConstants.Antigens[j], profile[j]);
        }
        return panelId;
    }

    private static void SeedAntigenPatternReactions(DatabaseService db, string specimenId,
        int panelId, string antigen, string[] positiveStrengths, string? c37Strength = null)
    {
        var cells = db.GetPanelCells(panelId);
        var rng = new Random(specimenId.GetHashCode());
        foreach (var cell in cells)
        {
            if (cell.CellNumber == "AC")
            {
                db.SaveReaction(specimenId, panelId, cell.CellNumber, "NT", "NT", "0", "0");
                continue;
            }

            if (cell.GetAntigen(antigen) == "+")
            {
                var strength = positiveStrengths[rng.Next(positiveStrengths.Length)];
                var c37 = c37Strength ?? "0";
                db.SaveReaction(specimenId, panelId, cell.CellNumber, "0", c37, strength, strength);
            }
            else
            {
                db.SaveReaction(specimenId, panelId, cell.CellNumber, "0", "0", "0", "0");
            }
        }
    }

    private static void SeedAllNegativeReactions(DatabaseService db, string specimenId, int panelId)
    {
        foreach (var cell in db.GetPanelCells(panelId))
        {
            if (cell.CellNumber == "AC")
                db.SaveReaction(specimenId, panelId, cell.CellNumber, "NT", "NT", "0", "0");
            else
                db.SaveReaction(specimenId, panelId, cell.CellNumber, "0", "0", "0", "0");
        }
    }

    private static string DaysFromNow(int days) =>
        DateTime.Now.AddDays(days).ToString("yyyy-MM-dd");

    // Panel antigen profiles (28 antigens each): D,C,c,E,e,f,Cw,V,K,k,Kpa,Kpb,Jsa,Jsb,Jka,Jkb,Fya,Fyb,Lea,Leb,M,N,S,s,Lua,Lub,Xga,P1
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

    private static string[] Row(params string[] values) => values;
}
