using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentRight.Migrations
{
    /// <inheritdoc />
    public partial class MaintenanceStatusHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MaintenanceStatusEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MaintenanceRequestId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    OccurredUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsLegacyBaseline = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenanceStatusEvents", x => x.Id);
                    table.CheckConstraint("CK_MaintenanceStatusEvents_Status", "[Status] IN (N'Submitted', N'InProgress', N'Resolved', N'Closed')");
                    table.ForeignKey(
                        name: "FK_MaintenanceStatusEvents_MaintenanceRequests_MaintenanceRequestId",
                        column: x => x.MaintenanceRequestId,
                        principalTable: "MaintenanceRequests",
                        principalColumn: "Id");
                });

            // Only the current persisted state is known. Do not fabricate a Submitted
            // transition for requests that have already progressed.
            migrationBuilder.Sql("""
                INSERT INTO [MaintenanceStatusEvents] ([MaintenanceRequestId], [Status], [OccurredUtc], [IsLegacyBaseline])
                SELECT [Id], [Status], CASE WHEN [LastUpdatedUtc] >= [DateSubmitted]
                    THEN [LastUpdatedUtc] ELSE [DateSubmitted] END, CAST(1 AS bit)
                FROM [MaintenanceRequests];
                """);

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceStatusEvents_MaintenanceRequestId_OccurredUtc_Id",
                table: "MaintenanceStatusEvents",
                columns: new[] { "MaintenanceRequestId", "OccurredUtc", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [MaintenanceStatusEvents] WHERE [IsLegacyBaseline] = 0)
                    THROW 51001, 'Cannot remove recorded maintenance status history. Archive it before rollback.', 1;
                """);
            migrationBuilder.DropTable(
                name: "MaintenanceStatusEvents");
        }
    }
}
