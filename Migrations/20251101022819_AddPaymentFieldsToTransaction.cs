using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PicklePlay.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentFieldsToTransaction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "card_last_four",
                table: "Transaction",
                type: "varchar(50)",
                maxLength: 50,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "payment_completed_at",
                table: "Transaction",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "payment_gateway_id",
                table: "Transaction",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "payment_method",
                table: "Transaction",
                type: "varchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "payment_response",
                table: "Transaction",
                type: "text",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "payment_status",
                table: "Transaction",
                type: "varchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "card_last_four",
                table: "Transaction");

            migrationBuilder.DropColumn(
                name: "payment_completed_at",
                table: "Transaction");

            migrationBuilder.DropColumn(
                name: "payment_gateway_id",
                table: "Transaction");

            migrationBuilder.DropColumn(
                name: "payment_method",
                table: "Transaction");

            migrationBuilder.DropColumn(
                name: "payment_response",
                table: "Transaction");

            migrationBuilder.DropColumn(
                name: "payment_status",
                table: "Transaction");
        }
    }
}
