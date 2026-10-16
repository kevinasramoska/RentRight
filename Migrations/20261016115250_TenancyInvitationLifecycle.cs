using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentRight.Migrations
{
    /// <inheritdoc />
    public partial class TenancyInvitationLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Tenancies_PropertyId",
                table: "Tenancies");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Tenancies_Status",
                table: "Tenancies");

            migrationBuilder.AlterColumn<string>(
                name: "TenantId",
                table: "Tenancies",
                type: "nvarchar(450)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AddColumn<string>(
                name: "AcceptedByUserId",
                table: "Tenancies",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AcceptedUtc",
                table: "Tenancies",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EndReason",
                table: "Tenancies",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EndedByUserId",
                table: "Tenancies",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EndedUtc",
                table: "Tenancies",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "InvitationExpiresUtc",
                table: "Tenancies",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InvitationTokenHash",
                table: "Tenancies",
                type: "varchar(64)",
                unicode: false,
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InvitedByUserId",
                table: "Tenancies",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "InvitedUtc",
                table: "Tenancies",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Tenancies",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.CreateIndex(
                name: "IX_Tenancies_InvitationTokenHash",
                table: "Tenancies",
                column: "InvitationTokenHash",
                unique: true,
                filter: "[InvitationTokenHash] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Tenancies_PropertyId",
                table: "Tenancies",
                column: "PropertyId",
                unique: true,
                filter: "[Status] IN (N'Active', N'Pending')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Tenancies_ActiveTenant",
                table: "Tenancies",
                sql: "[Status] <> N'Active' OR [TenantId] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Tenancies_Invitation",
                table: "Tenancies",
                sql: "[Status] <> N'Pending' OR ([InvitationTokenHash] IS NOT NULL AND [InvitedUtc] IS NOT NULL AND [InvitationExpiresUtc] IS NOT NULL AND [InvitationExpiresUtc] > [InvitedUtc] AND [InvitedByUserId] IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Tenancies_Status",
                table: "Tenancies",
                sql: "[Status] IN (N'Active', N'Ended', N'Pending')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [Tenancies] WHERE [InvitedUtc] IS NOT NULL OR [AcceptedUtc] IS NOT NULL OR [EndedUtc] IS NOT NULL OR [TenantId] IS NULL)
                    THROW 51000, 'Tenancy lifecycle audit data exists. Export and review it before an explicitly approved rollback; no lifecycle data will be discarded automatically.', 1;
                """);
            migrationBuilder.DropIndex(
                name: "IX_Tenancies_InvitationTokenHash",
                table: "Tenancies");

            migrationBuilder.DropIndex(
                name: "IX_Tenancies_PropertyId",
                table: "Tenancies");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Tenancies_ActiveTenant",
                table: "Tenancies");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Tenancies_Invitation",
                table: "Tenancies");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Tenancies_Status",
                table: "Tenancies");

            migrationBuilder.DropColumn(
                name: "AcceptedByUserId",
                table: "Tenancies");

            migrationBuilder.DropColumn(
                name: "AcceptedUtc",
                table: "Tenancies");

            migrationBuilder.DropColumn(
                name: "EndReason",
                table: "Tenancies");

            migrationBuilder.DropColumn(
                name: "EndedByUserId",
                table: "Tenancies");

            migrationBuilder.DropColumn(
                name: "EndedUtc",
                table: "Tenancies");

            migrationBuilder.DropColumn(
                name: "InvitationExpiresUtc",
                table: "Tenancies");

            migrationBuilder.DropColumn(
                name: "InvitationTokenHash",
                table: "Tenancies");

            migrationBuilder.DropColumn(
                name: "InvitedByUserId",
                table: "Tenancies");

            migrationBuilder.DropColumn(
                name: "InvitedUtc",
                table: "Tenancies");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Tenancies");

            migrationBuilder.AlterColumn<string>(
                name: "TenantId",
                table: "Tenancies",
                type: "nvarchar(450)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tenancies_PropertyId",
                table: "Tenancies",
                column: "PropertyId",
                unique: true,
                filter: "[Status] = N'Active'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Tenancies_Status",
                table: "Tenancies",
                sql: "[Status] IN (N'Active', N'Ended')");
        }
    }
}
