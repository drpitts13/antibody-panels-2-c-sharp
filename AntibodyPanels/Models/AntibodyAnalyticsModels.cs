using System;
using System.Collections.Generic;

namespace AntibodyPanels.Models
{
    public sealed class AntibodyCountRow
    {
        public string Antibody { get; init; } = string.Empty;
        public int Count { get; init; }
        public double PercentOfIds { get; init; }
        public string PercentDisplay => $"{PercentOfIds:F1}%";
        public int Specimens { get; init; }
    }

    public sealed class AntibodyTrendPoint
    {
        public DateTime Month { get; init; }
        public string MonthLabel => Month.ToString("yyyy-MM");
        public int SpecimenCount { get; init; }
    }

    public sealed class AntibodyAnalyticsResult
    {
        public int ConfirmedSpecimens { get; init; }
        public int DistinctSpecificities { get; init; }
        public int TotalOccurrences { get; init; }
        public string MostCommonId { get; init; } = "—";
        public IReadOnlyList<AntibodyCountRow> Rows { get; init; } = Array.Empty<AntibodyCountRow>();
        public IReadOnlyList<AntibodyTrendPoint> Trend { get; init; } = Array.Empty<AntibodyTrendPoint>();

        public static AntibodyAnalyticsResult Empty { get; } = new();
    }
}
