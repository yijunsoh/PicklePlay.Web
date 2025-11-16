using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace PicklePlay.Models
{
    [Table("schedule")]
    public class Schedule
    {
        [Key]
        [Column("schedule_id")]
        public int ScheduleId { get; set; }

        // --- NEW: Link to the parent "Template" schedule ---
        [Column("parentScheduleId")]
        public int? ParentScheduleId { get; set; }

        // --- NEW: The date this recurrence series ends ---
        [Column("recurringEndDate")]
        public DateTime? RecurringEndDate { get; set; }

        [Column("gameName")]
        [StringLength(255)]
        public string? GameName { get; set; }

        [Column("schedule_type")] // Removed TypeName
        public ScheduleType? ScheduleType { get; set; }

        [Column("event_tag")] // Removed TypeName
        public EventTag? EventTag { get; set; } = Models.EventTag.None;

        [Column("description", TypeName = "longtext")]
        public string? Description { get; set; }

        [Column("location")]
        [StringLength(255)]
        public string? Location { get; set; }

        [Column("startTime")]
        public DateTime? StartTime { get; set; }

        [Column("endTime")]
        public DateTime? EndTime { get; set; }

        [Column("duration")] // Removed TypeName
        public Duration? Duration { get; set; }

        [Column("num_player")]
        public int? NumPlayer { get; set; }

        [Column("num_team")]
        public int? NumTeam { get; set; }

        [Column("minRankRestriction", TypeName = "decimal(5,2)")]
        public decimal? MinRankRestriction { get; set; } = 0.00m;

        [Column("maxRankRestriction", TypeName = "decimal(5,2)")]
        public decimal? MaxRankRestriction { get; set; } = 3.00m;

        [Column("genderRestriction")] // Removed TypeName
        public GenderRestriction? GenderRestriction { get; set; } = Models.GenderRestriction.None;

        [Column("ageGroupRestriction")] // Removed TypeName
        public AgeGroupRestriction? AgeGroupRestriction { get; set; } = Models.AgeGroupRestriction.None;

        [Column("feeType")] // Removed TypeName
        public FeeType? FeeType { get; set; } = Models.FeeType.PerPerson;

        [Column("feeAmount", TypeName = "decimal(8,2)")]
        public decimal? FeeAmount { get; set; }

        [Column("privacy")] // Removed TypeName
        public Privacy? Privacy { get; set; } = Models.Privacy.Public;

        [Column("gameFeature")] // Removed TypeName
        public GameFeature? GameFeature { get; set; } = Models.GameFeature.Basic;

        [Column("cancellationfreeze")] // Removed TypeName
        public CancellationFreeze? CancellationFreeze { get; set; } = Models.CancellationFreeze.None;


        // --- NEW PROPERTIES ADDED ---
        [Column("recurringWeek")]
        public RecurringWeek? RecurringWeek { get; set; }

        [Column("autoCreateWhen")]
        public AutoCreateWhen? AutoCreateWhen { get; set; } = Models.AutoCreateWhen.B2d;
        // --- END NEW PROPERTIES ---

        [Column("hostrole")] // Removed TypeName
        public HostRole? HostRole { get; set; } = Models.HostRole.HostAndPlay;

        [Column("status")] // Removed TypeName
        public ScheduleStatus? Status { get; set; } = ScheduleStatus.Null;

        [Column("approxStartTime")]
        public DateTime? ApproxStartTime { get; set; }

        [Column("regOpen")]
        public DateTime? RegOpen { get; set; }

        [Column("regClose")]
        public DateTime? RegClose { get; set; }

        [Column("earlyBirdClose")]
        public DateTime? EarlyBirdClose { get; set; }

        [Column("competitionImageUrl")]
        [StringLength(512)] // Max path length
        public string? CompetitionImageUrl { get; set; }

        // --- ADD THIS NEW PROPERTY ---
        [Column("requireOrganizerApproval")]
        public bool RequireOrganizerApproval { get; set; } = true; // Default to ON (approval is required)

        [Column("endorsementStatus")]
        public EndorsementStatus EndorsementStatus { get; set; } = EndorsementStatus.InProgress;

        [Column("community_id")]
        public int? CommunityId { get; set; }

        // --- ADD THIS NAVIGATION PROPERTY ---
        [ForeignKey("CommunityId")]
        public virtual Community? Community { get; set; }


        // --- ADD NAVIGATION PROPERTY ---
        // This will hold the related Competition data if ScheduleType is Competition
        public virtual Competition? Competition { get; set; }
        // --- END ADD ---

        // Add this line
        public virtual ICollection<ScheduleParticipant> Participants { get; set; } = new List<ScheduleParticipant>();
        public virtual ICollection<Team> Teams { get; set; } = new List<Team>();
        // --- *** THIS IS THE NEW LINE YOU MUST ADD *** ---
        public virtual ICollection<Pool> Pools { get; set; } = new List<Pool>();

        public int? CreatedByUserId { get; set; }

        [ForeignKey("CreatedByUserId")]
        public User? CreatedByUser { get; set; }

    }
}