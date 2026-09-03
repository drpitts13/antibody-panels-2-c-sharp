using System;

namespace AntibodyPanels.Models
{
    public static class ExpirationStatus
    {
        public static string Classify(string? expirationDate, DateTime today, int warningDays)
        {
            if (string.IsNullOrWhiteSpace(expirationDate)) return "";
            var todayStr = today.ToString("yyyy-MM-dd");
            if (string.Compare(expirationDate.Trim(), todayStr, StringComparison.Ordinal) < 0)
                return "Expired";
            if (warningDays < 0) warningDays = 0;
            var horizon = today.Date.AddDays(warningDays).ToString("yyyy-MM-dd");
            if (string.Compare(expirationDate.Trim(), horizon, StringComparison.Ordinal) <= 0)
                return "Expiring";
            return "";
        }

        public static bool IsExpired(string? expirationDate, DateTime today) =>
            Classify(expirationDate, today, 0) == "Expired";

        public static bool IsExpiringSoon(string? expirationDate, DateTime today, int warningDays) =>
            Classify(expirationDate, today, warningDays) == "Expiring";
    }
}
