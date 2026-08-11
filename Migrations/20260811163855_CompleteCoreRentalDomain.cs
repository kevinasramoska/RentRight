using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentRight.Migrations
{
    /// <inheritdoc />
    public partial class CompleteCoreRentalDomain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM [Properties] WHERE LEN([Title]) > 200)
                    THROW 51000, 'Property titles longer than 200 characters must be corrected before migrating.', 1;
                IF EXISTS (SELECT 1 FROM [Properties] WHERE LEN([Address]) > 500)
                    THROW 51000, 'Property addresses longer than 500 characters must be corrected before migrating.', 1;
                IF EXISTS (SELECT 1 FROM [Properties] WHERE LEN([Description]) > 2000)
                    THROW 51000, 'Property descriptions longer than 2000 characters must be corrected before migrating.', 1;
                IF EXISTS (SELECT 1 FROM [MaintenanceRequests] WHERE LEN([Description]) > 2000)
                    THROW 51000, 'Maintenance descriptions longer than 2000 characters must be corrected before migrating.', 1;
                IF EXISTS (SELECT 1 FROM [AspNetUsers] WHERE LEN([FullName]) > 200)
                    THROW 51000, 'User full names longer than 200 characters must be corrected before migrating.', 1;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_MaintenanceRequests_AspNetUsers_TenantId",
                table: "MaintenanceRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_MaintenanceRequests_Properties_PropertyId",
                table: "MaintenanceRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_Properties_AspNetUsers_LandlordId",
                table: "Properties");

            migrationBuilder.CreateTable(
                name: "Tenancies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PropertyId = table.Column<int>(type: "int", nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenancies", x => x.Id);
                    table.CheckConstraint("CK_Tenancies_EndDate", "[EndDate] IS NULL OR [EndDate] >= [StartDate]");
                    table.CheckConstraint("CK_Tenancies_Status", "[Status] IN (N'Active', N'Ended')");
                    table.ForeignKey(
                        name: "FK_Tenancies_AspNetUsers_TenantId",
                        column: x => x.TenantId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Tenancies_Properties_PropertyId",
                        column: x => x.PropertyId,
                        principalTable: "Properties",
                        principalColumn: "Id");
                });

            migrationBuilder.AddColumn<int>(
                name: "TenancyId",
                table: "MaintenanceRequests",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastUpdatedUtc",
                table: "MaintenanceRequests",
                type: "datetime2",
                nullable: true);

            // Preserve existing requests as history by creating one ended tenancy per legacy
            // property/tenant pair before removing the redundant foreign keys.
            migrationBuilder.Sql(
                """
                INSERT INTO [Tenancies] ([PropertyId], [TenantId], [StartDate], [EndDate], [Status])
                SELECT
                    [PropertyId],
                    [TenantId],
                    CONVERT(date, MIN([DateSubmitted])),
                    CONVERT(date, MAX([DateSubmitted])),
                    N'Ended'
                FROM [MaintenanceRequests]
                GROUP BY [PropertyId], [TenantId];

                UPDATE request
                SET
                    request.[TenancyId] = tenancy.[Id],
                    request.[LastUpdatedUtc] = request.[DateSubmitted],
                    request.[Status] = CASE
                        WHEN request.[Status] = N'Pending' THEN N'Submitted'
                        ELSE request.[Status]
                    END
                FROM [MaintenanceRequests] request
                INNER JOIN [Tenancies] tenancy
                    ON tenancy.[PropertyId] = request.[PropertyId]
                    AND tenancy.[TenantId] = request.[TenantId];

                IF EXISTS (
                    SELECT 1
                    FROM [MaintenanceRequests]
                    WHERE [TenancyId] IS NULL OR [LastUpdatedUtc] IS NULL)
                    THROW 51000, 'Existing maintenance requests could not be linked to migrated tenancies.', 1;

                IF EXISTS (
                    SELECT 1
                    FROM [MaintenanceRequests]
                    WHERE [Status] NOT IN (N'Submitted', N'InProgress', N'Resolved', N'Closed'))
                    THROW 51000, 'Unsupported maintenance statuses must be corrected before migrating.', 1;
                """);

            migrationBuilder.DropIndex(
                name: "IX_MaintenanceRequests_PropertyId",
                table: "MaintenanceRequests");

            migrationBuilder.DropIndex(
                name: "IX_MaintenanceRequests_TenantId",
                table: "MaintenanceRequests");

            migrationBuilder.DropColumn(
                name: "PropertyId",
                table: "MaintenanceRequests");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "MaintenanceRequests");

            migrationBuilder.AlterColumn<int>(
                name: "TenancyId",
                table: "MaintenanceRequests",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "LastUpdatedUtc",
                table: "MaintenanceRequests",
                type: "datetime2",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Title",
                table: "Properties",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "Properties",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Address",
                table: "Properties",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "MaintenanceRequests",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "MaintenanceRequests",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "FullName",
                table: "AspNetUsers",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceRequests_TenancyId_DateSubmitted",
                table: "MaintenanceRequests",
                columns: new[] { "TenancyId", "DateSubmitted" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_MaintenanceRequests_Status",
                table: "MaintenanceRequests",
                sql: "[Status] IN (N'Submitted', N'InProgress', N'Resolved', N'Closed')");

            migrationBuilder.CreateIndex(
                name: "IX_Tenancies_PropertyId",
                table: "Tenancies",
                column: "PropertyId",
                unique: true,
                filter: "[Status] = N'Active'");

            migrationBuilder.CreateIndex(
                name: "IX_Tenancies_TenantId",
                table: "Tenancies",
                column: "TenantId");

            migrationBuilder.AddForeignKey(
                name: "FK_MaintenanceRequests_Tenancies_TenancyId",
                table: "MaintenanceRequests",
                column: "TenancyId",
                principalTable: "Tenancies",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Properties_AspNetUsers_LandlordId",
                table: "Properties",
                column: "LandlordId",
                principalTable: "AspNetUsers",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MaintenanceRequests_Tenancies_TenancyId",
                table: "MaintenanceRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_Properties_AspNetUsers_LandlordId",
                table: "Properties");

            migrationBuilder.DropIndex(
                name: "IX_MaintenanceRequests_TenancyId_DateSubmitted",
                table: "MaintenanceRequests");

            migrationBuilder.DropCheckConstraint(
                name: "CK_MaintenanceRequests_Status",
                table: "MaintenanceRequests");

            migrationBuilder.AddColumn<int>(
                name: "PropertyId",
                table: "MaintenanceRequests",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "MaintenanceRequests",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE request
                SET
                    request.[PropertyId] = tenancy.[PropertyId],
                    request.[TenantId] = tenancy.[TenantId],
                    request.[Status] = CASE
                        WHEN request.[Status] = N'Submitted' THEN N'Pending'
                        ELSE request.[Status]
                    END
                FROM [MaintenanceRequests] request
                INNER JOIN [Tenancies] tenancy ON tenancy.[Id] = request.[TenancyId];

                IF EXISTS (
                    SELECT 1
                    FROM [MaintenanceRequests]
                    WHERE [PropertyId] IS NULL OR [TenantId] IS NULL)
                    THROW 51000, 'Maintenance requests could not be restored to the legacy relationship.', 1;
                """);

            migrationBuilder.AlterColumn<int>(
                name: "PropertyId",
                table: "MaintenanceRequests",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "TenantId",
                table: "MaintenanceRequests",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldNullable: true);

            migrationBuilder.DropColumn(
                name: "LastUpdatedUtc",
                table: "MaintenanceRequests");

            migrationBuilder.DropColumn(
                name: "TenancyId",
                table: "MaintenanceRequests");

            migrationBuilder.DropTable(name: "Tenancies");

            migrationBuilder.AlterColumn<string>(
                name: "Title",
                table: "Properties",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "Properties",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(2000)",
                oldMaxLength: 2000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Address",
                table: "Properties",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(500)",
                oldMaxLength: 500);

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "MaintenanceRequests",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(20)",
                oldMaxLength: 20);

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "MaintenanceRequests",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(2000)",
                oldMaxLength: 2000);

            migrationBuilder.AlterColumn<string>(
                name: "FullName",
                table: "AspNetUsers",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200);

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceRequests_PropertyId",
                table: "MaintenanceRequests",
                column: "PropertyId");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceRequests_TenantId",
                table: "MaintenanceRequests",
                column: "TenantId");

            migrationBuilder.AddForeignKey(
                name: "FK_MaintenanceRequests_AspNetUsers_TenantId",
                table: "MaintenanceRequests",
                column: "TenantId",
                principalTable: "AspNetUsers",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_MaintenanceRequests_Properties_PropertyId",
                table: "MaintenanceRequests",
                column: "PropertyId",
                principalTable: "Properties",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Properties_AspNetUsers_LandlordId",
                table: "Properties",
                column: "LandlordId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
