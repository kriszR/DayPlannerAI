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

        public DbSet<GtfsAgency> GtfsAgencies => Set<GtfsAgency>();
        public DbSet<GtfsStop> GtfsStops => Set<GtfsStop>();
        public DbSet<GtfsRoute> GtfsRoutes => Set<GtfsRoute>();
        public DbSet<GtfsTrip> GtfsTrips => Set<GtfsTrip>();
        public DbSet<GtfsStopTime> GtfsStopTimes => Set<GtfsStopTime>();
        public DbSet<GtfsCalendar> GtfsCalendars => Set<GtfsCalendar>();
        public DbSet<GtfsCalendarDate> GtfsCalendarDates => Set<GtfsCalendarDate>();
        public DbSet<GtfsFeedInfo> GtfsFeedInfos => Set<GtfsFeedInfo>();

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

            modelBuilder.Entity<GtfsAgency>()
.HasKey(x => x.AgencyId);

            modelBuilder.Entity<GtfsStop>()
                .HasKey(x => x.StopId);

            modelBuilder.Entity<GtfsStop>()
                .HasIndex(x => x.StopName);

            modelBuilder.Entity<GtfsRoute>()
                .HasKey(x => x.RouteId);

            modelBuilder.Entity<GtfsRoute>()
                .HasIndex(x => x.RouteShortName);

            modelBuilder.Entity<GtfsTrip>()
                .HasKey(x => x.TripId);

            modelBuilder.Entity<GtfsTrip>()
                .HasIndex(x => x.ServiceId);

            modelBuilder.Entity<GtfsStopTime>()
                .HasKey(x => new { x.TripId, x.StopSequence });

            modelBuilder.Entity<GtfsStopTime>()
                .HasIndex(x => new { x.StopId, x.TripId });

            modelBuilder.Entity<GtfsCalendar>()
                .HasKey(x => x.ServiceId);

            modelBuilder.Entity<GtfsCalendarDate>()
                .HasKey(x => new { x.ServiceId, x.Date });

            modelBuilder.Entity<GtfsCalendarDate>()
                .HasIndex(x => x.Date);

            modelBuilder.Entity<GtfsFeedInfo>()
                .HasKey(x => x.Id);
        }
    }
}