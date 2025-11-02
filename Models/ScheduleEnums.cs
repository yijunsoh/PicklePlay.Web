using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;

namespace PicklePlay.Models
{
    // Enums based on your CREATE TABLE statement
    
    public enum ScheduleType
    {
        OneOff = 0,
        Recurring = 1,
        Competition = 2
    }

    public enum EventTag
    {
        None = 0,
        [Display(Name = "Beginner-Friendly")]
        BeginnerFriendly = 1,
        Competitive = 2,
        [Display(Name = "Single-Game")]
        SingleGame = 3,
        Training = 4
    }

    public enum Duration
    {
        [Display(Name = "0.5 hr")] H0_5 = 0,
        [Display(Name = "1 hr")] H1 = 1,
        [Display(Name = "1.5 hr")] H1_5 = 2,
        [Display(Name = "2 hr")] H2 = 3,
        [Display(Name = "2.5 hr")] H2_5 = 4,
        [Display(Name = "3 hr")] H3 = 5,
        [Display(Name = "3.5 hr")] H3_5 = 6,
        [Display(Name = "4 hr")] H4 = 7,
        [Display(Name = "5 hr")] H5 = 8,
        [Display(Name = "6 hr")] H6 = 9,
        [Display(Name = "7 hr")] H7 = 10,
        [Display(Name = "8 hr")] H8 = 11,
        [Display(Name = "1 day")] D1 = 12,
        [Display(Name = "2 days")] D2 = 13,
        [Display(Name = "3 days")] D3 = 14
    }

    public enum GenderRestriction
    {
        None = 0,
        Male = 1,
        Female = 2
    }

    public enum AgeGroupRestriction
    {
        Junior = 0, // (Under 18)
        Adult = 1,  // (18-55)
        Senior = 2  // (Above 55)
    }

    public enum FeeType
    {
        None = 0,
        Free = 1,
        [Display(Name = "Auto Split Total")]
        AutoSplitTotal = 2,
        [Display(Name = "Per Person")]
        PerPerson = 3
    }

    public enum Privacy
    {
        Public = 0,
        Private = 1
    }

    public enum GameFeature
    {
        Basic = 0,
        Ranking = 1
    }

    public enum CancellationFreeze
    {
        None = 0,
        [Display(Name = "2 hr before")] B2hr = 1,
        [Display(Name = "4 hr before")] B4hr = 2,
        [Display(Name = "6 hr before")] B6hr = 3,
        [Display(Name = "8 hr before")] B8hr = 4,
        [Display(Name = "12 hr before")] B12hr = 5,
        [Display(Name = "24 hr before")] B24hr = 6
    }

    public enum Repeat
    {
        None = 0,
        [Display(Name = "Repeat for 1 week")] W1 = 1,
        [Display(Name = "Repeat for 2 weeks")] W2 = 2,
        [Display(Name = "Repeat for 3 weeks")] W3 = 3,
        [Display(Name = "Repeat for 4 weeks")] W4 = 4
    }

    // --- NEW ENUMS ADDED FROM YOUR SQL ---
    [Flags]
    public enum RecurringWeek
    {
        // Must be powers of 2 for bitwise flags
        None = 0,
        Mon = 1,
        Tue = 2,
        Wed = 4,
        Thur = 8,
        Fri = 16,
        Sat = 32,
        Sun = 64
    }

    public enum AutoCreateWhen
    {
        [Display(Name = "24hr before")] B24hr = 0,
        [Display(Name = "2d before")] B2d = 1,
        [Display(Name = "3d before")] B3d = 2,
        [Display(Name = "4d before")] B4d = 3,
        [Display(Name = "5d before")] B5d = 4,
        [Display(Name = "6d before")] B6d = 5,
        [Display(Name = "1w before")] B1w = 6,
        [Display(Name = "2w before")] B2w = 7
    }
    // --- END NEW ENUMS ---

    public enum HostRole
    {
        [Display(Name = "Host & Play")]
        HostAndPlay = 0,
        [Display(Name = "Host Only")]
        HostOnly = 1
    }

    public enum ScheduleStatus
    {
        Null = 0,
        Active = 1,
        Past = 2,
        Quit = 3,
        Cancelled = 4,
        PendingSetup = 5
    }
}