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
                entity.Property(property => property.Title).HasMaxLength(200);
                entity.Property(property => property.Address).HasMaxLength(500);
                entity.Property(property => property.Description).HasMaxLength(2000);
                entity.Property(property => property.RentAmount).HasPrecision(18, 2);

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
                    .HasFilter("[Status] = N'Active'");

                entity.ToTable(table =>
                {
                    table.HasCheckConstraint(
                        "CK_Tenancies_EndDate",
                        "[EndDate] IS NULL OR [EndDate] >= [StartDate]");
                    table.HasCheckConstraint(
                        "CK_Tenancies_Status",
                        "[Status] IN (N'Active', N'Ended')");
                });
            });

            builder.Entity<MaintenanceRequest>(entity =>
            {
                entity.Property(request => request.Description).HasMaxLength(2000);
                entity.Property(request => request.Status)
                    .HasConversion<string>()
                    .HasMaxLength(20);

                entity.HasOne(request => request.Tenancy)
                    .WithMany(tenancy => tenancy.MaintenanceRequests)
                    .HasForeignKey(request => request.TenancyId)
                    .OnDelete(DeleteBehavior.NoAction);

                entity.HasIndex(request => new { request.TenancyId, request.DateSubmitted });

                entity.ToTable(table => table.HasCheckConstraint(
                    "CK_MaintenanceRequests_Status",
                    "[Status] IN (N'Submitted', N'InProgress', N'Resolved', N'Closed')"));
            });
        }
    }
}
