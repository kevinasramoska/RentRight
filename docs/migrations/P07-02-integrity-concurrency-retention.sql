BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017134027_IntegrityConcurrencyRetention'
)
BEGIN
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
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017134027_IntegrityConcurrencyRetention'
)
BEGIN
    ALTER TABLE [Properties] ADD [RowVersion] rowversion NOT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017134027_IntegrityConcurrencyRetention'
)
BEGIN
    ALTER TABLE [MaintenanceRequests] ADD [RowVersion] rowversion NOT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017134027_IntegrityConcurrencyRetention'
)
BEGIN
    CREATE INDEX [IX_Tenancies_AcceptedByUserId] ON [Tenancies] ([AcceptedByUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017134027_IntegrityConcurrencyRetention'
)
BEGIN
    CREATE INDEX [IX_Tenancies_EndedByUserId] ON [Tenancies] ([EndedByUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017134027_IntegrityConcurrencyRetention'
)
BEGIN
    CREATE INDEX [IX_Tenancies_InvitedByUserId] ON [Tenancies] ([InvitedByUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017134027_IntegrityConcurrencyRetention'
)
BEGIN
    EXEC(N'ALTER TABLE [Tenancies] ADD CONSTRAINT [CK_Tenancies_Consent] CHECK (([AcceptedUtc] IS NULL AND [AcceptedByUserId] IS NULL) OR ([AcceptedUtc] IS NOT NULL AND [AcceptedByUserId] IS NOT NULL AND [TenantId] IS NOT NULL AND [AcceptedByUserId] = [TenantId] AND [InvitedUtc] IS NOT NULL AND [AcceptedUtc] >= [InvitedUtc]))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017134027_IntegrityConcurrencyRetention'
)
BEGIN
    EXEC(N'ALTER TABLE [Tenancies] ADD CONSTRAINT [CK_Tenancies_InvitedActiveConsent] CHECK ([Status] <> N''Active'' OR [InvitedUtc] IS NULL OR [AcceptedUtc] IS NOT NULL)');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017134027_IntegrityConcurrencyRetention'
)
BEGIN
    EXEC(N'ALTER TABLE [Tenancies] ADD CONSTRAINT [CK_Tenancies_PendingConsent] CHECK ([Status] <> N''Pending'' OR ([AcceptedUtc] IS NULL AND [AcceptedByUserId] IS NULL))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017134027_IntegrityConcurrencyRetention'
)
BEGIN
    EXEC(N'ALTER TABLE [Tenancies] ADD CONSTRAINT [CK_Tenancies_TokenState] CHECK ([Status] = N''Pending'' OR [InvitationTokenHash] IS NULL)');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017134027_IntegrityConcurrencyRetention'
)
BEGIN
    EXEC(N'ALTER TABLE [Properties] ADD CONSTRAINT [CK_Properties_RentAmount] CHECK ([RentAmount] >= 0)');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017134027_IntegrityConcurrencyRetention'
)
BEGIN
    CREATE INDEX [IX_MaintenanceStatusEvents_ActorUserId] ON [MaintenanceStatusEvents] ([ActorUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017134027_IntegrityConcurrencyRetention'
)
BEGIN
    EXEC(N'ALTER TABLE [MaintenanceStatusEvents] ADD CONSTRAINT [CK_MaintenanceStatusEvents_Facts] CHECK (([PreviousStatus] IS NULL OR ([ActorUserId] IS NOT NULL AND (([PreviousStatus] = N''Submitted'' AND [Status] = N''InProgress'') OR ([PreviousStatus] = N''InProgress'' AND [Status] = N''Resolved'') OR ([PreviousStatus] = N''Resolved'' AND [Status] IN (N''InProgress'', N''Closed''))))) AND ([IsLegacyBaseline] = 0 OR ([PreviousStatus] IS NULL AND [ActorUserId] IS NULL AND [Note] IS NULL)))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017134027_IntegrityConcurrencyRetention'
)
BEGIN
    EXEC(N'ALTER TABLE [MaintenanceRequests] ADD CONSTRAINT [CK_MaintenanceRequests_Dates] CHECK ([LastUpdatedUtc] >= [DateSubmitted])');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017134027_IntegrityConcurrencyRetention'
)
BEGIN
    EXEC(N'ALTER TABLE [MaintenanceRequests] ADD CONSTRAINT [CK_MaintenanceRequests_Revision] CHECK ([Revision] >= 0)');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017134027_IntegrityConcurrencyRetention'
)
BEGIN
    ALTER TABLE [MaintenanceStatusEvents] ADD CONSTRAINT [FK_MaintenanceStatusEvents_AspNetUsers_ActorUserId] FOREIGN KEY ([ActorUserId]) REFERENCES [AspNetUsers] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017134027_IntegrityConcurrencyRetention'
)
BEGIN
    ALTER TABLE [Tenancies] ADD CONSTRAINT [FK_Tenancies_AspNetUsers_AcceptedByUserId] FOREIGN KEY ([AcceptedByUserId]) REFERENCES [AspNetUsers] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017134027_IntegrityConcurrencyRetention'
)
BEGIN
    ALTER TABLE [Tenancies] ADD CONSTRAINT [FK_Tenancies_AspNetUsers_EndedByUserId] FOREIGN KEY ([EndedByUserId]) REFERENCES [AspNetUsers] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017134027_IntegrityConcurrencyRetention'
)
BEGIN
    ALTER TABLE [Tenancies] ADD CONSTRAINT [FK_Tenancies_AspNetUsers_InvitedByUserId] FOREIGN KEY ([InvitedByUserId]) REFERENCES [AspNetUsers] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017134027_IntegrityConcurrencyRetention'
)
BEGIN
    EXEC(N'CREATE TRIGGER [TR_Tenancies_Retain] ON [Tenancies] AFTER DELETE AS
    BEGIN
        SET NOCOUNT ON;
        IF EXISTS (SELECT 1 FROM deleted) THROW 51004, ''Tenancy history must be retained.'', 1;
    END;');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017134027_IntegrityConcurrencyRetention'
)
BEGIN
    EXEC(N'CREATE TRIGGER [TR_MaintenanceRequests_Retain] ON [MaintenanceRequests] AFTER DELETE AS
    BEGIN
        SET NOCOUNT ON;
        IF EXISTS (SELECT 1 FROM deleted) THROW 51004, ''Maintenance requests must be retained.'', 1;
    END;');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017134027_IntegrityConcurrencyRetention'
)
BEGIN
    EXEC(N'CREATE TRIGGER [TR_MaintenanceStatusEvents_Immutable] ON [MaintenanceStatusEvents] AFTER UPDATE, DELETE AS
    BEGIN
        SET NOCOUNT ON;
        IF EXISTS (SELECT 1 FROM deleted) THROW 51004, ''Maintenance status history is append-only.'', 1;
    END;');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017134027_IntegrityConcurrencyRetention'
)
BEGIN
    EXEC(N'CREATE TRIGGER [TR_MaintenanceRequests_Revision] ON [MaintenanceRequests] AFTER UPDATE AS
    BEGIN
        SET NOCOUNT ON;
        IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON i.Id=d.Id
        WHERE CONVERT(decimal(20,0),i.Revision) <> CONVERT(decimal(20,0),d.Revision)+1)
        THROW 51005, ''Every maintenance update must advance revision exactly once.'', 1;
    END;');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017134027_IntegrityConcurrencyRetention'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261017134027_IntegrityConcurrencyRetention', N'10.0.10');
END;

COMMIT;
GO
