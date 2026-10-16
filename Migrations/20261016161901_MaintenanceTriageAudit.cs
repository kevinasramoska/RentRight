using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentRight.Migrations
{
    /// <inheritdoc />
    public partial class MaintenanceTriageAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ActorUserId",
                table: "MaintenanceStatusEvents",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Note",
                table: "MaintenanceStatusEvents",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreviousStatus",
                table: "MaintenanceStatusEvents",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Priority",
                table: "MaintenanceRequests",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Normal");

            migrationBuilder.AddColumn<long>(
                name: "Revision",
                table: "MaintenanceRequests",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddCheckConstraint(
                name: "CK_MaintenanceRequests_Priority",
                table: "MaintenanceRequests",
                sql: "[Priority] IN (N'Low', N'Normal', N'High', N'Urgent')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [MaintenanceStatusEvents]
                    WHERE [ActorUserId] IS NOT NULL OR [PreviousStatus] IS NOT NULL OR [Note] IS NOT NULL)
                    OR EXISTS (SELECT 1 FROM [MaintenanceRequests] WHERE [Revision] <> 0 OR [Priority] <> N'Normal')
                    THROW 51002, 'Cannot discard maintenance triage audit data. Archive it before rollback.', 1;
                """);
            migrationBuilder.DropCheckConstraint(
                name: "CK_MaintenanceRequests_Priority",
                table: "MaintenanceRequests");

            migrationBuilder.DropColumn(
                name: "ActorUserId",
                table: "MaintenanceStatusEvents");

            migrationBuilder.DropColumn(
                name: "Note",
                table: "MaintenanceStatusEvents");

            migrationBuilder.DropColumn(
                name: "PreviousStatus",
                table: "MaintenanceStatusEvents");

            migrationBuilder.DropColumn(
                name: "Priority",
                table: "MaintenanceRequests");

            migrationBuilder.DropColumn(
                name: "Revision",
                table: "MaintenanceRequests");
        }
    }
}
