using AntibodyPanels.Data;
using AntibodyPanels.Services;

namespace AntibodyPanels.Tests.Infrastructure;

/// <summary>
/// Creates a temporary database for stress/validation tests that must not alter shared seed data.
/// </summary>
public sealed class IsolatedDatabase : IDisposable
{
    public string DbPath { get; }
    public DatabaseService Db { get; }
    public AntibodyAnalyzer Analyzer { get; }
    public ReportService Reports { get; }

    public IsolatedDatabase()
    {
        DbPath = Path.Combine(Path.GetTempPath(), $"abpanels_test_{Guid.NewGuid():N}.db");
        Db = new DatabaseService(DbPath);
        Analyzer = new AntibodyAnalyzer(Db);
        Reports = new ReportService(Db);
    }

    public void Dispose()
    {
        Db.Dispose();
        try { File.Delete(DbPath); } catch { /* best effort cleanup */ }
    }
}
