using System.Globalization;
using System.IO.Compression;
using CsvHelper;
using CsvHelper.Configuration;
using DayPlannerAI.Api.DTOs;
using DayPlannerAI.Api.Models;
using Microsoft.EntityFrameworkCore;
using DayPlannerAI.Api.Data;

namespace DayPlannerAI.Api.Services;

public sealed class GtfsImportService
{
    private const int BatchSize = 5000;

    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<GtfsImportService> _logger;

    public GtfsImportService(
        ApplicationDbContext dbContext,
        ILogger<GtfsImportService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<GtfsImportResponse> ImportAsync(
        string zipPath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(zipPath))
        {
            throw new FileNotFoundException(
                "The GTFS ZIP file was not found.",
                zipPath);
        }

        using var archive = ZipFile.OpenRead(zipPath);

        ValidateArchive(archive);

        await using var transaction =
            await _dbContext.Database.BeginTransactionAsync(
                cancellationToken);

        try
        {
            _logger.LogInformation("Starting GTFS import.");

            await _dbContext.Database.ExecuteSqlRawAsync(
                """
                TRUNCATE TABLE
                    "GtfsStopTimes",
                    "GtfsCalendarDates",
                    "GtfsCalendars",
                    "GtfsTrips",
                    "GtfsRoutes",
                    "GtfsStops",
                    "GtfsAgencies",
                    "GtfsFeedInfos";
                """,
                cancellationToken);

            _dbContext.ChangeTracker.Clear();

            var result = new GtfsImportResponse
            {
                Agencies = await ImportCsvAsync<GtfsAgency>(
                    archive,
                    "agency.txt",
                    MapAgency,
                    required: true,
                    cancellationToken),

                Stops = await ImportCsvAsync<GtfsStop>(
                    archive,
                    "stops.txt",
                    MapStop,
                    required: true,
                    cancellationToken),

                Routes = await ImportCsvAsync<GtfsRoute>(
                    archive,
                    "routes.txt",
                    MapRoute,
                    required: true,
                    cancellationToken),

                Trips = await ImportCsvAsync<GtfsTrip>(
                    archive,
                    "trips.txt",
                    MapTrip,
                    required: true,
                    cancellationToken),

                StopTimes = await ImportCsvAsync<GtfsStopTime>(
                    archive,
                    "stop_times.txt",
                    MapStopTime,
                    required: true,
                    cancellationToken),

                Calendars = await ImportCsvAsync<GtfsCalendar>(
                    archive,
                    "calendar.txt",
                    MapCalendar,
                    required: false,
                    cancellationToken),

                CalendarDates = await ImportCsvAsync<GtfsCalendarDate>(
                    archive,
                    "calendar_dates.txt",
                    MapCalendarDate,
                    required: false,
                    cancellationToken),

                FeedInfoRecords = await ImportCsvAsync<GtfsFeedInfo>(
                    archive,
                    "feed_info.txt",
                    MapFeedInfo,
                    required: false,
                    cancellationToken)
            };

            var feedInfo = await _dbContext.GtfsFeedInfos
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.Id == 1,
                    cancellationToken);

            result.FeedVersion = feedInfo?.FeedVersion;
            result.FeedStartDate = feedInfo?.FeedStartDate;
            result.FeedEndDate = feedInfo?.FeedEndDate;

            await transaction.CommitAsync(cancellationToken);

            _logger.LogInformation(
                "GTFS import completed. Stops: {Stops}, Trips: {Trips}, StopTimes: {StopTimes}",
                result.Stops,
                result.Trips,
                result.StopTimes);

            return result;
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(cancellationToken);

            _logger.LogError(
                exception,
                "GTFS import failed. Database transaction rolled back.");

            throw;
        }
    }

    private async Task<int> ImportCsvAsync<TEntity>(
        ZipArchive archive,
        string fileName,
        Func<CsvReader, TEntity> map,
        bool required,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        var entry = archive.Entries.FirstOrDefault(
            x => string.Equals(
                Path.GetFileName(x.FullName),
                fileName,
                StringComparison.OrdinalIgnoreCase));

        if (entry is null)
        {
            if (required)
            {
                throw new InvalidDataException(
                    $"Required GTFS file is missing: {fileName}");
            }

            _logger.LogWarning(
                "Optional GTFS file is missing: {FileName}",
                fileName);

            return 0;
        }

        _logger.LogInformation(
            "Importing GTFS file: {FileName}",
            fileName);

        using var entryStream = entry.Open();

        using var textReader = new StreamReader(
            entryStream,
            System.Text.Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true);

        var configuration = new CsvConfiguration(
            CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            Delimiter = ",",
            TrimOptions = TrimOptions.Trim,
            BadDataFound = null,
            MissingFieldFound = null,
            HeaderValidated = null
        };

        using var csv = new CsvReader(
            textReader,
            configuration);

        if (!csv.Read())
        {
            return 0;
        }

        csv.ReadHeader();

        var batch = new List<TEntity>(BatchSize);
        var importedCount = 0;

        var dbSet = _dbContext.Set<TEntity>();

        while (csv.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var entity = map(csv);

            batch.Add(entity);

            if (batch.Count >= BatchSize)
            {
                dbSet.AddRange(batch);

                await _dbContext.SaveChangesAsync(
                    cancellationToken);

                importedCount += batch.Count;

                _dbContext.ChangeTracker.Clear();
                batch.Clear();
            }
        }

        if (batch.Count > 0)
        {
            dbSet.AddRange(batch);

            await _dbContext.SaveChangesAsync(
                cancellationToken);

            importedCount += batch.Count;

            _dbContext.ChangeTracker.Clear();
        }

        _logger.LogInformation(
            "Imported {Count} records from {FileName}",
            importedCount,
            fileName);

        return importedCount;
    }

    private static void ValidateArchive(ZipArchive archive)
    {
        string[] requiredFiles =
        [
            "agency.txt",
            "stops.txt",
            "routes.txt",
            "trips.txt",
            "stop_times.txt"
        ];

        foreach (var fileName in requiredFiles)
        {
            var exists = archive.Entries.Any(
                x => string.Equals(
                    Path.GetFileName(x.FullName),
                    fileName,
                    StringComparison.OrdinalIgnoreCase));

            if (!exists)
            {
                throw new InvalidDataException(
                    $"Invalid GTFS archive. Missing file: {fileName}");
            }
        }
    }

    private static GtfsAgency MapAgency(CsvReader csv)
    {
        return new GtfsAgency
        {
            AgencyId = GetString(csv, "agency_id") ?? "default",
            AgencyName = GetRequiredString(csv, "agency_name"),
            AgencyUrl = GetString(csv, "agency_url") ?? string.Empty,
            AgencyTimezone = GetString(csv, "agency_timezone")
                ?? "Europe/Budapest",
            AgencyLang = GetString(csv, "agency_lang"),
            AgencyPhone = GetString(csv, "agency_phone")
        };
    }

    private static GtfsStop MapStop(CsvReader csv)
    {
        return new GtfsStop
        {
            StopId = GetRequiredString(csv, "stop_id"),
            StopName = GetRequiredString(csv, "stop_name"),
            StopLat = GetNullableDouble(csv, "stop_lat"),
            StopLon = GetNullableDouble(csv, "stop_lon"),
            LocationType = GetNullableInt(csv, "location_type"),
            ParentStation = GetString(csv, "parent_station"),
            PlatformCode = GetString(csv, "platform_code")
        };
    }

    private static GtfsRoute MapRoute(CsvReader csv)
    {
        return new GtfsRoute
        {
            RouteId = GetRequiredString(csv, "route_id"),
            AgencyId = GetString(csv, "agency_id"),
            RouteShortName = GetString(csv, "route_short_name"),
            RouteLongName = GetString(csv, "route_long_name"),
            RouteType = GetRequiredInt(csv, "route_type"),
            RouteUrl = GetString(csv, "route_url"),
            RouteColor = GetString(csv, "route_color"),
            RouteTextColor = GetString(csv, "route_text_color"),
            RouteNetwork = GetString(csv, "route_network")
        };
    }

    private static GtfsTrip MapTrip(CsvReader csv)
    {
        return new GtfsTrip
        {
            TripId = GetRequiredString(csv, "trip_id"),
            RouteId = GetRequiredString(csv, "route_id"),
            ServiceId = GetRequiredString(csv, "service_id"),
            TripShortName = GetString(csv, "trip_short_name"),
            TripHeadsign = GetString(csv, "trip_headsign"),
            DirectionId = GetNullableInt(csv, "direction_id"),
            ShapeId = GetString(csv, "shape_id"),
            WheelchairAccessible = GetNullableInt(
                csv,
                "wheelchair_accessible"),
            TripRealtimeId = GetString(csv, "trip_realtime_id")
        };
    }

    private static GtfsStopTime MapStopTime(CsvReader csv)
    {
        var arrivalSeconds = ParseGtfsTime(
            GetString(csv, "arrival_time"));

        var departureSeconds = ParseGtfsTime(
            GetString(csv, "departure_time"));

        if (arrivalSeconds is null && departureSeconds is null)
        {
            throw new InvalidDataException(
                "A GTFS stop_time record has neither arrival nor departure time.");
        }

        return new GtfsStopTime
        {
            TripId = GetRequiredString(csv, "trip_id"),
            StopId = GetRequiredString(csv, "stop_id"),
            ArrivalSeconds = arrivalSeconds,
            DepartureSeconds = departureSeconds,
            StopSequence = GetRequiredInt(csv, "stop_sequence"),
            PickupType = GetNullableInt(csv, "pickup_type"),
            DropOffType = GetNullableInt(csv, "drop_off_type"),
            ShapeDistTraveled = GetNullableDouble(
                csv,
                "shape_dist_traveled")
        };
    }

    private static GtfsCalendar MapCalendar(CsvReader csv)
    {
        return new GtfsCalendar
        {
            ServiceId = GetRequiredString(csv, "service_id"),
            Monday = GetBoolean(csv, "monday"),
            Tuesday = GetBoolean(csv, "tuesday"),
            Wednesday = GetBoolean(csv, "wednesday"),
            Thursday = GetBoolean(csv, "thursday"),
            Friday = GetBoolean(csv, "friday"),
            Saturday = GetBoolean(csv, "saturday"),
            Sunday = GetBoolean(csv, "sunday"),
            StartDate = GetRequiredDate(csv, "start_date"),
            EndDate = GetRequiredDate(csv, "end_date"),
            ServiceDesc = GetString(csv, "service_desc")
        };
    }

    private static GtfsCalendarDate MapCalendarDate(CsvReader csv)
    {
        return new GtfsCalendarDate
        {
            ServiceId = GetRequiredString(csv, "service_id"),
            Date = GetRequiredDate(csv, "date"),
            ExceptionType = GetRequiredInt(csv, "exception_type")
        };
    }

    private static GtfsFeedInfo MapFeedInfo(CsvReader csv)
    {
        return new GtfsFeedInfo
        {
            Id = 1,
            FeedId = GetString(csv, "feed_id"),
            PublisherName = GetString(csv, "feed_publisher_name"),
            PublisherUrl = GetString(csv, "feed_publisher_url"),
            FeedLanguage = GetString(csv, "feed_lang"),
            FeedStartDate = GetNullableDate(csv, "feed_start_date"),
            FeedEndDate = GetNullableDate(csv, "feed_end_date"),
            FeedVersion = GetString(csv, "feed_version")
        };
    }

    private static string? GetString(CsvReader csv, string fieldName)
    {
        var value = csv.GetField(fieldName);

        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private static string GetRequiredString(
        CsvReader csv,
        string fieldName)
    {
        return GetString(csv, fieldName)
            ?? throw new InvalidDataException(
                $"Required GTFS field is empty: {fieldName}");
    }

    private static int GetRequiredInt(CsvReader csv, string fieldName)
    {
        var value = GetString(csv, fieldName);

        if (!int.TryParse(
            value,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var result))
        {
            throw new InvalidDataException(
                $"Invalid integer value in GTFS field: {fieldName}");
        }

        return result;
    }

    private static int? GetNullableInt(CsvReader csv, string fieldName)
    {
        var value = GetString(csv, fieldName);

        if (value is null)
        {
            return null;
        }

        if (int.TryParse(
            value,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var result))
        {
            return result;
        }

        throw new InvalidDataException(
            $"Invalid integer value in GTFS field: {fieldName}");
    }

    private static double? GetNullableDouble(
        CsvReader csv,
        string fieldName)
    {
        var value = GetString(csv, fieldName);

        if (value is null)
        {
            return null;
        }

        if (double.TryParse(
            value,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var result))
        {
            return result;
        }

        throw new InvalidDataException(
            $"Invalid decimal value in GTFS field: {fieldName}");
    }

    private static bool GetBoolean(CsvReader csv, string fieldName)
    {
        var value = GetRequiredString(csv, fieldName);

        return value switch
        {
            "1" => true,
            "0" => false,
            _ => throw new InvalidDataException(
                $"Invalid boolean value in GTFS field: {fieldName}")
        };
    }

    private static DateOnly GetRequiredDate(
        CsvReader csv,
        string fieldName)
    {
        var value = GetRequiredString(csv, fieldName);

        return ParseGtfsDate(value)
            ?? throw new InvalidDataException(
                $"Invalid date value in GTFS field: {fieldName}");
    }

    private static DateOnly? GetNullableDate(
        CsvReader csv,
        string fieldName)
    {
        var value = GetString(csv, fieldName);

        return value is null
            ? null
            : ParseGtfsDate(value);
    }

    private static DateOnly? ParseGtfsDate(string value)
    {
        return DateOnly.TryParseExact(
            value,
            "yyyyMMdd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var date)
                ? date
                : null;
    }

    private static int? ParseGtfsTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var parts = value.Split(':');

        if (parts.Length != 3 ||
            !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var hours) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minutes) ||
            !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) ||
            hours < 0 ||
            minutes is < 0 or > 59 ||
            seconds is < 0 or > 59)
        {
            throw new InvalidDataException(
                $"Invalid GTFS time value: {value}");
        }

        return checked(hours * 3600 + minutes * 60 + seconds);
    }
}