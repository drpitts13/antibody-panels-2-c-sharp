using AntibodyPanels.Data;
using AntibodyPanels.Services;

namespace AntibodyPanels.Tests.Infrastructure;

/// <summary>
/// Shared fixture that seeds a persistent SQLite database used across the test suite.
/// Data remains in TestData/seeded_antibody_panels.db after tests complete.
/// </summary>
public class PersistentDatabaseFixture : IDisposable
{
    public string DbPath { get; }
    public DatabaseService Db { get; }
    public AntibodyAnalyzer Analyzer { get; }
    public ReportService Reports { get; }

    public PersistentDatabaseFixture()
    {
        var testDataDir = Path.Combine(AppContext.BaseDirectory, "TestData");
        Directory.CreateDirectory(testDataDir);
        DbPath = Path.Combine(testDataDir, "seeded_antibody_panels.db");
        TestDataSeeder.EnsureSeeded(DbPath);
        Db = new DatabaseService(DbPath);
        Analyzer = new AntibodyAnalyzer(Db);
        Reports = new ReportService(Db);
    }

    public void Dispose() => Db.Dispose();
}

[CollectionDefinition("PersistentDatabase")]
public class PersistentDatabaseCollection : ICollectionFixture<PersistentDatabaseFixture> { }
