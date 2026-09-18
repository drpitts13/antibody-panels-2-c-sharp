# SOUP / OTS software inventory

From [AntibodyPanels.csproj](../../AntibodyPanels/AntibodyPanels.csproj) and the .NET 8 runtime.

| Component | Version | Purpose | Anomaly / residual note |
|---|---|---|---|
| .NET 8 Windows runtime | 8.x | Execution environment | Microsoft support lifecycle |
| Microsoft.Data.Sqlite | 10.0.7 | Persistence | Parameterized SQL; FK on |
| CsvHelper | 33.1.0 | CSV import/export | Not used for identification math |
| MathNet.Numerics | 5.0.0 | Referenced; Fisher implementation is custom in `AntibodyAnalyzer` | Unused SOUP until adopted |
| PDFsharp-WPF | 6.2.4 | PDF reports | Rendering only |
| PdfPig | 0.1.16 | PDF read (vendor import) | Input parsing |
| ScottPlot.WPF | 5.0.55 | Analytics charts | Display only |

Cybersecurity posture for this local app:

- Relies on Windows interactive logon.
- Database file ACL checked in `HipaaSecurityTests`.
- Vendor HTTP client restricted to HTTPS and an allow-list.
- No inbound server.
- Application log omits reaction grades and comments.
