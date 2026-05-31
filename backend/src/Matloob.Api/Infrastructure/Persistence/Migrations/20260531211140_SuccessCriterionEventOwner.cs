using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Matloob.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SuccessCriterionEventOwner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "opportunity_id",
                table: "success_management_criteria",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "event_id",
                table: "success_management_criteria",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_success_management_criteria_event",
                table: "success_management_criteria",
                column: "event_id",
                filter: "is_deleted = false");

            migrationBuilder.AddForeignKey(
                name: "fk_success_management_criteria_events_event_id",
                table: "success_management_criteria",
                column: "event_id",
                principalTable: "events",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_success_management_criteria_events_event_id",
                table: "success_management_criteria");

            migrationBuilder.DropIndex(
                name: "ix_success_management_criteria_event",
                table: "success_management_criteria");

            migrationBuilder.DropColumn(
                name: "event_id",
                table: "success_management_criteria");

            migrationBuilder.AlterColumn<Guid>(
                name: "opportunity_id",
                table: "success_management_criteria",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
