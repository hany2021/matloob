using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Matloob.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ApplicationsInitial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "opportunity_applications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    opportunity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    applicant_user_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    applicant_establishment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    applied_by_user_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_opportunity_applications", x => x.id);
                    table.CheckConstraint("ck_opportunity_applications_applicant_one_of", "(applicant_user_id IS NOT NULL AND applicant_establishment_id IS NULL) OR (applicant_user_id IS NULL AND applicant_establishment_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_opportunity_applications_establishments_applicant_establish",
                        column: x => x.applicant_establishment_id,
                        principalTable: "establishments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_opportunity_applications_opportunities_opportunity_id",
                        column: x => x.opportunity_id,
                        principalTable: "opportunities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_opportunity_applications_applicant_establishment_id",
                table: "opportunity_applications",
                column: "applicant_establishment_id");

            migrationBuilder.CreateIndex(
                name: "ix_opportunity_applications_opportunity",
                table: "opportunity_applications",
                column: "opportunity_id",
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ux_opportunity_applications_establishment_active",
                table: "opportunity_applications",
                columns: new[] { "opportunity_id", "applicant_establishment_id" },
                unique: true,
                filter: "applicant_establishment_id IS NOT NULL AND is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ux_opportunity_applications_user_active",
                table: "opportunity_applications",
                columns: new[] { "opportunity_id", "applicant_user_id" },
                unique: true,
                filter: "applicant_user_id IS NOT NULL AND is_deleted = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "opportunity_applications");
        }
    }
}
