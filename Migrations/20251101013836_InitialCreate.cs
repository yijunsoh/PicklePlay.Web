using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PicklePlay.Web.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
                name: "Escrow_Dispute",
                columns: table => new
                {
                    dispute_id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    escrow_id = table.Column<int>(type: "int", nullable: false),
                    raisedByUserId = table.Column<int>(type: "int", nullable: false),
                    dispute_reason = table.Column<string>(type: "text", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    admin_decision = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    decision_date = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    refund_process = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Escrow_Dispute", x => x.dispute_id);
                    table.ForeignKey(
                        name: "FK_Escrow_Dispute_Escrow_escrow_id",
                        column: x => x.escrow_id,
                        principalTable: "Escrow",
                        principalColumn: "escrow_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Escrow_Dispute_User_raisedByUserId",
                        column: x => x.raisedByUserId,
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
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false)
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

            migrationBuilder.CreateIndex(
                name: "IX_Escrow_user_id",
                table: "Escrow",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_Escrow_Dispute_escrow_id",
                table: "Escrow_Dispute",
                column: "escrow_id");

            migrationBuilder.CreateIndex(
                name: "IX_Escrow_Dispute_raisedByUserId",
                table: "Escrow_Dispute",
                column: "raisedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Transaction_escrow_id",
                table: "Transaction",
                column: "escrow_id");

            migrationBuilder.CreateIndex(
                name: "IX_Transaction_wallet_id",
                table: "Transaction",
                column: "wallet_id");

            migrationBuilder.CreateIndex(
                name: "IX_Wallet_user_id",
                table: "Wallet",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Escrow_Dispute");

            migrationBuilder.DropTable(
                name: "Transaction");

            migrationBuilder.DropTable(
                name: "Escrow");

            migrationBuilder.DropTable(
                name: "Wallet");
        }
    }
}
