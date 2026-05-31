using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Matloob.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UserProfileSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "additional_phone",
                table: "users",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "age",
                table: "users",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "bio",
                table: "users",
                type: "character varying(250)",
                maxLength: 250,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "city_id",
                table: "users",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "date_of_birth",
                table: "users",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "gender",
                table: "users",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "id_number",
                table: "users",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "nationality_id",
                table: "users",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "photo_asset_id",
                table: "users",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "profile_completed",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "region_id",
                table: "users",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "years_of_experience",
                table: "users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "bank_accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bank_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    iban = table.Column<string>(type: "character varying(34)", maxLength: 34, nullable: false),
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
                    table.PrimaryKey("pk_bank_accounts", x => x.id);
                    table.ForeignKey(
                        name: "fk_bank_accounts_banks_bank_id",
                        column: x => x.bank_id,
                        principalTable: "banks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "supportive_documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    file_asset_id = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("pk_supportive_documents", x => x.id);
                    table.ForeignKey(
                        name: "fk_supportive_documents_assets_file_asset_id",
                        column: x => x.file_asset_id,
                        principalTable: "assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_certificates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    issued_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    issued_at = table.Column<DateOnly>(type: "date", nullable: true),
                    copy_asset_id = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("pk_user_certificates", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_certificates_assets_copy_asset_id",
                        column: x => x.copy_asset_id,
                        principalTable: "assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_education",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    degree = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    specialization = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    gpa_system = table.Column<int>(type: "integer", nullable: false),
                    gpa = table.Column<decimal>(type: "numeric(6,2)", nullable: false),
                    graduation_year = table.Column<int>(type: "integer", nullable: false),
                    copy_asset_id = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("pk_user_education", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_education_assets_copy_asset_id",
                        column: x => x.copy_asset_id,
                        principalTable: "assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_experiences",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    position = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    from = table.Column<DateOnly>(type: "date", nullable: false),
                    to = table.Column<DateOnly>(type: "date", nullable: true),
                    current = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    description = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: true),
                    type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
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
                    table.PrimaryKey("pk_user_experiences", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "user_languages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    language_id = table.Column<Guid>(type: "uuid", nullable: false),
                    level = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
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
                    table.PrimaryKey("pk_user_languages", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_languages_languages_language_id",
                        column: x => x.language_id,
                        principalTable: "languages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_professions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    opportunity_category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    other = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
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
                    table.PrimaryKey("pk_user_professions", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_professions_opportunity_categories_opportunity_categor",
                        column: x => x.opportunity_category_id,
                        principalTable: "opportunity_categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_skills",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    level = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
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
                    table.PrimaryKey("pk_user_skills", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_users_city_id",
                table: "users",
                column: "city_id");

            migrationBuilder.CreateIndex(
                name: "ix_users_nationality_id",
                table: "users",
                column: "nationality_id");

            migrationBuilder.CreateIndex(
                name: "ix_users_photo_asset_id",
                table: "users",
                column: "photo_asset_id");

            migrationBuilder.CreateIndex(
                name: "ix_users_region_id",
                table: "users",
                column: "region_id");

            migrationBuilder.CreateIndex(
                name: "ix_bank_accounts_bank_id",
                table: "bank_accounts",
                column: "bank_id");

            migrationBuilder.CreateIndex(
                name: "ux_bank_accounts_user_active",
                table: "bank_accounts",
                column: "user_id",
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_supportive_documents_file_asset_id",
                table: "supportive_documents",
                column: "file_asset_id");

            migrationBuilder.CreateIndex(
                name: "ix_supportive_documents_user_id",
                table: "supportive_documents",
                column: "user_id",
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_user_certificates_copy_asset_id",
                table: "user_certificates",
                column: "copy_asset_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_certificates_user_id",
                table: "user_certificates",
                column: "user_id",
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_user_education_copy_asset_id",
                table: "user_education",
                column: "copy_asset_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_education_user_id",
                table: "user_education",
                column: "user_id",
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_user_experiences_user_id",
                table: "user_experiences",
                column: "user_id",
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_user_languages_language_id",
                table: "user_languages",
                column: "language_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_languages_user_id",
                table: "user_languages",
                column: "user_id",
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ux_user_languages_user_language_active",
                table: "user_languages",
                columns: new[] { "user_id", "language_id" },
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_user_professions_opportunity_category_id",
                table: "user_professions",
                column: "opportunity_category_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_professions_user_id",
                table: "user_professions",
                column: "user_id",
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ux_user_professions_user_category_active",
                table: "user_professions",
                columns: new[] { "user_id", "opportunity_category_id" },
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_user_skills_user_id",
                table: "user_skills",
                column: "user_id",
                filter: "is_deleted = false");

            migrationBuilder.AddForeignKey(
                name: "fk_users_assets_photo_asset_id",
                table: "users",
                column: "photo_asset_id",
                principalTable: "assets",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_users_cities_city_id",
                table: "users",
                column: "city_id",
                principalTable: "cities",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_users_nationalities_nationality_id",
                table: "users",
                column: "nationality_id",
                principalTable: "nationalities",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_users_regions_region_id",
                table: "users",
                column: "region_id",
                principalTable: "regions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_users_assets_photo_asset_id",
                table: "users");

            migrationBuilder.DropForeignKey(
                name: "fk_users_cities_city_id",
                table: "users");

            migrationBuilder.DropForeignKey(
                name: "fk_users_nationalities_nationality_id",
                table: "users");

            migrationBuilder.DropForeignKey(
                name: "fk_users_regions_region_id",
                table: "users");

            migrationBuilder.DropTable(
                name: "bank_accounts");

            migrationBuilder.DropTable(
                name: "supportive_documents");

            migrationBuilder.DropTable(
                name: "user_certificates");

            migrationBuilder.DropTable(
                name: "user_education");

            migrationBuilder.DropTable(
                name: "user_experiences");

            migrationBuilder.DropTable(
                name: "user_languages");

            migrationBuilder.DropTable(
                name: "user_professions");

            migrationBuilder.DropTable(
                name: "user_skills");

            migrationBuilder.DropIndex(
                name: "ix_users_city_id",
                table: "users");

            migrationBuilder.DropIndex(
                name: "ix_users_nationality_id",
                table: "users");

            migrationBuilder.DropIndex(
                name: "ix_users_photo_asset_id",
                table: "users");

            migrationBuilder.DropIndex(
                name: "ix_users_region_id",
                table: "users");

            migrationBuilder.DropColumn(
                name: "additional_phone",
                table: "users");

            migrationBuilder.DropColumn(
                name: "age",
                table: "users");

            migrationBuilder.DropColumn(
                name: "bio",
                table: "users");

            migrationBuilder.DropColumn(
                name: "city_id",
                table: "users");

            migrationBuilder.DropColumn(
                name: "date_of_birth",
                table: "users");

            migrationBuilder.DropColumn(
                name: "gender",
                table: "users");

            migrationBuilder.DropColumn(
                name: "id_number",
                table: "users");

            migrationBuilder.DropColumn(
                name: "nationality_id",
                table: "users");

            migrationBuilder.DropColumn(
                name: "photo_asset_id",
                table: "users");

            migrationBuilder.DropColumn(
                name: "profile_completed",
                table: "users");

            migrationBuilder.DropColumn(
                name: "region_id",
                table: "users");

            migrationBuilder.DropColumn(
                name: "years_of_experience",
                table: "users");
        }
    }
}
