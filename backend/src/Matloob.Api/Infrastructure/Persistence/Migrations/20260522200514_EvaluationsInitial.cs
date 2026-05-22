using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Matloob.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EvaluationsInitial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "evaluations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    opportunity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    offer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    evaluable_user_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    evaluable_establishment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    evaluator_user_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    evaluator_establishment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    rating = table.Column<byte>(type: "smallint", nullable: false),
                    recommend_for_future_opportunities = table.Column<bool>(type: "boolean", nullable: false),
                    matloob_evaluation = table.Column<byte>(type: "smallint", nullable: true),
                    matching_percentage = table.Column<int>(type: "integer", nullable: true),
                    comment = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    success_management_criteria_comment = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
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
                    table.PrimaryKey("pk_evaluations", x => x.id);
                    table.CheckConstraint("ck_evaluations_evaluable_one_of", "(evaluable_user_id IS NOT NULL AND evaluable_establishment_id IS NULL) OR (evaluable_user_id IS NULL AND evaluable_establishment_id IS NOT NULL)");
                    table.CheckConstraint("ck_evaluations_evaluator_one_of", "(evaluator_user_id IS NOT NULL AND evaluator_establishment_id IS NULL) OR (evaluator_user_id IS NULL AND evaluator_establishment_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_evaluations_establishments_evaluable_establishment_id",
                        column: x => x.evaluable_establishment_id,
                        principalTable: "establishments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_evaluations_establishments_evaluator_establishment_id",
                        column: x => x.evaluator_establishment_id,
                        principalTable: "establishments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_evaluations_offers_offer_id",
                        column: x => x.offer_id,
                        principalTable: "offers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_evaluations_opportunities_opportunity_id",
                        column: x => x.opportunity_id,
                        principalTable: "opportunities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "evaluation_assets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    evaluation_id = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("pk_evaluation_assets", x => x.id);
                    table.ForeignKey(
                        name: "fk_evaluation_assets_assets_asset_id",
                        column: x => x.asset_id,
                        principalTable: "assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_evaluation_assets_evaluations_evaluation_id",
                        column: x => x.evaluation_id,
                        principalTable: "evaluations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_evaluation_assets_asset",
                table: "evaluation_assets",
                column: "asset_id",
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_evaluation_assets_evaluation",
                table: "evaluation_assets",
                column: "evaluation_id",
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_evaluations_evaluable_establishment_id",
                table: "evaluations",
                column: "evaluable_establishment_id");

            migrationBuilder.CreateIndex(
                name: "ix_evaluations_evaluator_establishment_id",
                table: "evaluations",
                column: "evaluator_establishment_id");

            migrationBuilder.CreateIndex(
                name: "ix_evaluations_offer",
                table: "evaluations",
                column: "offer_id",
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_evaluations_opportunity",
                table: "evaluations",
                column: "opportunity_id",
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ux_evaluations_offer_evaluator_establishment",
                table: "evaluations",
                columns: new[] { "offer_id", "evaluator_establishment_id" },
                unique: true,
                filter: "evaluator_establishment_id IS NOT NULL AND is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ux_evaluations_offer_evaluator_user",
                table: "evaluations",
                columns: new[] { "offer_id", "evaluator_user_id" },
                unique: true,
                filter: "evaluator_user_id IS NOT NULL AND is_deleted = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "evaluation_assets");

            migrationBuilder.DropTable(
                name: "evaluations");
        }
    }
}
