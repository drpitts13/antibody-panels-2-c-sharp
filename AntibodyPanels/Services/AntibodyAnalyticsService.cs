using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using AntibodyPanels.Models;

namespace AntibodyPanels.Services
{
    public static class AntibodyAnalyticsService
    {
        private static readonly string[] IdentifiedAtFormats =
        {
            "yyyy-MM-dd HH:mm",
            "yyyy-MM-dd",
            "yyyy-MM-ddTHH:mm:ss",
            "yyyy-MM-ddTHH:mm"
        };

        public static AntibodyAnalyticsResult Compute(
            IEnumerable<Specimen> specimens,
            DateTime? from,
            DateTime? to)
        {
            var confirmed = new List<(Specimen Specimen, DateTime? IdentifiedAt)>();
            var hasRange = from.HasValue || to.HasValue;

            foreach (var specimen in specimens)
            {
                if (!specimen.HasFinalCall) continue;
                var parsed = TryParseIdentifiedAt(specimen.IdentifiedAt, out var at);
                if (hasRange)
                {
                    if (!parsed) continue;
                    if (from.HasValue && at.Date < from.Value.Date) continue;
                    if (to.HasValue && at.Date > to.Value.Date) continue;
                    confirmed.Add((specimen, at));
                }
                else
                {
                    confirmed.Add((specimen, parsed ? at : null));
                }
            }

            if (confirmed.Count == 0)
                return AntibodyAnalyticsResult.Empty;

            var groups = new Dictionary<string, SpecificityAccumulator>(StringComparer.OrdinalIgnoreCase);
            foreach (var (specimen, _) in confirmed)
            {
                var parts = FinalAntibodyParser.Split(specimen.FinalAntibodies)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                foreach (var part in parts)
                {
                    if (!groups.TryGetValue(part, out var acc))
                    {
                        acc = new SpecificityAccumulator();
                        groups[part] = acc;
                    }
                    acc.Count++;
                    acc.NoteCasing(part);
                }
            }

            var totalOccurrences = groups.Values.Sum(g => g.Count);
            var rows = groups
                .Select(kv => new AntibodyCountRow
                {
                    Antibody = kv.Value.DisplayName,
                    Count = kv.Value.Count,
                    Specimens = kv.Value.Count,
                    PercentOfIds = totalOccurrences == 0 ? 0 : 100.0 * kv.Value.Count / totalOccurrences
                })
                .OrderByDescending(r => r.Count)
                .ThenBy(r => r.Antibody, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var mostCommon = rows.Count == 0 ? "—" : rows[0].Antibody;
            var trend = BuildTrend(confirmed, from, to);

            return new AntibodyAnalyticsResult
            {
                ConfirmedSpecimens = confirmed.Count,
                DistinctSpecificities = rows.Count,
                TotalOccurrences = totalOccurrences,
                MostCommonId = mostCommon,
                Rows = rows,
                Trend = trend
            };
        }

        public static void ExportToCsv(
            AntibodyAnalyticsResult result,
            DateTime? from,
            DateTime? to,
            string filePath)
        {
            var fromText = from?.ToString("yyyy-MM-dd") ?? "";
            var toText = to?.ToString("yyyy-MM-dd") ?? "";
            var config = new CsvConfiguration(CultureInfo.InvariantCulture);
            using var writer = new StreamWriter(filePath, false, Encoding.UTF8);
            using var csv = new CsvWriter(writer, config);
            csv.WriteRecords(result.Rows.Select(r => new
            {
                r.Antibody,
                r.Count,
                PercentOfIds = r.PercentDisplay,
                r.Specimens,
                From = fromText,
                To = toText
            }));
        }

        public static bool TryParseIdentifiedAt(string? text, out DateTime identifiedAt)
        {
            identifiedAt = default;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            if (DateTime.TryParseExact(text.Trim(), IdentifiedAtFormats, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out identifiedAt))
                return true;

            return DateTime.TryParse(text.Trim(), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal, out identifiedAt);
        }

        private static List<AntibodyTrendPoint> BuildTrend(
            List<(Specimen Specimen, DateTime? IdentifiedAt)> confirmed,
            DateTime? from,
            DateTime? to)
        {
            var dated = confirmed
                .Where(c => c.IdentifiedAt.HasValue)
                .Select(c => c.IdentifiedAt!.Value)
                .ToList();
            if (dated.Count == 0)
                return new List<AntibodyTrendPoint>();

            var start = new DateTime((from ?? dated.Min()).Year, (from ?? dated.Min()).Month, 1);
            var end = new DateTime((to ?? dated.Max()).Year, (to ?? dated.Max()).Month, 1);
            if (end < start)
                (start, end) = (end, start);

            var counts = dated
                .GroupBy(d => new DateTime(d.Year, d.Month, 1))
                .ToDictionary(g => g.Key, g => g.Count());

            var points = new List<AntibodyTrendPoint>();
            for (var month = start; month <= end; month = month.AddMonths(1))
            {
                points.Add(new AntibodyTrendPoint
                {
                    Month = month,
                    SpecimenCount = counts.TryGetValue(month, out var n) ? n : 0
                });
            }
            return points;
        }

        private sealed class SpecificityAccumulator
        {
            public int Count { get; set; }
            private readonly Dictionary<string, int> _casings = new(StringComparer.Ordinal);

            public void NoteCasing(string name)
            {
                _casings[name] = _casings.TryGetValue(name, out var n) ? n + 1 : 1;
            }

            public string DisplayName =>
                _casings
                    .OrderByDescending(kv => kv.Value)
                    .ThenBy(kv => kv.Key, StringComparer.Ordinal)
                    .Select(kv => kv.Key)
                    .FirstOrDefault() ?? "";
        }
    }
}
