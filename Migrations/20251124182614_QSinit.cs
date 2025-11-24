using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PicklePlay.Web.Migrations
{
    /// <inheritdoc />
    public partial class QSinit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "AiSuggestedPartners",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    RequestedByUserId = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SuggestedUserId = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Score = table.Column<double>(type: "double", nullable: false),
                    FeaturesJson = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ReliabilityEstimate = table.Column<double>(type: "double", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiSuggestedPartners", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "User",
                columns: table => new
                {
                    user_id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    username = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    email = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    profile_picture = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    phoneNo = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    gender = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    password = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    dateOfBirth = table.Column<DateTime>(type: "date", nullable: true),
                    age = table.Column<int>(type: "int", nullable: true),
                    bio = table.Column<string>(type: "text", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    location = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_date = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    last_login = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    emailVerify = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    role = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    email_verification_token = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    email_verified_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    verification_token_expiry = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    PasswordResetToken = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PasswordResetTokenExpiry = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_User", x => x.user_id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Community",
                columns: table => new
                {
                    community_id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    community_name = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    description = table.Column<string>(type: "text", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    createByUserId = table.Column<int>(type: "int", nullable: false),
                    community_location = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_date = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    community_type = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    lastActivityDate = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    community_pic = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    deletion_reason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    deleted_by_user_id = table.Column<int>(type: "int", nullable: true),
                    deletion_date = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    is_system_deletion = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Community", x => x.community_id);
                    table.ForeignKey(
                        name: "FK_Community_User_createByUserId",
                        column: x => x.createByUserId,
                        principalTable: "User",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Community_User_deleted_by_user_id",
                        column: x => x.deleted_by_user_id,
                        principalTable: "User",
                        principalColumn: "user_id");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CommunityRequest",
                columns: table => new
                {
                    request_id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    requestByUserId = table.Column<int>(type: "int", nullable: false),
                    communityName = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    description = table.Column<string>(type: "text", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    community_location = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    community_type = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    request_date = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    request_status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommunityRequest", x => x.request_id);
                    table.ForeignKey(
                        name: "FK_CommunityRequest_User_requestByUserId",
                        column: x => x.requestByUserId,
                        principalTable: "User",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Favorite",
                columns: table => new
                {
                    favorite_id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    user_id = table.Column<int>(type: "int", nullable: false),
                    target_user_id = table.Column<int>(type: "int", nullable: false),
                    created_date = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Favorite", x => x.favorite_id);
                    table.ForeignKey(
                        name: "FK_Favorite_User_target_user_id",
                        column: x => x.target_user_id,
                        principalTable: "User",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Favorite_User_user_id",
                        column: x => x.user_id,
                        principalTable: "User",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Friendship",
                columns: table => new
                {
                    FriendshipId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    UserOneId = table.Column<int>(type: "int", nullable: false),
                    UserTwoId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RequestDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    AcceptedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Friendship", x => x.FriendshipId);
                    table.ForeignKey(
                        name: "FK_Friendship_User_UserOneId",
                        column: x => x.UserOneId,
                        principalTable: "User",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Friendship_User_UserTwoId",
                        column: x => x.UserTwoId,
                        principalTable: "User",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Messages",
                columns: table => new
                {
                    MessageId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    SenderId = table.Column<int>(type: "int", nullable: false),
                    ReceiverId = table.Column<int>(type: "int", nullable: false),
                    Content = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SentAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsRead = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ReadAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Messages", x => x.MessageId);
                    table.ForeignKey(
                        name: "FK_Messages_User_ReceiverId",
                        column: x => x.ReceiverId,
                        principalTable: "User",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Messages_User_SenderId",
                        column: x => x.SenderId,
                        principalTable: "User",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    NotificationId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Message = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LinkUrl = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ActionUrl = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RelatedUserId = table.Column<int>(type: "int", nullable: true),
                    RelatedEntityId = table.Column<int>(type: "int", nullable: true),
                    IsRead = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ReadAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.NotificationId);
                    table.ForeignKey(
                        name: "FK_Notifications_User_RelatedUserId",
                        column: x => x.RelatedUserId,
                        principalTable: "User",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Notifications_User_UserId",
                        column: x => x.UserId,
                        principalTable: "User",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "PlayerRanks",
                columns: table => new
                {
                    RankId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Rating = table.Column<decimal>(type: "decimal(5,3)", nullable: false),
                    ReliabilityScore = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    TotalMatches = table.Column<int>(type: "int", nullable: false),
                    SinglesMatches = table.Column<int>(type: "int", nullable: false),
                    DoublesMatches = table.Column<int>(type: "int", nullable: false),
                    Wins = table.Column<int>(type: "int", nullable: false),
                    Losses = table.Column<int>(type: "int", nullable: false),
                    UniquePartners = table.Column<int>(type: "int", nullable: false),
                    UniqueOpponents = table.Column<int>(type: "int", nullable: false),
                    UniqueCommunities = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    LastUpdated = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    LastMatchDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerRanks", x => x.RankId);
                    table.ForeignKey(
                        name: "FK_PlayerRanks_User_UserId",
                        column: x => x.UserId,
                        principalTable: "User",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "User_Suspension",
                columns: table => new
                {
                    suspension_id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    user_id = table.Column<int>(type: "int", nullable: false),
                    reported_by_user_id = table.Column<int>(type: "int", nullable: false),
                    report_reason = table.Column<string>(type: "text", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    admin_decision = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    suspension_start = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    suspension_end = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    rejection_reason = table.Column<string>(type: "text", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    is_banned = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_User_Suspension", x => x.suspension_id);
                    table.ForeignKey(
                        name: "FK_User_Suspension_User_reported_by_user_id",
                        column: x => x.reported_by_user_id,
                        principalTable: "User",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_User_Suspension_User_user_id",
                        column: x => x.user_id,
                        principalTable: "User",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Wallet",
                columns: table => new
                {
                    wallet_id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    user_id = table.Column<int>(type: "int", nullable: false),
                    wallet_balance = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    escrow_balance = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    total_spent = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    last_updated = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Wallet", x => x.wallet_id);
                    table.ForeignKey(
                        name: "FK_Wallet_User_user_id",
                        column: x => x.user_id,
                        principalTable: "User",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CommunityAnnouncement",
                columns: table => new
                {
                    announcement_id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    community_id = table.Column<int>(type: "int", nullable: false),
                    poster_user_id = table.Column<int>(type: "int", nullable: false),
                    title = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    content = table.Column<string>(type: "text", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    post_date = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    expiry_date = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    is_hidden = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommunityAnnouncement", x => x.announcement_id);
                    table.ForeignKey(
                        name: "FK_CommunityAnnouncement_Community_community_id",
                        column: x => x.community_id,
                        principalTable: "Community",
                        principalColumn: "community_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CommunityAnnouncement_User_poster_user_id",
                        column: x => x.poster_user_id,
                        principalTable: "User",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CommunityBlockList",
                columns: table => new
                {
                    block_id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    community_id = table.Column<int>(type: "int", nullable: false),
                    user_id = table.Column<int>(type: "int", nullable: false),
                    blockByAdminId = table.Column<int>(type: "int", nullable: false),
                    block_reason = table.Column<string>(type: "text", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    block_date = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommunityBlockList", x => x.block_id);
                    table.ForeignKey(
                        name: "FK_CommunityBlockList_Community_community_id",
                        column: x => x.community_id,
                        principalTable: "Community",
                        principalColumn: "community_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CommunityBlockList_User_blockByAdminId",
                        column: x => x.blockByAdminId,
                        principalTable: "User",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CommunityBlockList_User_user_id",
                        column: x => x.user_id,
                        principalTable: "User",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CommunityChatMessages",
                columns: table => new
                {
                    MessageId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    CommunityId = table.Column<int>(type: "int", nullable: false),
                    SenderId = table.Column<int>(type: "int", nullable: false),
                    Content = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SentAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    DeletedByUserId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommunityChatMessages", x => x.MessageId);
                    table.ForeignKey(
                        name: "FK_CommunityChatMessages_Community_CommunityId",
                        column: x => x.CommunityId,
                        principalTable: "Community",
                        principalColumn: "community_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CommunityChatMessages_User_DeletedByUserId",
                        column: x => x.DeletedByUserId,
                        principalTable: "User",
                        principalColumn: "user_id");
                    table.ForeignKey(
                        name: "FK_CommunityChatMessages_User_SenderId",
                        column: x => x.SenderId,
                        principalTable: "User",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CommunityInvitation",
                columns: table => new
                {
                    invitation_id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    community_id = table.Column<int>(type: "int", nullable: false),
                    invitee_user_id = table.Column<int>(type: "int", nullable: false),
                    inviter_user_id = table.Column<int>(type: "int", nullable: false),
                    role = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    date_sent = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    date_responded = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommunityInvitation", x => x.invitation_id);
                    table.ForeignKey(
                        name: "FK_CommunityInvitation_Community_community_id",
                        column: x => x.community_id,
                        principalTable: "Community",
                        principalColumn: "community_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CommunityInvitation_User_invitee_user_id",
                        column: x => x.invitee_user_id,
                        principalTable: "User",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CommunityInvitation_User_inviter_user_id",
                        column: x => x.inviter_user_id,
                        principalTable: "User",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CommunityMember",
                columns: table => new
                {
                    member_id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    community_id = table.Column<int>(type: "int", nullable: false),
                    user_id = table.Column<int>(type: "int", nullable: false),
                    community_role = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    join_date = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommunityMember", x => x.member_id);
                    table.ForeignKey(
                        name: "FK_CommunityMember_Community_community_id",
                        column: x => x.community_id,
                        principalTable: "Community",
                        principalColumn: "community_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CommunityMember_User_user_id",
                        column: x => x.user_id,
                        principalTable: "User",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "schedule",
                columns: table => new
                {
                    schedule_id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    parentScheduleId = table.Column<int>(type: "int", nullable: true),
                    recurringEndDate = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    gameName = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    schedule_type = table.Column<int>(type: "int", nullable: true),
                    event_tag = table.Column<int>(type: "int", nullable: true),
                    description = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    location = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    startTime = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    endTime = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    duration = table.Column<int>(type: "int", nullable: true),
                    num_player = table.Column<int>(type: "int", nullable: true),
                    num_team = table.Column<int>(type: "int", nullable: true),
                    minRankRestriction = table.Column<decimal>(type: "decimal(5,3)", nullable: true),
                    maxRankRestriction = table.Column<decimal>(type: "decimal(5,3)", nullable: true),
                    genderRestriction = table.Column<int>(type: "int", nullable: true),
                    ageGroupRestriction = table.Column<int>(type: "int", nullable: true),
                    feeType = table.Column<int>(type: "int", nullable: true),
                    feeAmount = table.Column<decimal>(type: "decimal(8,2)", nullable: true),
                    privacy = table.Column<int>(type: "int", nullable: true),
                    cancellationfreeze = table.Column<int>(type: "int", nullable: true),
                    recurringWeek = table.Column<int>(type: "int", nullable: true),
                    hostrole = table.Column<int>(type: "int", nullable: true),
                    status = table.Column<int>(type: "int", nullable: true),
                    regOpen = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    regClose = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    earlyBirdClose = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    earlyBirdPrice = table.Column<decimal>(type: "decimal(8,2)", nullable: true),
                    competitionImageUrl = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    requireOrganizerApproval = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    endorsementStatus = table.Column<int>(type: "int", nullable: false),
                    community_id = table.Column<int>(type: "int", nullable: true),
                    escrow_status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    escrow_auto_dispute_time = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    total_escrow_amount = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_schedule", x => x.schedule_id);
                    table.ForeignKey(
                        name: "FK_schedule_Community_community_id",
                        column: x => x.community_id,
                        principalTable: "Community",
                        principalColumn: "community_id");
                    table.ForeignKey(
                        name: "FK_schedule_User_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "User",
                        principalColumn: "user_id");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Bookmark",
                columns: table => new
                {
                    BookmarkId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    ScheduleId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bookmark", x => x.BookmarkId);
                    table.ForeignKey(
                        name: "FK_Bookmark_User_UserId",
                        column: x => x.UserId,
                        principalTable: "User",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Bookmark_schedule_ScheduleId",
                        column: x => x.ScheduleId,
                        principalTable: "schedule",
                        principalColumn: "schedule_id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "competition",
                columns: table => new
                {
                    schedule_id = table.Column<int>(type: "int", nullable: false),
                    format = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    numPool = table.Column<int>(type: "int", nullable: false),
                    winnersPerPool = table.Column<int>(type: "int", nullable: false),
                    thirdPlaceMatch = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    doubleRR = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    standingCalculation = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    standardWin = table.Column<int>(type: "int", nullable: false),
                    standardLoss = table.Column<int>(type: "int", nullable: false),
                    tieBreakWin = table.Column<int>(type: "int", nullable: false),
                    tieBreakLoss = table.Column<int>(type: "int", nullable: false),
                    draw = table.Column<int>(type: "int", nullable: false),
                    drawPublished = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    matchRule = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_competition", x => x.schedule_id);
                    table.ForeignKey(
                        name: "FK_competition_schedule_schedule_id",
                        column: x => x.schedule_id,
                        principalTable: "schedule",
                        principalColumn: "schedule_id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Endorsements",
                columns: table => new
                {
                    EndorsementId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ScheduleId = table.Column<int>(type: "int", nullable: false),
                    GiverUserId = table.Column<int>(type: "int", nullable: false),
                    ReceiverUserId = table.Column<int>(type: "int", nullable: false),
                    Personality = table.Column<int>(type: "int", nullable: false),
                    Skill = table.Column<int>(type: "int", nullable: false),
                    DateGiven = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Endorsements", x => x.EndorsementId);
                    table.ForeignKey(
                        name: "FK_Endorsements_User_GiverUserId",
                        column: x => x.GiverUserId,
                        principalTable: "User",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Endorsements_User_ReceiverUserId",
                        column: x => x.ReceiverUserId,
                        principalTable: "User",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Endorsements_schedule_ScheduleId",
                        column: x => x.ScheduleId,
                        principalTable: "schedule",
                        principalColumn: "schedule_id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Escrow",
                columns: table => new
                {
                    escrow_id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    schedule_id = table.Column<int>(type: "int", nullable: false),
                    user_id = table.Column<int>(type: "int", nullable: false),
                    status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Escrow", x => x.escrow_id);
                    table.ForeignKey(
                        name: "FK_Escrow_User_user_id",
                        column: x => x.user_id,
                        principalTable: "User",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Escrow_schedule_schedule_id",
                        column: x => x.schedule_id,
                        principalTable: "schedule",
                        principalColumn: "schedule_id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Escrow_Dispute",
                columns: table => new
                {
                    dispute_id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    schedule_id = table.Column<int>(type: "int", nullable: false),
                    raisedByUserId = table.Column<int>(type: "int", nullable: false),
                    dispute_reason = table.Column<string>(type: "text", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    admin_decision = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    decision_date = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    admin_review_note = table.Column<string>(type: "text", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Escrow_Dispute", x => x.dispute_id);
                    table.ForeignKey(
                        name: "FK_Escrow_Dispute_User_raisedByUserId",
                        column: x => x.raisedByUserId,
                        principalTable: "User",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Escrow_Dispute_schedule_schedule_id",
                        column: x => x.schedule_id,
                        principalTable: "schedule",
                        principalColumn: "schedule_id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Pool",
                columns: table => new
                {
                    PoolId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ScheduleId = table.Column<int>(type: "int", nullable: false),
                    PoolName = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Pool", x => x.PoolId);
                    table.ForeignKey(
                        name: "FK_Pool_schedule_ScheduleId",
                        column: x => x.ScheduleId,
                        principalTable: "schedule",
                        principalColumn: "schedule_id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "RankMatches",
                columns: table => new
                {
                    RankMatchId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ScheduleId = table.Column<int>(type: "int", nullable: false),
                    Format = table.Column<int>(type: "int", nullable: false),
                    Team1Player1Id = table.Column<int>(type: "int", nullable: false),
                    Team1Player2Id = table.Column<int>(type: "int", nullable: true),
                    Team2Player1Id = table.Column<int>(type: "int", nullable: false),
                    Team2Player2Id = table.Column<int>(type: "int", nullable: true),
                    Team1Score = table.Column<int>(type: "int", nullable: false),
                    Team2Score = table.Column<int>(type: "int", nullable: false),
                    Team1RatingBefore = table.Column<decimal>(type: "decimal(6,3)", nullable: false),
                    Team2RatingBefore = table.Column<decimal>(type: "decimal(6,3)", nullable: false),
                    Team1RatingChange = table.Column<decimal>(type: "decimal(6,3)", nullable: false),
                    Team2RatingChange = table.Column<decimal>(type: "decimal(6,3)", nullable: false),
                    MatchDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RankMatches", x => x.RankMatchId);
                    table.ForeignKey(
                        name: "FK_RankMatches_User_Team1Player1Id",
                        column: x => x.Team1Player1Id,
                        principalTable: "User",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RankMatches_User_Team1Player2Id",
                        column: x => x.Team1Player2Id,
                        principalTable: "User",
                        principalColumn: "user_id");
                    table.ForeignKey(
                        name: "FK_RankMatches_User_Team2Player1Id",
                        column: x => x.Team2Player1Id,
                        principalTable: "User",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RankMatches_User_Team2Player2Id",
                        column: x => x.Team2Player2Id,
                        principalTable: "User",
                        principalColumn: "user_id");
                    table.ForeignKey(
                        name: "FK_RankMatches_schedule_ScheduleId",
                        column: x => x.ScheduleId,
                        principalTable: "schedule",
                        principalColumn: "schedule_id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ScheduleChatMessages",
                columns: table => new
                {
                    MessageId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ScheduleId = table.Column<int>(type: "int", nullable: false),
                    SenderId = table.Column<int>(type: "int", nullable: false),
                    Content = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SentAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    DeletedByUserId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduleChatMessages", x => x.MessageId);
                    table.ForeignKey(
                        name: "FK_ScheduleChatMessages_User_DeletedByUserId",
                        column: x => x.DeletedByUserId,
                        principalTable: "User",
                        principalColumn: "user_id");
                    table.ForeignKey(
                        name: "FK_ScheduleChatMessages_User_SenderId",
                        column: x => x.SenderId,
                        principalTable: "User",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ScheduleChatMessages_schedule_ScheduleId",
                        column: x => x.ScheduleId,
                        principalTable: "schedule",
                        principalColumn: "schedule_id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ScheduleParticipants",
                columns: table => new
                {
                    SP_Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ScheduleId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Role = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    JoinedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    RequestDate = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ResponseDate = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ReservedSlots = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduleParticipants", x => x.SP_Id);
                    table.ForeignKey(
                        name: "FK_ScheduleParticipants_User_UserId",
                        column: x => x.UserId,
                        principalTable: "User",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ScheduleParticipants_schedule_ScheduleId",
                        column: x => x.ScheduleId,
                        principalTable: "schedule",
                        principalColumn: "schedule_id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Refund_Request",
                columns: table => new
                {
                    refund_id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    escrow_id = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    reported_by = table.Column<int>(type: "int", nullable: false),
                    refund_reason = table.Column<string>(type: "text", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    admin_decision = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    admin_note = table.Column<string>(type: "text", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    decision_date = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Refund_Request", x => x.refund_id);
                    table.ForeignKey(
                        name: "FK_Refund_Request_Escrow_escrow_id",
                        column: x => x.escrow_id,
                        principalTable: "Escrow",
                        principalColumn: "escrow_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Refund_Request_User_UserId",
                        column: x => x.UserId,
                        principalTable: "User",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Transaction",
                columns: table => new
                {
                    transaction_id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    wallet_id = table.Column<int>(type: "int", nullable: false),
                    escrow_id = table.Column<int>(type: "int", nullable: true),
                    transaction_type = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    amount = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    payment_method = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    payment_status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    payment_gateway_id = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    card_last_four = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    payment_completed_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transaction", x => x.transaction_id);
                    table.ForeignKey(
                        name: "FK_Transaction_Escrow_escrow_id",
                        column: x => x.escrow_id,
                        principalTable: "Escrow",
                        principalColumn: "escrow_id");
                    table.ForeignKey(
                        name: "FK_Transaction_Wallet_wallet_id",
                        column: x => x.wallet_id,
                        principalTable: "Wallet",
                        principalColumn: "wallet_id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Team",
                columns: table => new
                {
                    TeamId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    TeamName = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TeamIconUrl = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ScheduleId = table.Column<int>(type: "int", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                    PoolId = table.Column<int>(type: "int", nullable: true),
                    BracketSeed = table.Column<int>(type: "int", nullable: true),
                    PaymentStatusForSchedule = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Team", x => x.TeamId);
                    table.ForeignKey(
                        name: "FK_Team_Pool_PoolId",
                        column: x => x.PoolId,
                        principalTable: "Pool",
                        principalColumn: "PoolId",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Team_User_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "User",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Team_schedule_ScheduleId",
                        column: x => x.ScheduleId,
                        principalTable: "schedule",
                        principalColumn: "schedule_id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "RankMatchHistories",
                columns: table => new
                {
                    HistoryId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    RankMatchId = table.Column<int>(type: "int", nullable: false),
                    Outcome = table.Column<int>(type: "int", nullable: false),
                    Format = table.Column<int>(type: "int", nullable: false),
                    PartnerId = table.Column<int>(type: "int", nullable: true),
                    RatingBefore = table.Column<decimal>(type: "decimal(5,3)", nullable: false),
                    RatingAfter = table.Column<decimal>(type: "decimal(5,3)", nullable: false),
                    RatingChange = table.Column<decimal>(type: "decimal(6,3)", nullable: false),
                    ReliabilityBefore = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    ReliabilityAfter = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    KFactorUsed = table.Column<decimal>(type: "decimal(5,3)", nullable: false),
                    MatchDate = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RankMatchHistories", x => x.HistoryId);
                    table.ForeignKey(
                        name: "FK_RankMatchHistories_RankMatches_RankMatchId",
                        column: x => x.RankMatchId,
                        principalTable: "RankMatches",
                        principalColumn: "RankMatchId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RankMatchHistories_User_PartnerId",
                        column: x => x.PartnerId,
                        principalTable: "User",
                        principalColumn: "user_id");
                    table.ForeignKey(
                        name: "FK_RankMatchHistories_User_UserId",
                        column: x => x.UserId,
                        principalTable: "User",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Awards",
                columns: table => new
                {
                    AwardId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ScheduleId = table.Column<int>(type: "int", nullable: false),
                    AwardName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AwardType = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Position = table.Column<int>(type: "int", nullable: false),
                    TeamId = table.Column<int>(type: "int", nullable: true),
                    AwardedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    SetByUserId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Awards", x => x.AwardId);
                    table.ForeignKey(
                        name: "FK_Awards_Team_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Team",
                        principalColumn: "TeamId");
                    table.ForeignKey(
                        name: "FK_Awards_User_SetByUserId",
                        column: x => x.SetByUserId,
                        principalTable: "User",
                        principalColumn: "user_id");
                    table.ForeignKey(
                        name: "FK_Awards_schedule_ScheduleId",
                        column: x => x.ScheduleId,
                        principalTable: "schedule",
                        principalColumn: "schedule_id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Matches",
                columns: table => new
                {
                    MatchId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ScheduleId = table.Column<int>(type: "int", nullable: false),
                    Team1Id = table.Column<int>(type: "int", nullable: true),
                    Team2Id = table.Column<int>(type: "int", nullable: true),
                    Team1Score = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Team2Score = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MatchTime = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Court = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RoundName = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RoundNumber = table.Column<int>(type: "int", nullable: false),
                    MatchNumber = table.Column<int>(type: "int", nullable: false),
                    WinnerId = table.Column<int>(type: "int", nullable: true),
                    IsBye = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    LastUpdatedByUserId = table.Column<int>(type: "int", nullable: true),
                    NextMatchId = table.Column<int>(type: "int", nullable: true),
                    NextLoserMatchId = table.Column<int>(type: "int", nullable: true),
                    MatchPosition = table.Column<int>(type: "int", nullable: true),
                    IsThirdPlaceMatch = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Matches", x => x.MatchId);
                    table.ForeignKey(
                        name: "FK_Matches_Team_Team1Id",
                        column: x => x.Team1Id,
                        principalTable: "Team",
                        principalColumn: "TeamId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Matches_Team_Team2Id",
                        column: x => x.Team2Id,
                        principalTable: "Team",
                        principalColumn: "TeamId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Matches_Team_WinnerId",
                        column: x => x.WinnerId,
                        principalTable: "Team",
                        principalColumn: "TeamId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Matches_User_LastUpdatedByUserId",
                        column: x => x.LastUpdatedByUserId,
                        principalTable: "User",
                        principalColumn: "user_id");
                    table.ForeignKey(
                        name: "FK_Matches_schedule_ScheduleId",
                        column: x => x.ScheduleId,
                        principalTable: "schedule",
                        principalColumn: "schedule_id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "TeamInvitation",
                columns: table => new
                {
                    InvitationId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    TeamId = table.Column<int>(type: "int", nullable: false),
                    InviterUserId = table.Column<int>(type: "int", nullable: false),
                    InviteeUserId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    DateSent = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamInvitation", x => x.InvitationId);
                    table.ForeignKey(
                        name: "FK_TeamInvitation_Team_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Team",
                        principalColumn: "TeamId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TeamInvitation_User_InviteeUserId",
                        column: x => x.InviteeUserId,
                        principalTable: "User",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TeamInvitation_User_InviterUserId",
                        column: x => x.InviterUserId,
                        principalTable: "User",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "TeamMember",
                columns: table => new
                {
                    TeamMemberId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    TeamId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamMember", x => x.TeamMemberId);
                    table.ForeignKey(
                        name: "FK_TeamMember_Team_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Team",
                        principalColumn: "TeamId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TeamMember_User_UserId",
                        column: x => x.UserId,
                        principalTable: "User",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Awards_ScheduleId",
                table: "Awards",
                column: "ScheduleId");

            migrationBuilder.CreateIndex(
                name: "IX_Awards_SetByUserId",
                table: "Awards",
                column: "SetByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Awards_TeamId",
                table: "Awards",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_Bookmark_ScheduleId",
                table: "Bookmark",
                column: "ScheduleId");

            migrationBuilder.CreateIndex(
                name: "IX_Bookmark_UserId",
                table: "Bookmark",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Community_community_name",
                table: "Community",
                column: "community_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Community_createByUserId",
                table: "Community",
                column: "createByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Community_deleted_by_user_id",
                table: "Community",
                column: "deleted_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_CommunityAnnouncement_community_id",
                table: "CommunityAnnouncement",
                column: "community_id");

            migrationBuilder.CreateIndex(
                name: "IX_CommunityAnnouncement_poster_user_id",
                table: "CommunityAnnouncement",
                column: "poster_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_CommunityBlockList_blockByAdminId",
                table: "CommunityBlockList",
                column: "blockByAdminId");

            migrationBuilder.CreateIndex(
                name: "IX_CommunityBlockList_community_id_user_id",
                table: "CommunityBlockList",
                columns: new[] { "community_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommunityBlockList_user_id",
                table: "CommunityBlockList",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_CommunityChatMessages_CommunityId",
                table: "CommunityChatMessages",
                column: "CommunityId");

            migrationBuilder.CreateIndex(
                name: "IX_CommunityChatMessages_DeletedByUserId",
                table: "CommunityChatMessages",
                column: "DeletedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CommunityChatMessages_SenderId",
                table: "CommunityChatMessages",
                column: "SenderId");

            migrationBuilder.CreateIndex(
                name: "IX_CommunityInvitation_community_id_invitee_user_id_status",
                table: "CommunityInvitation",
                columns: new[] { "community_id", "invitee_user_id", "status" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommunityInvitation_invitee_user_id",
                table: "CommunityInvitation",
                column: "invitee_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_CommunityInvitation_inviter_user_id",
                table: "CommunityInvitation",
                column: "inviter_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_CommunityMember_community_id_user_id",
                table: "CommunityMember",
                columns: new[] { "community_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommunityMember_user_id",
                table: "CommunityMember",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_CommunityRequest_requestByUserId",
                table: "CommunityRequest",
                column: "requestByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Endorsements_GiverUserId",
                table: "Endorsements",
                column: "GiverUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Endorsements_ReceiverUserId",
                table: "Endorsements",
                column: "ReceiverUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Endorsements_ScheduleId",
                table: "Endorsements",
                column: "ScheduleId");

            migrationBuilder.CreateIndex(
                name: "IX_Escrow_schedule_id",
                table: "Escrow",
                column: "schedule_id");

            migrationBuilder.CreateIndex(
                name: "IX_Escrow_user_id",
                table: "Escrow",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_Escrow_Dispute_raisedByUserId",
                table: "Escrow_Dispute",
                column: "raisedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Escrow_Dispute_schedule_id",
                table: "Escrow_Dispute",
                column: "schedule_id");

            migrationBuilder.CreateIndex(
                name: "IX_Favorite_target_user_id",
                table: "Favorite",
                column: "target_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_Favorite_user_id_target_user_id",
                table: "Favorite",
                columns: new[] { "user_id", "target_user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Friendship_UserOneId",
                table: "Friendship",
                column: "UserOneId");

            migrationBuilder.CreateIndex(
                name: "IX_Friendship_UserTwoId",
                table: "Friendship",
                column: "UserTwoId");

            migrationBuilder.CreateIndex(
                name: "IX_Matches_LastUpdatedByUserId",
                table: "Matches",
                column: "LastUpdatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Matches_ScheduleId",
                table: "Matches",
                column: "ScheduleId");

            migrationBuilder.CreateIndex(
                name: "IX_Matches_Team1Id",
                table: "Matches",
                column: "Team1Id");

            migrationBuilder.CreateIndex(
                name: "IX_Matches_Team2Id",
                table: "Matches",
                column: "Team2Id");

            migrationBuilder.CreateIndex(
                name: "IX_Matches_WinnerId",
                table: "Matches",
                column: "WinnerId");

            migrationBuilder.CreateIndex(
                name: "IX_Messages_ReceiverId",
                table: "Messages",
                column: "ReceiverId");

            migrationBuilder.CreateIndex(
                name: "IX_Messages_SenderId",
                table: "Messages",
                column: "SenderId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_RelatedUserId",
                table: "Notifications",
                column: "RelatedUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId",
                table: "Notifications",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerRanks_UserId",
                table: "PlayerRanks",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Pool_ScheduleId",
                table: "Pool",
                column: "ScheduleId");

            migrationBuilder.CreateIndex(
                name: "IX_RankMatches_ScheduleId",
                table: "RankMatches",
                column: "ScheduleId");

            migrationBuilder.CreateIndex(
                name: "IX_RankMatches_Team1Player1Id",
                table: "RankMatches",
                column: "Team1Player1Id");

            migrationBuilder.CreateIndex(
                name: "IX_RankMatches_Team1Player2Id",
                table: "RankMatches",
                column: "Team1Player2Id");

            migrationBuilder.CreateIndex(
                name: "IX_RankMatches_Team2Player1Id",
                table: "RankMatches",
                column: "Team2Player1Id");

            migrationBuilder.CreateIndex(
                name: "IX_RankMatches_Team2Player2Id",
                table: "RankMatches",
                column: "Team2Player2Id");

            migrationBuilder.CreateIndex(
                name: "IX_RankMatchHistories_PartnerId",
                table: "RankMatchHistories",
                column: "PartnerId");

            migrationBuilder.CreateIndex(
                name: "IX_RankMatchHistories_RankMatchId",
                table: "RankMatchHistories",
                column: "RankMatchId");

            migrationBuilder.CreateIndex(
                name: "IX_RankMatchHistories_UserId",
                table: "RankMatchHistories",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Refund_Request_escrow_id",
                table: "Refund_Request",
                column: "escrow_id");

            migrationBuilder.CreateIndex(
                name: "IX_Refund_Request_UserId",
                table: "Refund_Request",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_schedule_community_id",
                table: "schedule",
                column: "community_id");

            migrationBuilder.CreateIndex(
                name: "IX_schedule_CreatedByUserId",
                table: "schedule",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleChatMessages_DeletedByUserId",
                table: "ScheduleChatMessages",
                column: "DeletedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleChatMessages_ScheduleId",
                table: "ScheduleChatMessages",
                column: "ScheduleId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleChatMessages_SenderId",
                table: "ScheduleChatMessages",
                column: "SenderId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleParticipants_ScheduleId",
                table: "ScheduleParticipants",
                column: "ScheduleId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleParticipants_UserId",
                table: "ScheduleParticipants",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Team_CreatedByUserId",
                table: "Team",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Team_PoolId",
                table: "Team",
                column: "PoolId");

            migrationBuilder.CreateIndex(
                name: "IX_Team_ScheduleId",
                table: "Team",
                column: "ScheduleId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamInvitation_InviteeUserId",
                table: "TeamInvitation",
                column: "InviteeUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamInvitation_InviterUserId",
                table: "TeamInvitation",
                column: "InviterUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamInvitation_TeamId",
                table: "TeamInvitation",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamMember_TeamId",
                table: "TeamMember",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamMember_UserId",
                table: "TeamMember",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Transaction_escrow_id",
                table: "Transaction",
                column: "escrow_id");

            migrationBuilder.CreateIndex(
                name: "IX_Transaction_wallet_id",
                table: "Transaction",
                column: "wallet_id");

            migrationBuilder.CreateIndex(
                name: "IX_User_email",
                table: "User",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_User_Suspension_reported_by_user_id",
                table: "User_Suspension",
                column: "reported_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_User_Suspension_user_id",
                table: "User_Suspension",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_Wallet_user_id",
                table: "Wallet",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiSuggestedPartners");

            migrationBuilder.DropTable(
                name: "Awards");

            migrationBuilder.DropTable(
                name: "Bookmark");

            migrationBuilder.DropTable(
                name: "CommunityAnnouncement");

            migrationBuilder.DropTable(
                name: "CommunityBlockList");

            migrationBuilder.DropTable(
                name: "CommunityChatMessages");

            migrationBuilder.DropTable(
                name: "CommunityInvitation");

            migrationBuilder.DropTable(
                name: "CommunityMember");

            migrationBuilder.DropTable(
                name: "CommunityRequest");

            migrationBuilder.DropTable(
                name: "competition");

            migrationBuilder.DropTable(
                name: "Endorsements");

            migrationBuilder.DropTable(
                name: "Escrow_Dispute");

            migrationBuilder.DropTable(
                name: "Favorite");

            migrationBuilder.DropTable(
                name: "Friendship");

            migrationBuilder.DropTable(
                name: "Matches");

            migrationBuilder.DropTable(
                name: "Messages");

            migrationBuilder.DropTable(
                name: "Notifications");

            migrationBuilder.DropTable(
                name: "PlayerRanks");

            migrationBuilder.DropTable(
                name: "RankMatchHistories");

            migrationBuilder.DropTable(
                name: "Refund_Request");

            migrationBuilder.DropTable(
                name: "ScheduleChatMessages");

            migrationBuilder.DropTable(
                name: "ScheduleParticipants");

            migrationBuilder.DropTable(
                name: "TeamInvitation");

            migrationBuilder.DropTable(
                name: "TeamMember");

            migrationBuilder.DropTable(
                name: "Transaction");

            migrationBuilder.DropTable(
                name: "User_Suspension");

            migrationBuilder.DropTable(
                name: "RankMatches");

            migrationBuilder.DropTable(
                name: "Team");

            migrationBuilder.DropTable(
                name: "Escrow");

            migrationBuilder.DropTable(
                name: "Wallet");

            migrationBuilder.DropTable(
                name: "Pool");

            migrationBuilder.DropTable(
                name: "schedule");

            migrationBuilder.DropTable(
                name: "Community");

            migrationBuilder.DropTable(
                name: "User");
        }
    }
}
