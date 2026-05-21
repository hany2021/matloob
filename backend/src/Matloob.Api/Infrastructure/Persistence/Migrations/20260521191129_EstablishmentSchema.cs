using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Matloob.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EstablishmentSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "establishment_change_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    establishment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_by_user_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reviewed_by_admin_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    review_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    applied_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    proposed_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    proposed_commercial_registration_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    proposed_labor_office_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    proposed_sequence_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    proposed_city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    proposed_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    proposed_phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    proposed_commercial_registration_expiry = table.Column<DateOnly>(type: "date", nullable: true),
                    proposed_economic_activity = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    proposed_sub_economic_activity = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    proposed_district = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    proposed_area = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    proposed_street = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    proposed_description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    proposed_location_title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    proposed_latitude = table.Column<decimal>(type: "numeric(8,6)", nullable: true),
                    proposed_longitude = table.Column<decimal>(type: "numeric(9,6)", nullable: true),
                    proposed_building_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    proposed_postal_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    proposed_additional_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    proposed_website = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    proposed_years_of_experience = table.Column<int>(type: "integer", nullable: true),
                    proposed_establishment_size = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    proposed_additional_contact_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    proposed_authorization_letter_asset_id = table.Column<Guid>(type: "uuid", nullable: true),
                    proposed_commercial_registration_asset_id = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("pk_establishment_change_requests", x => x.id);
                    table.ForeignKey(
                        name: "fk_establishment_change_requests_assets_proposed_authorization",
                        column: x => x.proposed_authorization_letter_asset_id,
                        principalTable: "assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_establishment_change_requests_assets_proposed_commercial_re",
                        column: x => x.proposed_commercial_registration_asset_id,
                        principalTable: "assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "establishment_documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    establishment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
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
                    table.PrimaryKey("pk_establishment_documents", x => x.id);
                    table.ForeignKey(
                        name: "fk_establishment_documents_assets_asset_id",
                        column: x => x.asset_id,
                        principalTable: "assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "establishment_members",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    establishment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    added_by_user_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    added_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
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
                    table.PrimaryKey("pk_establishment_members", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "establishment_review_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    establishment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    change_request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    actor_user_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    actor_admin_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    snapshot_json = table.Column<string>(type: "jsonb", nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_establishment_review_history", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "establishments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    commercial_registration_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    labor_office_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    sequence_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    commercial_registration_expiry = table.Column<DateOnly>(type: "date", nullable: true),
                    economic_activity = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    sub_economic_activity = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    district = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    area = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    street = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    location_title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    latitude = table.Column<decimal>(type: "numeric(8,6)", nullable: true),
                    longitude = table.Column<decimal>(type: "numeric(9,6)", nullable: true),
                    building_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    postal_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    additional_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    website = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    years_of_experience = table.Column<int>(type: "integer", nullable: true),
                    establishment_size = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    additional_contact_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    is_sponsor = table.Column<bool>(type: "boolean", nullable: false),
                    can_manage_events = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_by_user_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    is_legacy_import = table.Column<bool>(type: "boolean", nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    approved_by_admin_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    rejected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    rejected_by_admin_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    rejection_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    suspended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    suspended_by_admin_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    suspension_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
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
                    table.PrimaryKey("pk_establishments", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_establishment_change_requests_created_by_user_id",
                table: "establishment_change_requests",
                column: "created_by_user_id",
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_establishment_change_requests_proposed_authorization_letter",
                table: "establishment_change_requests",
                column: "proposed_authorization_letter_asset_id");

            migrationBuilder.CreateIndex(
                name: "ix_establishment_change_requests_proposed_commercial_registrat",
                table: "establishment_change_requests",
                column: "proposed_commercial_registration_asset_id");

            migrationBuilder.CreateIndex(
                name: "ix_establishment_change_requests_status",
                table: "establishment_change_requests",
                column: "status",
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ux_establishment_change_requests_pending_per_estab",
                table: "establishment_change_requests",
                column: "establishment_id",
                unique: true,
                filter: "status = 'PendingReview' AND is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_establishment_documents_asset_id",
                table: "establishment_documents",
                column: "asset_id",
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_establishment_documents_establishment_id",
                table: "establishment_documents",
                column: "establishment_id",
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ux_establishment_documents_slot_active",
                table: "establishment_documents",
                columns: new[] { "establishment_id", "document_type" },
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_establishment_members_establishment_id",
                table: "establishment_members",
                column: "establishment_id",
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_establishment_members_role",
                table: "establishment_members",
                column: "role",
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_establishment_members_user_id",
                table: "establishment_members",
                column: "user_id",
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ux_establishment_members_pair_active",
                table: "establishment_members",
                columns: new[] { "establishment_id", "user_id" },
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_establishment_review_history_action",
                table: "establishment_review_history",
                column: "action");

            migrationBuilder.CreateIndex(
                name: "ix_establishment_review_history_change_request_id",
                table: "establishment_review_history",
                column: "change_request_id");

            migrationBuilder.CreateIndex(
                name: "ix_establishment_review_history_estab_time",
                table: "establishment_review_history",
                columns: new[] { "establishment_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_establishments_created_by_user_id",
                table: "establishments",
                column: "created_by_user_id",
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_establishments_status",
                table: "establishments",
                column: "status",
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ux_establishments_cr_active",
                table: "establishments",
                column: "commercial_registration_number",
                unique: true,
                filter: "status IN ('PendingReview','Approved','Suspended') AND is_deleted = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "establishment_change_requests");

            migrationBuilder.DropTable(
                name: "establishment_documents");

            migrationBuilder.DropTable(
                name: "establishment_members");

            migrationBuilder.DropTable(
                name: "establishment_review_history");

            migrationBuilder.DropTable(
                name: "establishments");
        }
    }
}
