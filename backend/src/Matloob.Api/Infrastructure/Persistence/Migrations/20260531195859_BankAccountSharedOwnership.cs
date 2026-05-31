using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Matloob.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BankAccountSharedOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Ownership moves from bank_accounts.user_id to owner-side FKs. Add
            // the new owner columns first, backfill the user side from the
            // existing user_id, THEN drop the old column so no data is lost.
            migrationBuilder.AddColumn<Guid>(
                name: "bank_account_id",
                table: "users",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "bank_account_id",
                table: "establishments",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(@"
                UPDATE users u
                SET bank_account_id = ba.id
                FROM bank_accounts ba
                WHERE ba.user_id = u.id AND ba.is_deleted = false;");

            migrationBuilder.DropIndex(
                name: "ux_bank_accounts_user_active",
                table: "bank_accounts");

            migrationBuilder.DropColumn(
                name: "user_id",
                table: "bank_accounts");

            migrationBuilder.CreateIndex(
                name: "ix_users_bank_account_id",
                table: "users",
                column: "bank_account_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_establishments_bank_account_id",
                table: "establishments",
                column: "bank_account_id",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_establishments_bank_accounts_bank_account_id",
                table: "establishments",
                column: "bank_account_id",
                principalTable: "bank_accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_users_bank_accounts_bank_account_id",
                table: "users",
                column: "bank_account_id",
                principalTable: "bank_accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_establishments_bank_accounts_bank_account_id",
                table: "establishments");

            migrationBuilder.DropForeignKey(
                name: "fk_users_bank_accounts_bank_account_id",
                table: "users");

            migrationBuilder.DropIndex(
                name: "ix_users_bank_account_id",
                table: "users");

            migrationBuilder.DropIndex(
                name: "ix_establishments_bank_account_id",
                table: "establishments");

            migrationBuilder.DropColumn(
                name: "bank_account_id",
                table: "users");

            migrationBuilder.DropColumn(
                name: "bank_account_id",
                table: "establishments");

            migrationBuilder.AddColumn<Guid>(
                name: "user_id",
                table: "bank_accounts",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "ux_bank_accounts_user_active",
                table: "bank_accounts",
                column: "user_id",
                unique: true,
                filter: "is_deleted = false");
        }
    }
}
