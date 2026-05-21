using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Matloob.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AssetSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "assets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_file_name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    stored_file_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    content_type = table.Column<string>(type: "character varying(127)", maxLength: 127, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    sha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    relative_path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    storage_driver = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    visibility = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    purpose = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    owner_user_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    owner_establishment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    metadata_json = table.Column<string>(type: "jsonb", nullable: true),
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
                    table.PrimaryKey("pk_assets", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_assets_content_type",
                table: "assets",
                column: "content_type",
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_assets_owner_establishment_id",
                table: "assets",
                column: "owner_establishment_id",
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_assets_owner_user_id",
                table: "assets",
                column: "owner_user_id",
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_assets_purpose",
                table: "assets",
                column: "purpose",
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_assets_sha256",
                table: "assets",
                column: "sha256",
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_assets_visibility",
                table: "assets",
                column: "visibility",
                filter: "is_deleted = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "assets");
        }
    }
}
