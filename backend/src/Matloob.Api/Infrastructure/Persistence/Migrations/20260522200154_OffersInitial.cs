using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Matloob.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OffersInitial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "offers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sender_establishment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    opportunity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_title_id = table.Column<Guid>(type: "uuid", nullable: true),
                    job_title_category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sponsor_establishment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    applied_by_user_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    sent_by_user_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    offer_rejection_reason_id = table.Column<Guid>(type: "uuid", nullable: true),
                    daily_wage = table.Column<int>(type: "integer", nullable: true),
                    number_of_working_days = table.Column<int>(type: "integer", nullable: true),
                    monthly_salary = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    currency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    offer_validity_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    offer_validity_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    start_date = table.Column<DateOnly>(type: "date", nullable: true),
                    end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    laborer_commitments = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    other_details = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    other_rejection_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    status = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("pk_offers", x => x.id);
                    table.ForeignKey(
                        name: "fk_offers_establishments_sender_establishment_id",
                        column: x => x.sender_establishment_id,
                        principalTable: "establishments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_offers_establishments_sponsor_establishment_id",
                        column: x => x.sponsor_establishment_id,
                        principalTable: "establishments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_offers_job_titles_job_title_id",
                        column: x => x.job_title_id,
                        principalTable: "job_titles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_offers_offer_rejection_reasons_offer_rejection_reason_id",
                        column: x => x.offer_rejection_reason_id,
                        principalTable: "offer_rejection_reasons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_offers_opportunities_opportunity_id",
                        column: x => x.opportunity_id,
                        principalTable: "opportunities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_offers_opportunity_applications_application_id",
                        column: x => x.application_id,
                        principalTable: "opportunity_applications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_offers_opportunity_categories_job_title_category_id",
                        column: x => x.job_title_category_id,
                        principalTable: "opportunity_categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "offer_cancellation_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    offer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_by_user_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    requested_by_establishment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    offer_cancellation_reason_id = table.Column<Guid>(type: "uuid", nullable: true),
                    other_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_approved = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    is_rejected = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reviewed_by_user_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_offer_cancellation_requests", x => x.id);
                    table.CheckConstraint("ck_offer_cancellation_requests_requester_one_of", "(requested_by_user_id IS NOT NULL AND requested_by_establishment_id IS NULL) OR (requested_by_user_id IS NULL AND requested_by_establishment_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_offer_cancellation_requests_establishments_requested_by_est",
                        column: x => x.requested_by_establishment_id,
                        principalTable: "establishments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_offer_cancellation_requests_offer_cancellation_reasons_offe",
                        column: x => x.offer_cancellation_reason_id,
                        principalTable: "offer_cancellation_reasons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_offer_cancellation_requests_offers_offer_id",
                        column: x => x.offer_id,
                        principalTable: "offers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_offer_cancellation_requests_offer_cancellation_reason_id",
                table: "offer_cancellation_requests",
                column: "offer_cancellation_reason_id");

            migrationBuilder.CreateIndex(
                name: "ix_offer_cancellation_requests_requested_by_establishment_id",
                table: "offer_cancellation_requests",
                column: "requested_by_establishment_id");

            migrationBuilder.CreateIndex(
                name: "ux_offer_cancellation_requests_open_per_offer",
                table: "offer_cancellation_requests",
                column: "offer_id",
                unique: true,
                filter: "is_approved = false AND is_rejected = false");

            migrationBuilder.CreateIndex(
                name: "ix_offers_application",
                table: "offers",
                column: "application_id",
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_offers_job_title_category_id",
                table: "offers",
                column: "job_title_category_id");

            migrationBuilder.CreateIndex(
                name: "ix_offers_job_title_id",
                table: "offers",
                column: "job_title_id");

            migrationBuilder.CreateIndex(
                name: "ix_offers_offer_rejection_reason_id",
                table: "offers",
                column: "offer_rejection_reason_id");

            migrationBuilder.CreateIndex(
                name: "ix_offers_opportunity_id",
                table: "offers",
                column: "opportunity_id");

            migrationBuilder.CreateIndex(
                name: "ix_offers_sender_status",
                table: "offers",
                columns: new[] { "sender_establishment_id", "status" },
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_offers_sponsor_status",
                table: "offers",
                columns: new[] { "sponsor_establishment_id", "status" },
                filter: "sponsor_establishment_id IS NOT NULL AND is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_offers_status",
                table: "offers",
                column: "status",
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_offers_validity_to",
                table: "offers",
                column: "offer_validity_to",
                filter: "is_deleted = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "offer_cancellation_requests");

            migrationBuilder.DropTable(
                name: "offers");
        }
    }
}
