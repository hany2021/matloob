using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Matloob.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OpportunitiesInitial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "opportunities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    issuer_establishment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    opportunity_category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    city_id = table.Column<Guid>(type: "uuid", nullable: true),
                    nationality_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    description = table.Column<string>(type: "character varying(3000)", maxLength: 3000, nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    location_title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    latitude = table.Column<decimal>(type: "numeric(8,6)", nullable: false),
                    longitude = table.Column<decimal>(type: "numeric(9,6)", nullable: false),
                    required_personnel = table.Column<int>(type: "integer", nullable: false),
                    monthly_salary = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    years_of_experience_required = table.Column<byte>(type: "smallint", nullable: true),
                    working_hours_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    working_hours_from = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    working_hours_to = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    fees = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    phone_contact_information = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    email_contact_information = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    establishment_classifications = table.Column<int>(type: "integer", nullable: false),
                    genders = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ended_by_user_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
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
                    table.PrimaryKey("pk_opportunities", x => x.id);
                    table.ForeignKey(
                        name: "fk_opportunities_cities_city_id",
                        column: x => x.city_id,
                        principalTable: "cities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_opportunities_establishments_issuer_establishment_id",
                        column: x => x.issuer_establishment_id,
                        principalTable: "establishments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_opportunities_nationalities_nationality_id",
                        column: x => x.nationality_id,
                        principalTable: "nationalities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_opportunities_opportunity_categories_opportunity_category_id",
                        column: x => x.opportunity_category_id,
                        principalTable: "opportunity_categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "opportunity_assets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    opportunity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    asset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    uploaded_by_user_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
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
                    table.PrimaryKey("pk_opportunity_assets", x => x.id);
                    table.ForeignKey(
                        name: "fk_opportunity_assets_assets_asset_id",
                        column: x => x.asset_id,
                        principalTable: "assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_opportunity_assets_opportunities_opportunity_id",
                        column: x => x.opportunity_id,
                        principalTable: "opportunities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "success_management_criteria",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    opportunity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    output = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    success_criteria = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
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
                    table.PrimaryKey("pk_success_management_criteria", x => x.id);
                    table.ForeignKey(
                        name: "fk_success_management_criteria_opportunities_opportunity_id",
                        column: x => x.opportunity_id,
                        principalTable: "opportunities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "success_management_criterion_assets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    success_management_criterion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    asset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    uploaded_by_user_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
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
                    table.PrimaryKey("pk_success_management_criterion_assets", x => x.id);
                    table.ForeignKey(
                        name: "fk_success_management_criterion_assets_assets_asset_id",
                        column: x => x.asset_id,
                        principalTable: "assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_success_management_criterion_assets_success_management_crit",
                        column: x => x.success_management_criterion_id,
                        principalTable: "success_management_criteria",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_opportunities_category",
                table: "opportunities",
                column: "opportunity_category_id",
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_opportunities_city_id",
                table: "opportunities",
                column: "city_id");

            migrationBuilder.CreateIndex(
                name: "ix_opportunities_event",
                table: "opportunities",
                column: "event_id",
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_opportunities_issuer_status",
                table: "opportunities",
                columns: new[] { "issuer_establishment_id", "status" },
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_opportunities_nationality_id",
                table: "opportunities",
                column: "nationality_id");

            migrationBuilder.CreateIndex(
                name: "ix_opportunities_status",
                table: "opportunities",
                column: "status",
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_opportunity_assets_asset",
                table: "opportunity_assets",
                column: "asset_id",
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_opportunity_assets_opportunity",
                table: "opportunity_assets",
                column: "opportunity_id",
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_success_management_criteria_opportunity",
                table: "success_management_criteria",
                column: "opportunity_id",
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_smc_assets_asset",
                table: "success_management_criterion_assets",
                column: "asset_id",
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_smc_assets_criterion",
                table: "success_management_criterion_assets",
                column: "success_management_criterion_id",
                filter: "is_deleted = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "opportunity_assets");

            migrationBuilder.DropTable(
                name: "success_management_criterion_assets");

            migrationBuilder.DropTable(
                name: "success_management_criteria");

            migrationBuilder.DropTable(
                name: "opportunities");
        }
    }
}
