namespace PicklePlay.Helpers
{
    public static class DateTimeHelper
    {
        private static readonly TimeZoneInfo MalaysiaTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Singapore Standard Time");
        // Note: Windows uses "Singapore Standard Time" for Malaysia (GMT+8)
        // For Linux/Docker, use "Asia/Kuala_Lumpur"

        /// <summary>
        /// Gets current Malaysia time (GMT+8)
        /// </summary>
        public static DateTime GetMalaysiaTime()
        {
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, MalaysiaTimeZone);
        }

        /// <summary>
        /// Converts UTC to Malaysia time
        /// </summary>
        public static DateTime ConvertToMalaysiaTime(DateTime utcDateTime)
        {
            if (utcDateTime.Kind != DateTimeKind.Utc)
            {
                utcDateTime = DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc);
            }
            return TimeZoneInfo.ConvertTimeFromUtc(utcDateTime, MalaysiaTimeZone);
        }

        /// <summary>
        /// Converts Malaysia time to UTC
        /// </summary>
        public static DateTime ConvertToUtc(DateTime malaysiaDateTime)
        {
            return TimeZoneInfo.ConvertTimeToUtc(malaysiaDateTime, MalaysiaTimeZone);
        }

        /// <summary>
        /// Gets time zone info for Malaysia
        /// </summary>
        public static TimeZoneInfo GetMalaysiaTimeZone()
        {
            return MalaysiaTimeZone;
        }

        /// <summary>
        /// Format relative time (e.g., "2 hours ago")
        /// </summary>
        public static string GetTimeAgo(DateTime dateTime)
        {
            var now = GetMalaysiaTime();
            var timeSpan = now - dateTime;

            if (timeSpan.TotalSeconds < 60)
                return "Just now";
            if (timeSpan.TotalMinutes < 60)
                return $"{(int)timeSpan.TotalMinutes} minute{((int)timeSpan.TotalMinutes != 1 ? "s" : "")} ago";
            if (timeSpan.TotalHours < 24)
                return $"{(int)timeSpan.TotalHours} hour{((int)timeSpan.TotalHours != 1 ? "s" : "")} ago";
            if (timeSpan.TotalDays < 7)
                return $"{(int)timeSpan.TotalDays} day{((int)timeSpan.TotalDays != 1 ? "s" : "")} ago";
            if (timeSpan.TotalDays < 30)
                return $"{(int)(timeSpan.TotalDays / 7)} week{((int)(timeSpan.TotalDays / 7) != 1 ? "s" : "")} ago";
            if (timeSpan.TotalDays < 365)
                return $"{(int)(timeSpan.TotalDays / 30)} month{((int)(timeSpan.TotalDays / 30) != 1 ? "s" : "")} ago";
            
            return $"{(int)(timeSpan.TotalDays / 365)} year{((int)(timeSpan.TotalDays / 365) != 1 ? "s" : "")} ago";
        }

        /// <summary>
        /// Format date for display
        /// </summary>
        public static string FormatMalaysiaDate(DateTime dateTime, string format = "dd MMM yyyy, hh:mm tt")
        {
            var malaysiaTime = dateTime.Kind == DateTimeKind.Utc 
                ? ConvertToMalaysiaTime(dateTime) 
                : dateTime;
            return malaysiaTime.ToString(format);
        }
    }
}