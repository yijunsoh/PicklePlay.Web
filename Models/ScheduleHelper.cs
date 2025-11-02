using System;
using PicklePlay.Models; // Make sure to import your enums

namespace PicklePlay.Models
{
    public static class ScheduleHelper
    {
        /// <summary>
        /// Converts a Duration enum into a precise TimeSpan for calculations.
        /// </summary>
        public static TimeSpan GetTimeSpan(Duration duration)
        {
            return duration switch
            {
                Duration.H0_5 => TimeSpan.FromMinutes(30),
                Duration.H1 => TimeSpan.FromHours(1),
                Duration.H1_5 => TimeSpan.FromMinutes(90),
                Duration.H2 => TimeSpan.FromHours(2),
                Duration.H2_5 => TimeSpan.FromMinutes(150),
                Duration.H3 => TimeSpan.FromHours(3),
                Duration.H3_5 => TimeSpan.FromMinutes(210),
                Duration.H4 => TimeSpan.FromHours(4),
                Duration.H5 => TimeSpan.FromHours(5),
                Duration.H6 => TimeSpan.FromHours(6),
                Duration.H7 => TimeSpan.FromHours(7),
                Duration.H8 => TimeSpan.FromHours(8),
                Duration.D1 => TimeSpan.FromDays(1),
                Duration.D2 => TimeSpan.FromDays(2),
                Duration.D3 => TimeSpan.FromDays(3),
                _ => TimeSpan.Zero
            };
        }

        /// <summary>
        /// Converts a Duration enum into a human-readable string for display.
        /// </summary>
        public static string GetFriendlyDuration(Duration duration)
        {
            return duration switch
            {
                Duration.H0_5 => "30 min",
                Duration.H1 => "1 hr",
                Duration.H1_5 => "1 hr 30 min",
                Duration.H2 => "2 hr",
                Duration.H2_5 => "2 hr 30 min", // This is the format you wanted
                Duration.H3 => "3 hr",
                Duration.H3_5 => "3 hr 30 min",
                Duration.H4 => "4 hr",
                Duration.H5 => "5 hr",
                Duration.H6 => "6 hr",
                Duration.H7 => "7 hr",
                Duration.H8 => "8 hr",
                Duration.D1 => "1 day",
                Duration.D2 => "2 days",
                Duration.D3 => "3 days",
                _ => "N/A"
            };
        }
    }
}