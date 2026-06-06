using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Matloob.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EstablishmentInvitationsSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "establishment_invitations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    establishment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    invited_by_user_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    invited_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    accepted_by_user_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    materialized_member_id = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("pk_establishment_invitations", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_invitations_accepted_unmaterialized",
                table: "establishment_invitations",
                column: "email",
                filter: "is_deleted = false AND status = 'Accepted' AND materialized_member_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_invitations_establishment",
                table: "establishment_invitations",
                column: "establishment_id",
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ux_invitations_one_pending_per_email_per_est",
                table: "establishment_invitations",
                columns: new[] { "establishment_id", "email" },
                unique: true,
                filter: "is_deleted = false AND status = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "ux_invitations_token_hash",
                table: "establishment_invitations",
                column: "token_hash",
                unique: true,
                filter: "is_deleted = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "establishment_invitations");
        }
    }
}
