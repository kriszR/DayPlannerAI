using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace DayPlannerAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddGtfsTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GtfsAgencies",
                columns: table => new
                {
                    AgencyId = table.Column<string>(type: "text", nullable: false),
                    AgencyName = table.Column<string>(type: "text", nullable: false),
                    AgencyUrl = table.Column<string>(type: "text", nullable: false),
                    AgencyTimezone = table.Column<string>(type: "text", nullable: false),
                    AgencyLang = table.Column<string>(type: "text", nullable: true),
                    AgencyPhone = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GtfsAgencies", x => x.AgencyId);
                });

            migrationBuilder.CreateTable(
                name: "GtfsCalendarDates",
                columns: table => new
                {
                    ServiceId = table.Column<string>(type: "text", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    ExceptionType = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GtfsCalendarDates", x => new { x.ServiceId, x.Date });
                });

            migrationBuilder.CreateTable(
                name: "GtfsCalendars",
                columns: table => new
                {
                    ServiceId = table.Column<string>(type: "text", nullable: false),
                    Monday = table.Column<bool>(type: "boolean", nullable: false),
                    Tuesday = table.Column<bool>(type: "boolean", nullable: false),
                    Wednesday = table.Column<bool>(type: "boolean", nullable: false),
                    Thursday = table.Column<bool>(type: "boolean", nullable: false),
                    Friday = table.Column<bool>(type: "boolean", nullable: false),
                    Saturday = table.Column<bool>(type: "boolean", nullable: false),
                    Sunday = table.Column<bool>(type: "boolean", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ServiceDesc = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GtfsCalendars", x => x.ServiceId);
                });

            migrationBuilder.CreateTable(
                name: "GtfsFeedInfos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FeedId = table.Column<string>(type: "text", nullable: true),
                    PublisherName = table.Column<string>(type: "text", nullable: true),
                    PublisherUrl = table.Column<string>(type: "text", nullable: true),
                    FeedLanguage = table.Column<string>(type: "text", nullable: true),
                    FeedStartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    FeedEndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    FeedVersion = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GtfsFeedInfos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GtfsRoutes",
                columns: table => new
                {
                    RouteId = table.Column<string>(type: "text", nullable: false),
                    AgencyId = table.Column<string>(type: "text", nullable: true),
                    RouteShortName = table.Column<string>(type: "text", nullable: true),
                    RouteLongName = table.Column<string>(type: "text", nullable: true),
                    RouteType = table.Column<int>(type: "integer", nullable: false),
                    RouteUrl = table.Column<string>(type: "text", nullable: true),
                    RouteColor = table.Column<string>(type: "text", nullable: true),
                    RouteTextColor = table.Column<string>(type: "text", nullable: true),
                    RouteNetwork = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GtfsRoutes", x => x.RouteId);
                });

            migrationBuilder.CreateTable(
                name: "GtfsStops",
                columns: table => new
                {
                    StopId = table.Column<string>(type: "text", nullable: false),
                    StopName = table.Column<string>(type: "text", nullable: false),
                    StopLat = table.Column<double>(type: "double precision", nullable: true),
                    StopLon = table.Column<double>(type: "double precision", nullable: true),
                    LocationType = table.Column<int>(type: "integer", nullable: true),
                    ParentStation = table.Column<string>(type: "text", nullable: true),
                    PlatformCode = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GtfsStops", x => x.StopId);
                });

            migrationBuilder.CreateTable(
                name: "GtfsStopTimes",
                columns: table => new
                {
                    TripId = table.Column<string>(type: "text", nullable: false),
                    StopSequence = table.Column<int>(type: "integer", nullable: false),
                    StopId = table.Column<string>(type: "text", nullable: false),
                    ArrivalSeconds = table.Column<int>(type: "integer", nullable: true),
                    DepartureSeconds = table.Column<int>(type: "integer", nullable: true),
                    PickupType = table.Column<int>(type: "integer", nullable: true),
                    DropOffType = table.Column<int>(type: "integer", nullable: true),
                    ShapeDistTraveled = table.Column<double>(type: "double precision", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GtfsStopTimes", x => new { x.TripId, x.StopSequence });
                });

            migrationBuilder.CreateTable(
                name: "GtfsTrips",
                columns: table => new
                {
                    TripId = table.Column<string>(type: "text", nullable: false),
                    RouteId = table.Column<string>(type: "text", nullable: false),
                    ServiceId = table.Column<string>(type: "text", nullable: false),
                    TripShortName = table.Column<string>(type: "text", nullable: true),
                    TripHeadsign = table.Column<string>(type: "text", nullable: true),
                    DirectionId = table.Column<int>(type: "integer", nullable: true),
                    ShapeId = table.Column<string>(type: "text", nullable: true),
                    WheelchairAccessible = table.Column<int>(type: "integer", nullable: true),
                    TripRealtimeId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GtfsTrips", x => x.TripId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GtfsCalendarDates_Date",
                table: "GtfsCalendarDates",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_GtfsRoutes_RouteShortName",
                table: "GtfsRoutes",
                column: "RouteShortName");

            migrationBuilder.CreateIndex(
                name: "IX_GtfsStops_StopName",
                table: "GtfsStops",
                column: "StopName");

            migrationBuilder.CreateIndex(
                name: "IX_GtfsStopTimes_StopId_TripId",
                table: "GtfsStopTimes",
                columns: new[] { "StopId", "TripId" });

            migrationBuilder.CreateIndex(
                name: "IX_GtfsTrips_ServiceId",
                table: "GtfsTrips",
                column: "ServiceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GtfsAgencies");

            migrationBuilder.DropTable(
                name: "GtfsCalendarDates");

            migrationBuilder.DropTable(
                name: "GtfsCalendars");

            migrationBuilder.DropTable(
                name: "GtfsFeedInfos");

            migrationBuilder.DropTable(
                name: "GtfsRoutes");

            migrationBuilder.DropTable(
                name: "GtfsStops");

            migrationBuilder.DropTable(
                name: "GtfsStopTimes");

            migrationBuilder.DropTable(
                name: "GtfsTrips");
        }
    }
}
