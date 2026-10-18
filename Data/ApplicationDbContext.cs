using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using RentRight.Models;

namespace RentRight.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options) { }

        public DbSet<Property> Properties => Set<Property>();
        public DbSet<Tenancy> Tenancies => Set<Tenancy>();
        public DbSet<MaintenanceRequest> MaintenanceRequests => Set<MaintenanceRequest>();

        public DbSet<MaintenanceStatusEvent> MaintenanceStatusEvents => Set<MaintenanceStatusEvent>();

        private void ValidateHistory()
        {
            if (ChangeTracker.Entries<MaintenanceStatusEvent>().Any(entry =>
                entry.State is EntityState.Modified or EntityState.Deleted))
                throw new InvalidOperationException("Maintenance status history is append-only.");
        }

        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            ValidateHistory();
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            ValidateHistory();
            return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<ApplicationUser>()
                .Property(user => user.FullName)
                .HasMaxLength(200);

            // Preserve the key lengths from the existing initial migration.
            builder.Entity<IdentityUserLogin<string>>(entity =>
            {
                entity.Property(login => login.LoginProvider).HasMaxLength(450);
                entity.Property(login => login.ProviderKey).HasMaxLength(450);
            });

            builder.Entity<IdentityUserToken<string>>(entity =>
            {
                entity.Property(token => token.LoginProvider).HasMaxLength(450);
                entity.Property(token => token.Name).HasMaxLength(450);
            });

            builder.Entity<Property>(entity =>
            {
                entity.Property(property => property.RowVersion).IsRowVersion();
                entity.ToTable(table => table.HasCheckConstraint("CK_Properties_RentAmount", "[RentAmount] >= 0"));
                entity.Property(property => property.Title).HasMaxLength(200);
                entity.Property(property => property.Address).HasMaxLength(500);
                entity.Property(property => property.Latitude).HasPrecision(9, 6);
                entity.Property(property => property.Longitude).HasPrecision(9, 6);
                entity.Property(property => property.GeocodingPlaceId).HasMaxLength(512);
                entity.ToTable(table => table.HasCheckConstraint("CK_Properties_Location",
                    "([Latitude] IS NULL AND [Longitude] IS NULL AND [GeocodingPlaceId] IS NULL AND [GeocodedUtc] IS NULL) OR " +
                    "([Latitude] IS NOT NULL AND [Longitude] IS NOT NULL AND [GeocodingPlaceId] IS NOT NULL AND LEN([GeocodingPlaceId]) > 0 AND [GeocodedUtc] IS NOT NULL AND [Latitude] BETWEEN -90 AND 90 AND [Longitude] BETWEEN -180 AND 180)"));
                entity.Property(property => property.Description).HasMaxLength(2000);
                entity.Property(property => property.RentAmount).HasPrecision(18, 2);

                // Public MVC/API ordering; measured plans and reads: docs/performance/QUERY_PERFORMANCE.md.
                entity.HasIndex(property => new { property.Title, property.Address, property.Id })
                    .IncludeProperties(property => new { property.RentAmount, property.Description, property.LandlordId });

                entity.HasOne(property => property.Landlord)
                    .WithMany(user => user.PropertiesOwned)
                    .HasForeignKey(property => property.LandlordId)
                    .OnDelete(DeleteBehavior.NoAction);
            });

            builder.Entity<Tenancy>(entity =>
            {
                entity.Property(tenancy => tenancy.StartDate).HasColumnType("date");
                entity.Property(tenancy => tenancy.EndDate).HasColumnType("date");
                entity.Property(tenancy => tenancy.Status)
                    .HasConversion<string>()
                    .HasMaxLength(20);

                entity.Property(tenancy => tenancy.InvitationTokenHash).HasMaxLength(64).IsUnicode(false);
                entity.Property(tenancy => tenancy.InvitedByUserId).HasMaxLength(450);
                entity.Property(tenancy => tenancy.AcceptedByUserId).HasMaxLength(450);
                entity.Property(tenancy => tenancy.EndedByUserId).HasMaxLength(450);
                entity.Property(tenancy => tenancy.EndReason).HasMaxLength(20);
                entity.Property(tenancy => tenancy.RowVersion).IsRowVersion();
                entity.ToTable(table => table.UseSqlOutputClause(false));
                entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(t => t.InvitedByUserId).OnDelete(DeleteBehavior.NoAction);
                entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(t => t.AcceptedByUserId).OnDelete(DeleteBehavior.NoAction);
                entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(t => t.EndedByUserId).OnDelete(DeleteBehavior.NoAction);
                entity.HasIndex(tenancy => tenancy.InvitationTokenHash).IsUnique()
                    .HasFilter("[InvitationTokenHash] IS NOT NULL");

                entity.HasOne(tenancy => tenancy.Property)
                    .WithMany(property => property.Tenancies)
                    .HasForeignKey(tenancy => tenancy.PropertyId)
                    .OnDelete(DeleteBehavior.NoAction);

                entity.HasOne(tenancy => tenancy.Tenant)
                    .WithMany(user => user.Tenancies)
                    .HasForeignKey(tenancy => tenancy.TenantId)
                    .OnDelete(DeleteBehavior.NoAction);

                entity.HasIndex(tenancy => tenancy.TenantId);
                entity.HasIndex(tenancy => tenancy.PropertyId)
                    .IsUnique()
                    .HasFilter("[Status] IN (N'Active', N'Pending')");

                entity.ToTable(table =>
                {
                    table.HasCheckConstraint("CK_Tenancies_Consent",
                        "([AcceptedUtc] IS NULL AND [AcceptedByUserId] IS NULL) OR ([AcceptedUtc] IS NOT NULL AND [AcceptedByUserId] IS NOT NULL AND [TenantId] IS NOT NULL AND [AcceptedByUserId] = [TenantId] AND [InvitedUtc] IS NOT NULL AND [AcceptedUtc] >= [InvitedUtc])");
                    table.HasCheckConstraint("CK_Tenancies_InvitedActiveConsent",
                        "[Status] <> N'Active' OR [InvitedUtc] IS NULL OR [AcceptedUtc] IS NOT NULL");
                    table.HasCheckConstraint("CK_Tenancies_TokenState",
                        "[Status] = N'Pending' OR [InvitationTokenHash] IS NULL");
                    table.HasCheckConstraint("CK_Tenancies_PendingConsent",
                        "[Status] <> N'Pending' OR ([AcceptedUtc] IS NULL AND [AcceptedByUserId] IS NULL)");
                    table.HasCheckConstraint(
                        "CK_Tenancies_EndDate",
                        "[EndDate] IS NULL OR [EndDate] >= [StartDate]");
                    table.HasCheckConstraint("CK_Tenancies_ActiveTenant",
                        "[Status] <> N'Active' OR [TenantId] IS NOT NULL");
                    table.HasCheckConstraint("CK_Tenancies_Invitation",
                        "[Status] <> N'Pending' OR ([InvitationTokenHash] IS NOT NULL AND [InvitedUtc] IS NOT NULL AND [InvitationExpiresUtc] IS NOT NULL AND [InvitationExpiresUtc] > [InvitedUtc] AND [InvitedByUserId] IS NOT NULL)");
                    table.HasCheckConstraint(
                        "CK_Tenancies_Status",
                        "[Status] IN (N'Active', N'Ended', N'Pending')");
                });
            });

            builder.Entity<MaintenanceStatusEvent>(entity =>
            {
                entity.ToTable(table => table.UseSqlOutputClause(false));
                entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(e => e.ActorUserId).OnDelete(DeleteBehavior.NoAction);
                entity.ToTable(table =>
                {
                    table.HasCheckConstraint("CK_MaintenanceStatusEvents_Facts",
                        "([PreviousStatus] IS NULL OR ([ActorUserId] IS NOT NULL AND (([PreviousStatus] = N'Submitted' AND [Status] = N'InProgress') OR ([PreviousStatus] = N'InProgress' AND [Status] = N'Resolved') OR ([PreviousStatus] = N'Resolved' AND [Status] IN (N'InProgress', N'Closed'))))) AND ([IsLegacyBaseline] = 0 OR ([PreviousStatus] IS NULL AND [ActorUserId] IS NULL AND [Note] IS NULL))");
                });
                entity.Property(e => e.PreviousStatus).HasConversion<string>().HasMaxLength(20);
                entity.Property(e => e.ActorUserId).HasMaxLength(450);
                entity.Property(e => e.Note).HasMaxLength(1000);
                entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
                entity.HasOne<MaintenanceRequest>().WithMany(r => r.StatusHistory)
                    .HasForeignKey(e => e.MaintenanceRequestId).OnDelete(DeleteBehavior.NoAction);
                entity.HasIndex(e => new { e.MaintenanceRequestId, e.OccurredUtc, e.Id });
                entity.ToTable(table => table.HasCheckConstraint("CK_MaintenanceStatusEvents_Status",
                    "[Status] IN (N'Submitted', N'InProgress', N'Resolved', N'Closed')"));
            });

            builder.Entity<MaintenanceRequest>(entity =>
            {
                entity.Property(request => request.RowVersion).IsRowVersion();
                entity.ToTable(table =>
                {
                    table.UseSqlOutputClause(false);
                    table.HasCheckConstraint("CK_MaintenanceRequests_Revision", "[Revision] >= 0");
                    table.HasCheckConstraint("CK_MaintenanceRequests_Dates", "[LastUpdatedUtc] >= [DateSubmitted]");
                });
                entity.Property(request => request.Priority).HasConversion<string>().HasMaxLength(20)
                    .HasSentinel((MaintenancePriority)0)
                    .HasDefaultValue(MaintenancePriority.Normal);
                entity.Property(request => request.Revision).HasDefaultValue(0L);
                entity.ToTable(table => table.HasCheckConstraint("CK_MaintenanceRequests_Priority",
                    "[Priority] IN (N'Low', N'Normal', N'High', N'Urgent')"));
                entity.Property(request => request.Description).HasMaxLength(2000);
                entity.Property(request => request.Status)
                    .HasConversion<string>()
                    .HasMaxLength(20);

                entity.HasOne(request => request.Tenancy)
                    .WithMany(tenancy => tenancy.MaintenanceRequests)
                    .HasForeignKey(request => request.TenancyId)
                    .OnDelete(DeleteBehavior.NoAction);

                // Cover tenant dashboard status/count projections without per-request clustered lookups.
                entity.HasIndex(request => new { request.TenancyId, request.DateSubmitted })
                    .IncludeProperties(request => request.Status);
                // Filtered landlord/Admin queues; preserve tenancy/property ownership joins.
                entity.HasIndex(request => new { request.Status, request.DateSubmitted, request.Id })
                    .IsDescending(false, true, true)
                    .IncludeProperties(request => new { request.TenancyId, request.Priority, request.Revision });

                entity.ToTable(table => table.HasCheckConstraint(
                    "CK_MaintenanceRequests_Status",
                    "[Status] IN (N'Submitted', N'InProgress', N'Resolved', N'Closed')"));
            });
        }
    }
}
