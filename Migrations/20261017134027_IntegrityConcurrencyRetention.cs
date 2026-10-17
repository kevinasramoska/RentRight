using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentRight.Migrations
{
    /// <inheritdoc />
    public partial class IntegrityConcurrencyRetention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Do not fabricate consent/actors or repair timestamps by silently rewriting records.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [Tenancies] WHERE NOT (([AcceptedUtc] IS NULL AND [AcceptedByUserId] IS NULL) OR ([AcceptedUtc] IS NOT NULL AND [AcceptedByUserId] IS NOT NULL AND [TenantId] IS NOT NULL AND [AcceptedByUserId] = [TenantId] AND [InvitedUtc] IS NOT NULL AND [AcceptedUtc] >= [InvitedUtc])))
                    THROW 51003, 'P07-02 preflight: CK_Tenancies_Consent; review incompatible rows before deployment.', 1;
                IF EXISTS (SELECT 1 FROM [Tenancies] WHERE NOT ([Status] <> N'Active' OR [InvitedUtc] IS NULL OR [AcceptedUtc] IS NOT NULL))
                    THROW 51003, 'P07-02 preflight: CK_Tenancies_InvitedActiveConsent; review incompatible rows before deployment.', 1;
                IF EXISTS (SELECT 1 FROM [Tenancies] WHERE NOT ([Status] <> N'Pending' OR ([AcceptedUtc] IS NULL AND [AcceptedByUserId] IS NULL)))
                    THROW 51003, 'P07-02 preflight: CK_Tenancies_PendingConsent; review incompatible rows before deployment.', 1;
                IF EXISTS (SELECT 1 FROM [Tenancies] WHERE NOT ([Status] = N'Pending' OR [InvitationTokenHash] IS NULL))
                    THROW 51003, 'P07-02 preflight: CK_Tenancies_TokenState; review incompatible rows before deployment.', 1;
                IF EXISTS (SELECT 1 FROM [Properties] WHERE NOT ([RentAmount] >= 0))
                    THROW 51003, 'P07-02 preflight: CK_Properties_RentAmount; review incompatible rows before deployment.', 1;
                IF EXISTS (SELECT 1 FROM [MaintenanceStatusEvents] WHERE NOT (([PreviousStatus] IS NULL OR ([ActorUserId] IS NOT NULL AND (([PreviousStatus] = N'Submitted' AND [Status] = N'InProgress') OR ([PreviousStatus] = N'InProgress' AND [Status] = N'Resolved') OR ([PreviousStatus] = N'Resolved' AND [Status] IN (N'InProgress', N'Closed'))))) AND ([IsLegacyBaseline] = 0 OR ([PreviousStatus] IS NULL AND [ActorUserId] IS NULL AND [Note] IS NULL))))
                    THROW 51003, 'P07-02 preflight: CK_MaintenanceStatusEvents_Facts; review incompatible rows before deployment.', 1;
                IF EXISTS (SELECT 1 FROM [MaintenanceRequests] WHERE NOT ([LastUpdatedUtc] >= [DateSubmitted]))
                    THROW 51003, 'P07-02 preflight: CK_MaintenanceRequests_Dates; review incompatible rows before deployment.', 1;
                IF EXISTS (SELECT 1 FROM [MaintenanceRequests] WHERE NOT ([Revision] >= 0))
                    THROW 51003, 'P07-02 preflight: CK_MaintenanceRequests_Revision; review incompatible rows before deployment.', 1;
                IF EXISTS (SELECT 1 FROM [Tenancies] r WHERE r.[InvitedByUserId] IS NOT NULL AND NOT EXISTS (SELECT 1 FROM [AspNetUsers] u WHERE u.[Id] = r.[InvitedByUserId]))
                    THROW 51003, 'P07-02 preflight: orphan Tenancies.InvitedByUserId; preserve truthful provenance before deployment.', 1;
                IF EXISTS (SELECT 1 FROM [Tenancies] r WHERE r.[AcceptedByUserId] IS NOT NULL AND NOT EXISTS (SELECT 1 FROM [AspNetUsers] u WHERE u.[Id] = r.[AcceptedByUserId]))
                    THROW 51003, 'P07-02 preflight: orphan Tenancies.AcceptedByUserId; preserve truthful provenance before deployment.', 1;
                IF EXISTS (SELECT 1 FROM [Tenancies] r WHERE r.[EndedByUserId] IS NOT NULL AND NOT EXISTS (SELECT 1 FROM [AspNetUsers] u WHERE u.[Id] = r.[EndedByUserId]))
                    THROW 51003, 'P07-02 preflight: orphan Tenancies.EndedByUserId; preserve truthful provenance before deployment.', 1;
                IF EXISTS (SELECT 1 FROM [MaintenanceStatusEvents] r WHERE r.[ActorUserId] IS NOT NULL AND NOT EXISTS (SELECT 1 FROM [AspNetUsers] u WHERE u.[Id] = r.[ActorUserId]))
                    THROW 51003, 'P07-02 preflight: orphan MaintenanceStatusEvents.ActorUserId; preserve truthful provenance before deployment.', 1;
                """);
            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Properties",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "MaintenanceRequests",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.CreateIndex(
                name: "IX_Tenancies_AcceptedByUserId",
                table: "Tenancies",
                column: "AcceptedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Tenancies_EndedByUserId",
                table: "Tenancies",
                column: "EndedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Tenancies_InvitedByUserId",
                table: "Tenancies",
                column: "InvitedByUserId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Tenancies_Consent",
                table: "Tenancies",
                sql: "([AcceptedUtc] IS NULL AND [AcceptedByUserId] IS NULL) OR ([AcceptedUtc] IS NOT NULL AND [AcceptedByUserId] IS NOT NULL AND [TenantId] IS NOT NULL AND [AcceptedByUserId] = [TenantId] AND [InvitedUtc] IS NOT NULL AND [AcceptedUtc] >= [InvitedUtc])");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Tenancies_InvitedActiveConsent",
                table: "Tenancies",
                sql: "[Status] <> N'Active' OR [InvitedUtc] IS NULL OR [AcceptedUtc] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Tenancies_PendingConsent",
                table: "Tenancies",
                sql: "[Status] <> N'Pending' OR ([AcceptedUtc] IS NULL AND [AcceptedByUserId] IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Tenancies_TokenState",
                table: "Tenancies",
                sql: "[Status] = N'Pending' OR [InvitationTokenHash] IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Properties_RentAmount",
                table: "Properties",
                sql: "[RentAmount] >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceStatusEvents_ActorUserId",
                table: "MaintenanceStatusEvents",
                column: "ActorUserId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_MaintenanceStatusEvents_Facts",
                table: "MaintenanceStatusEvents",
                sql: "([PreviousStatus] IS NULL OR ([ActorUserId] IS NOT NULL AND (([PreviousStatus] = N'Submitted' AND [Status] = N'InProgress') OR ([PreviousStatus] = N'InProgress' AND [Status] = N'Resolved') OR ([PreviousStatus] = N'Resolved' AND [Status] IN (N'InProgress', N'Closed'))))) AND ([IsLegacyBaseline] = 0 OR ([PreviousStatus] IS NULL AND [ActorUserId] IS NULL AND [Note] IS NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_MaintenanceRequests_Dates",
                table: "MaintenanceRequests",
                sql: "[LastUpdatedUtc] >= [DateSubmitted]");

            migrationBuilder.AddCheckConstraint(
                name: "CK_MaintenanceRequests_Revision",
                table: "MaintenanceRequests",
                sql: "[Revision] >= 0");

            migrationBuilder.AddForeignKey(
                name: "FK_MaintenanceStatusEvents_AspNetUsers_ActorUserId",
                table: "MaintenanceStatusEvents",
                column: "ActorUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Tenancies_AspNetUsers_AcceptedByUserId",
                table: "Tenancies",
                column: "AcceptedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Tenancies_AspNetUsers_EndedByUserId",
                table: "Tenancies",
                column: "EndedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Tenancies_AspNetUsers_InvitedByUserId",
                table: "Tenancies",
                column: "InvitedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id");

            // Dynamic DDL also works inside EF idempotent migration IF blocks.
            migrationBuilder.Sql("""
                EXEC(N'CREATE TRIGGER [TR_Tenancies_Retain] ON [Tenancies] AFTER DELETE AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (SELECT 1 FROM deleted) THROW 51004, ''Tenancy history must be retained.'', 1;
                END;');
                """);

            migrationBuilder.Sql("""
                EXEC(N'CREATE TRIGGER [TR_MaintenanceRequests_Retain] ON [MaintenanceRequests] AFTER DELETE AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (SELECT 1 FROM deleted) THROW 51004, ''Maintenance requests must be retained.'', 1;
                END;');
                """);

            migrationBuilder.Sql("""
                EXEC(N'CREATE TRIGGER [TR_MaintenanceStatusEvents_Immutable] ON [MaintenanceStatusEvents] AFTER UPDATE, DELETE AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (SELECT 1 FROM deleted) THROW 51004, ''Maintenance status history is append-only.'', 1;
                END;');
                """);

            migrationBuilder.Sql("""
                EXEC(N'CREATE TRIGGER [TR_MaintenanceRequests_Revision] ON [MaintenanceRequests] AFTER UPDATE AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON i.Id=d.Id
                    WHERE CONVERT(decimal(20,0),i.Revision) <> CONVERT(decimal(20,0),d.Revision)+1)
                    THROW 51005, ''Every maintenance update must advance revision exactly once.'', 1;
                END;');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Business/history columns are preserved. Rowversion values are invalid after rollback;
            // stop all writers and force clients to reload when redeploying either application.
            migrationBuilder.Sql("DROP TRIGGER [TR_Tenancies_Retain];");
            migrationBuilder.Sql("DROP TRIGGER [TR_MaintenanceRequests_Retain];");
            migrationBuilder.Sql("DROP TRIGGER [TR_MaintenanceStatusEvents_Immutable];");
            migrationBuilder.Sql("DROP TRIGGER [TR_MaintenanceRequests_Revision];");

            migrationBuilder.DropForeignKey(
                name: "FK_MaintenanceStatusEvents_AspNetUsers_ActorUserId",
                table: "MaintenanceStatusEvents");

            migrationBuilder.DropForeignKey(
                name: "FK_Tenancies_AspNetUsers_AcceptedByUserId",
                table: "Tenancies");

            migrationBuilder.DropForeignKey(
                name: "FK_Tenancies_AspNetUsers_EndedByUserId",
                table: "Tenancies");

            migrationBuilder.DropForeignKey(
                name: "FK_Tenancies_AspNetUsers_InvitedByUserId",
                table: "Tenancies");

            migrationBuilder.DropIndex(
                name: "IX_Tenancies_AcceptedByUserId",
                table: "Tenancies");

            migrationBuilder.DropIndex(
                name: "IX_Tenancies_EndedByUserId",
                table: "Tenancies");

            migrationBuilder.DropIndex(
                name: "IX_Tenancies_InvitedByUserId",
                table: "Tenancies");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Tenancies_Consent",
                table: "Tenancies");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Tenancies_InvitedActiveConsent",
                table: "Tenancies");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Tenancies_PendingConsent",
                table: "Tenancies");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Tenancies_TokenState",
                table: "Tenancies");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Properties_RentAmount",
                table: "Properties");

            migrationBuilder.DropIndex(
                name: "IX_MaintenanceStatusEvents_ActorUserId",
                table: "MaintenanceStatusEvents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_MaintenanceStatusEvents_Facts",
                table: "MaintenanceStatusEvents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_MaintenanceRequests_Dates",
                table: "MaintenanceRequests");

            migrationBuilder.DropCheckConstraint(
                name: "CK_MaintenanceRequests_Revision",
                table: "MaintenanceRequests");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "MaintenanceRequests");
        }
    }
}
