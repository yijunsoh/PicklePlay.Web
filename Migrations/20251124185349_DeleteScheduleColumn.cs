using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PicklePlay.Web.Migrations
{
    /// <inheritdoc />
    public partial class DeleteScheduleColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "escrow_auto_dispute_time",
                table: "schedule");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "escrow_auto_dispute_time",
                table: "schedule",
                type: "datetime(6)",
                nullable: true);
        }
    }
}
