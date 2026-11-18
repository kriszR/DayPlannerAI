using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DayPlannerAI.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace DayPlannerAI.Api.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(
            DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<UserProfile> UserProfiles => Set<UserProfile>();

        public DbSet<GoogleCalendarConnection> GoogleCalendarConnections
    => Set<GoogleCalendarConnection>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<UserProfile>(entity =>
            {
                entity.HasKey(x => x.Id);

                entity.Property(x => x.DisplayName)
                    .HasMaxLength(100)
                    .IsRequired();

                entity.Property(x => x.HomeLocation)
                    .HasMaxLength(200);

                entity.Property(x => x.TimeZone)
                    .HasMaxLength(100)
                    .IsRequired();

                entity.HasData(new UserProfile
                {
                    Id = 1,
                    DisplayName = "DayPlannerAI User",
                    HomeLocation = null,
                    TimeZone = "Europe/Budapest",
                    CreatedAt = new DateTime(
                        2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                });
            });

            modelBuilder.Entity<GoogleCalendarConnection>(entity =>
                {
                    entity.HasKey(x => x.Id);

                    entity.Property(x => x.ProtectedRefreshToken)
                        .IsRequired();

                    entity.Property(x => x.ConnectedAt)
                        .IsRequired();

                    entity.Property(x => x.UpdatedAt)
                        .IsRequired();
                });
        }
    }
}