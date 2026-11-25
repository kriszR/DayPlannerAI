using System.Globalization;
using System.Text;
using DayPlannerAI.Api.Data;
using DayPlannerAI.Api.DTOs;
using DayPlannerAI.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace DayPlannerAI.Api.Services;

public sealed class GtfsRouteSearchService
{
    private const int MinimumTransferSeconds = 10 * 60;

    private readonly ApplicationDbContext _dbContext;

    public GtfsRouteSearchService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<GtfsDirectRouteDto>> SearchDirectRoutesAsync(
        string fromCity,
        string toCity,
        DateOnly travelDate,
        TimeOnly arriveBy,
        int limit = 5,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fromCity) ||
            string.IsNullOrWhiteSpace(toCity))
        {
            throw new ArgumentException(
                "Departure and destination cities are required.");
        }

        if (Normalize(fromCity) == Normalize(toCity))
        {
            throw new ArgumentException(
                "Departure and destination must be different.");
        }

        var feed = await _dbContext.GtfsFeedInfos
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == 1, cancellationToken);

        if (feed?.FeedStartDate is DateOnly feedStart &&
            travelDate < feedStart)
        {
            throw new ArgumentOutOfRangeException(
                nameof(travelDate),
                "The requested date is earlier than the GTFS feed validity period.");
        }

        if (feed?.FeedEndDate is DateOnly feedEnd &&
            travelDate > feedEnd)
        {
            throw new ArgumentOutOfRangeException(
                nameof(travelDate),
                "The requested date is later than the GTFS feed validity period.");
        }
        var activeServices = await GetActiveServicesAsync(
            travelDate,
            cancellationToken);

        if (activeServices.Count == 0)
        {
            return [];
        }

        var activeServiceIds = activeServices.ToArray();

        var departureStops = await GetBusStationStopIdsAsync(
            fromCity,
            activeServiceIds,
            cancellationToken);

        var arrivalStops = await GetBusStationStopIdsAsync(
            toCity,
            activeServiceIds,
            cancellationToken);

        if (departureStops.Intersect(arrivalStops).Any())
        {
            throw new InvalidOperationException(
                "Departure and destination resolve to the same stops.");
        }

        var arrivalCutoffSeconds =
            (int)arriveBy.ToTimeSpan().TotalSeconds;
        var earliestDepartureSeconds =
            GetEarliestDepartureSeconds(travelDate);

        var rawResults = await (
            from departure in _dbContext.GtfsStopTimes.AsNoTracking()

            join trip in _dbContext.GtfsTrips.AsNoTracking()
                on departure.TripId equals trip.TripId

            join route in _dbContext.GtfsRoutes.AsNoTracking()
                on trip.RouteId equals route.RouteId

            join arrival in _dbContext.GtfsStopTimes.AsNoTracking()
                on departure.TripId equals arrival.TripId

            join departureStop in _dbContext.GtfsStops.AsNoTracking()
                on departure.StopId equals departureStop.StopId

            join arrivalStop in _dbContext.GtfsStops.AsNoTracking()
                on arrival.StopId equals arrivalStop.StopId

            let departureSeconds =
                departure.DepartureSeconds ?? departure.ArrivalSeconds

            let arrivalSeconds =
                arrival.ArrivalSeconds ?? arrival.DepartureSeconds

            where departureStops.Contains(departure.StopId)
                  && arrivalStops.Contains(arrival.StopId)
                  && arrival.StopSequence > departure.StopSequence
                  && departureSeconds.HasValue
                  && arrivalSeconds.HasValue
                  && arrivalSeconds.Value > earliestDepartureSeconds
                  && arrivalSeconds.Value > departureSeconds.Value
                  && arrivalSeconds.Value <= arrivalCutoffSeconds
                  && activeServiceIds.Contains(trip.ServiceId)
                  && (
                      route.RouteType == 3 ||
                      (route.RouteType >= 200 && route.RouteType <= 299)
                  )

            orderby (departureSeconds.Value <= earliestDepartureSeconds &&
                     arrivalSeconds.Value > earliestDepartureSeconds) descending,
                    arrivalSeconds descending,
                    departureSeconds descending

            select new
            {
                trip.TripId,
                trip.RouteId,
                trip.TripHeadsign,
                route.RouteShortName,
                route.RouteLongName,
                DepartureStop = departureStop.StopName,
                DeparturePlatform = departureStop.PlatformCode,
                ArrivalStop = arrivalStop.StopName,
                ArrivalPlatform = arrivalStop.PlatformCode,
                DepartureSeconds = departureSeconds.Value,
                ArrivalSeconds = arrivalSeconds.Value
            }
        )
        .Take(500)
        .ToListAsync(cancellationToken);

        return rawResults
            .GroupBy(x => x.TripId)
            .Select(group => group.First())
            .OrderByDescending(x => IsInProgress(
                travelDate,
                x.DepartureSeconds,
                x.ArrivalSeconds))
            .ThenByDescending(x => x.ArrivalSeconds)
            .ThenByDescending(x => x.DepartureSeconds)
            .Take(Math.Clamp(limit, 1, 10))
            .Select(x => new GtfsDirectRouteDto
            {
                TripId = x.TripId,
                RouteId = x.RouteId,
                RouteShortName = x.RouteShortName,
                RouteLongName = x.RouteLongName,
                TripHeadsign = x.TripHeadsign,
                DepartureStop = x.DepartureStop,
                DeparturePlatform = x.DeparturePlatform,
                DepartureTime = FormatGtfsTime(x.DepartureSeconds),
                ArrivalStop = x.ArrivalStop,
                ArrivalPlatform = x.ArrivalPlatform,
                ArrivalTime = FormatGtfsTime(x.ArrivalSeconds),
                DurationMinutes =
                    (x.ArrivalSeconds - x.DepartureSeconds) / 60,
                ServiceDate = travelDate,
                IsInProgress = IsInProgress(
                    travelDate,
                    x.DepartureSeconds,
                    x.ArrivalSeconds)
            })
            .ToList();
    }

    public async Task<List<GtfsDirectRouteDto>> SearchDirectDeparturesAsync(
    string fromCity,
    string toCity,
    DateOnly travelDate,
    TimeOnly departAfter,
    int limit = 5,
    CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fromCity) ||
            string.IsNullOrWhiteSpace(toCity))
        {
            throw new ArgumentException(
                "Departure and destination cities are required.");
        }

        if (Normalize(fromCity) == Normalize(toCity))
        {
            throw new ArgumentException(
                "Departure and destination must be different.");
        }

        var feed = await _dbContext.GtfsFeedInfos
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == 1, cancellationToken);

        if (feed?.FeedStartDate is DateOnly feedStart &&
            travelDate < feedStart)
        {
            throw new ArgumentOutOfRangeException(
                nameof(travelDate),
                "The requested date is earlier than the GTFS feed validity period.");
        }

        if (feed?.FeedEndDate is DateOnly feedEnd &&
            travelDate > feedEnd)
        {
            throw new ArgumentOutOfRangeException(
                nameof(travelDate),
                "The requested date is later than the GTFS feed validity period.");
        }

        var activeServices = await GetActiveServicesAsync(
            travelDate,
            cancellationToken);

        if (activeServices.Count == 0)
            return [];

        var activeServiceIds = activeServices.ToArray();

        var departureStops = await GetBusStationStopIdsAsync(
            fromCity,
            activeServiceIds,
            cancellationToken);

        var arrivalStops = await GetBusStationStopIdsAsync(
            toCity,
            activeServiceIds,
            cancellationToken);

        if (departureStops.Intersect(arrivalStops).Any())
        {
            throw new InvalidOperationException(
                "Departure and destination resolve to the same stops.");
        }

        var departureCutoffSeconds =
            (int)departAfter.ToTimeSpan().TotalSeconds;
        var earliestDepartureSeconds =
            GetEarliestDepartureSeconds(travelDate);
        var includeInProgressTrips =
            earliestDepartureSeconds >= 0 &&
            departureCutoffSeconds <= earliestDepartureSeconds;

        var rawResults = await (
            from departure in _dbContext.GtfsStopTimes.AsNoTracking()

            join trip in _dbContext.GtfsTrips.AsNoTracking()
                on departure.TripId equals trip.TripId

            join route in _dbContext.GtfsRoutes.AsNoTracking()
                on trip.RouteId equals route.RouteId

            join arrival in _dbContext.GtfsStopTimes.AsNoTracking()
                on departure.TripId equals arrival.TripId

            join departureStop in _dbContext.GtfsStops.AsNoTracking()
                on departure.StopId equals departureStop.StopId

            join arrivalStop in _dbContext.GtfsStops.AsNoTracking()
                on arrival.StopId equals arrivalStop.StopId

            let departureSeconds =
                departure.DepartureSeconds ?? departure.ArrivalSeconds

            let arrivalSeconds =
                arrival.ArrivalSeconds ?? arrival.DepartureSeconds

            where departureStops.Contains(departure.StopId)
                  && arrivalStops.Contains(arrival.StopId)
                  && arrival.StopSequence > departure.StopSequence
                  && departureSeconds.HasValue
                  && arrivalSeconds.HasValue
                  && (
                      (departureSeconds.Value >= departureCutoffSeconds &&
                       departureSeconds.Value > earliestDepartureSeconds) ||
                      (includeInProgressTrips &&
                       departureSeconds.Value <= earliestDepartureSeconds &&
                       arrivalSeconds.Value > earliestDepartureSeconds)
                  )
                  && arrivalSeconds.Value > departureSeconds.Value
                  && activeServiceIds.Contains(trip.ServiceId)
                  && (
                      route.RouteType == 3 ||
                      (route.RouteType >= 200 && route.RouteType <= 299)
                  )

            orderby (departureSeconds.Value <= earliestDepartureSeconds &&
                     arrivalSeconds.Value > earliestDepartureSeconds) descending,
                    departureSeconds ascending,
                    arrivalSeconds ascending

            select new
            {
                trip.TripId,
                trip.RouteId,
                trip.TripHeadsign,
                route.RouteShortName,
                route.RouteLongName,

                DepartureStop = departureStop.StopName,
                DeparturePlatform = departureStop.PlatformCode,

                ArrivalStop = arrivalStop.StopName,
                ArrivalPlatform = arrivalStop.PlatformCode,

                DepartureSeconds = departureSeconds.Value,
                ArrivalSeconds = arrivalSeconds.Value
            }
        )
        .Take(1000)
        .ToListAsync(cancellationToken);

        return rawResults
            .GroupBy(x => x.TripId)
            .Select(group => group
                .OrderBy(x => x.DepartureSeconds)
                .ThenBy(x => x.ArrivalSeconds)
                .First())
            .OrderBy(x => x.DepartureSeconds)
            .ThenBy(x => x.ArrivalSeconds)
            .Take(Math.Clamp(limit, 1, 10))
            .Select(x => new GtfsDirectRouteDto
            {
                TripId = x.TripId,
                RouteId = x.RouteId,
                RouteShortName = x.RouteShortName,
                RouteLongName = x.RouteLongName,
                TripHeadsign = x.TripHeadsign,

                DepartureStop = x.DepartureStop,
                DeparturePlatform = x.DeparturePlatform,
                DepartureTime = FormatGtfsTime(x.DepartureSeconds),

                ArrivalStop = x.ArrivalStop,
                ArrivalPlatform = x.ArrivalPlatform,
                ArrivalTime = FormatGtfsTime(x.ArrivalSeconds),

                DurationMinutes =
                    (x.ArrivalSeconds - x.DepartureSeconds) / 60,

                ServiceDate = travelDate,
                IsInProgress = IsInProgress(
                    travelDate,
                    x.DepartureSeconds,
                    x.ArrivalSeconds)
            })
            .ToList();
    }

    public Task<List<GtfsJourneyDto>> SearchOneTransferRoutesAsync(
        string fromCity,
        string toCity,
        DateOnly travelDate,
        TimeOnly arriveBy,
        int limit = 5,
        CancellationToken cancellationToken = default)
    {
        return SearchOneTransferRoutesCoreAsync(
            fromCity,
            toCity,
            travelDate,
            arriveBy,
            null,
            limit,
            cancellationToken);
    }

    public Task<List<GtfsJourneyDto>> SearchOneTransferRoutesAfterAsync(
        string fromCity,
        string toCity,
        DateOnly travelDate,
        TimeOnly departAfter,
        int limit = 5,
        CancellationToken cancellationToken = default)
    {
        return SearchOneTransferRoutesCoreAsync(
            fromCity,
            toCity,
            travelDate,
            null,
            departAfter,
            limit,
            cancellationToken);
    }

    private async Task<List<GtfsJourneyDto>> SearchOneTransferRoutesCoreAsync(
        string fromCity,
        string toCity,
        DateOnly travelDate,
        TimeOnly? arriveBy,
        TimeOnly? departAfter,
        int limit,
        CancellationToken cancellationToken)
    {
        if (arriveBy.HasValue == departAfter.HasValue)
        {
            throw new ArgumentException(
                "Exactly one of arriveBy or departAfter must be provided.");
        }

        if (string.IsNullOrWhiteSpace(fromCity) ||
            string.IsNullOrWhiteSpace(toCity))
        {
            throw new ArgumentException(
                "Departure and destination cities are required.");
        }

        if (Normalize(fromCity) == Normalize(toCity))
        {
            throw new ArgumentException(
                "Departure and destination must be different.");
        }

        var feed = await _dbContext.GtfsFeedInfos
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == 1, cancellationToken);

        if (feed?.FeedStartDate is DateOnly feedStart &&
            travelDate < feedStart)
        {
            throw new ArgumentOutOfRangeException(
                nameof(travelDate),
                "The requested date is earlier than the GTFS feed validity period.");
        }

        if (feed?.FeedEndDate is DateOnly feedEnd &&
            travelDate > feedEnd)
        {
            throw new ArgumentOutOfRangeException(
                nameof(travelDate),
                "The requested date is later than the GTFS feed validity period.");
        }

        var activeServices = await GetActiveServicesAsync(
            travelDate,
            cancellationToken);

        if (activeServices.Count == 0)
            return [];

        var activeServiceIds = activeServices.ToArray();

        var departureStops = await GetBusStationStopIdsAsync(
            fromCity,
            activeServiceIds,
            cancellationToken);

        var destinationStops = await GetBusStationStopIdsAsync(
            toCity,
            activeServiceIds,
            cancellationToken);

        if (departureStops.Intersect(destinationStops).Any())
        {
            throw new InvalidOperationException(
                "Departure and destination resolve to the same stops.");
        }

        // A missing arrival deadline means that any timetable arrival time is
        // allowed. A departure-after search still respects both the requested
        // time and the current local time for today's service date.
        var arrivalCutoffSeconds = arriveBy.HasValue
            ? (int)arriveBy.Value.ToTimeSpan().TotalSeconds
            : int.MaxValue;

        var departureCutoffSeconds = departAfter.HasValue
            ? (int)departAfter.Value.ToTimeSpan().TotalSeconds
            : 0;

        var earliestDepartureSeconds =
            GetEarliestDepartureSeconds(travelDate);

        var minimumDepartureSeconds = Math.Max(
            departureCutoffSeconds,
            earliestDepartureSeconds + 1);
        var includeInProgressFirstLegs =
            earliestDepartureSeconds >= 0 &&
            (arriveBy.HasValue ||
             departureCutoffSeconds <= earliestDepartureSeconds);

        // Find all possible first journey legs from the departure city.
        var firstLegs = await (
            from departure in _dbContext.GtfsStopTimes.AsNoTracking()

            join trip in _dbContext.GtfsTrips.AsNoTracking()
                on departure.TripId equals trip.TripId

            join route in _dbContext.GtfsRoutes.AsNoTracking()
                on trip.RouteId equals route.RouteId

            join arrival in _dbContext.GtfsStopTimes.AsNoTracking()
                on departure.TripId equals arrival.TripId

            join departureStop in _dbContext.GtfsStops.AsNoTracking()
                on departure.StopId equals departureStop.StopId

            join arrivalStop in _dbContext.GtfsStops.AsNoTracking()
                on arrival.StopId equals arrivalStop.StopId

            let departureSeconds =
                departure.DepartureSeconds ?? departure.ArrivalSeconds

            let arrivalSeconds =
                arrival.ArrivalSeconds ?? arrival.DepartureSeconds

            where departureStops.Contains(departure.StopId)
                  && arrival.StopSequence > departure.StopSequence
                  && departureSeconds.HasValue
                  && arrivalSeconds.HasValue
                  && (
                      departureSeconds.Value >= minimumDepartureSeconds ||
                      (includeInProgressFirstLegs &&
                       departureSeconds.Value <= earliestDepartureSeconds &&
                       arrivalSeconds.Value > earliestDepartureSeconds)
                  )
                  && arrivalSeconds.Value > departureSeconds.Value
                  && arrivalSeconds.Value < arrivalCutoffSeconds
                  && activeServiceIds.Contains(trip.ServiceId)
                  && (
                      route.RouteType == 3 ||
                      (route.RouteType >= 200 && route.RouteType <= 299)
                  )

            select new RawJourneyLeg(
                trip.TripId,
                trip.RouteId,
                route.RouteShortName,
                route.RouteLongName,
                trip.TripHeadsign,
                departure.StopId,
                departureStop.StopName,
                departureStop.PlatformCode,
                departureSeconds.Value,
                arrival.StopId,
                arrivalStop.StopName,
                arrivalStop.PlatformCode,
                arrivalSeconds.Value
            )
        )
        .ToListAsync(cancellationToken);

        if (firstLegs.Count == 0)
            return [];

        // Load the station information for all first-leg arrival stops.
        var firstArrivalStopIds = firstLegs
            .Select(x => x.ArrivalStopId)
            .Distinct()
            .ToArray();

        var firstArrivalMetadata = await _dbContext.GtfsStops
            .AsNoTracking()
            .Where(x => firstArrivalStopIds.Contains(x.StopId))
            .Select(x => new
            {
                x.StopId,
                x.ParentStation
            })
            .ToListAsync(cancellationToken);

        var arrivalMetadataById = firstArrivalMetadata
            .ToDictionary(x => x.StopId);

        // Identify the station groups that can be used for transfers.
        var transferStationIds = firstArrivalMetadata
            .Select(x => x.ParentStation ?? x.StopId)
            .Distinct()
            .ToArray();

        // Exclude the departure and destination station groups.
        var endpointStopIds = departureStops
            .Concat(destinationStops)
            .Distinct()
            .ToArray();

        var endpointMetadata = await _dbContext.GtfsStops
            .AsNoTracking()
            .Where(x => endpointStopIds.Contains(x.StopId))
            .Select(x => new
            {
                x.StopId,
                x.ParentStation
            })
            .ToListAsync(cancellationToken);

        var endpointStationIds = endpointMetadata
            .Select(x => x.ParentStation ?? x.StopId)
            .ToHashSet();

        transferStationIds = transferStationIds
            .Where(x => !endpointStationIds.Contains(x))
            .ToArray();

        if (transferStationIds.Length == 0)
            return [];

        // Include every platform belonging to a possible transfer station.
        var transferStopMetadata = await _dbContext.GtfsStops
            .AsNoTracking()
            .Where(x =>
                transferStationIds.Contains(x.StopId) ||
                (x.ParentStation != null &&
                 transferStationIds.Contains(x.ParentStation)))
            .Select(x => new
            {
                x.StopId,
                x.ParentStation
            })
            .ToListAsync(cancellationToken);

        var transferStopToStation = transferStopMetadata
            .ToDictionary(
                x => x.StopId,
                x => x.ParentStation ?? x.StopId);

        var transferStopIds = transferStopToStation.Keys.ToArray();

        // Any valid second leg must depart after the earliest possible first-leg
        // arrival plus the minimum transfer time.
        var earliestSecondDepartureSeconds =
            firstLegs.Min(x => x.ArrivalSeconds) + MinimumTransferSeconds;

        // Find possible second journey legs to the destination city.
        var secondLegs = await (
            from departure in _dbContext.GtfsStopTimes.AsNoTracking()

            join trip in _dbContext.GtfsTrips.AsNoTracking()
                on departure.TripId equals trip.TripId

            join route in _dbContext.GtfsRoutes.AsNoTracking()
                on trip.RouteId equals route.RouteId

            join arrival in _dbContext.GtfsStopTimes.AsNoTracking()
                on departure.TripId equals arrival.TripId

            join departureStop in _dbContext.GtfsStops.AsNoTracking()
                on departure.StopId equals departureStop.StopId

            join arrivalStop in _dbContext.GtfsStops.AsNoTracking()
                on arrival.StopId equals arrivalStop.StopId

            let departureSeconds =
                departure.DepartureSeconds ?? departure.ArrivalSeconds

            let arrivalSeconds =
                arrival.ArrivalSeconds ?? arrival.DepartureSeconds

            where transferStopIds.Contains(departure.StopId)
                  && destinationStops.Contains(arrival.StopId)
                  && arrival.StopSequence > departure.StopSequence
                  && departureSeconds.HasValue
                  && arrivalSeconds.HasValue
                  && departureSeconds.Value >= earliestSecondDepartureSeconds
                  && arrivalSeconds.Value > departureSeconds.Value
                  && arrivalSeconds.Value <= arrivalCutoffSeconds
                  && activeServiceIds.Contains(trip.ServiceId)
                  && (
                      route.RouteType == 3 ||
                      (route.RouteType >= 200 && route.RouteType <= 299)
                  )

            select new RawJourneyLeg(
                trip.TripId,
                trip.RouteId,
                route.RouteShortName,
                route.RouteLongName,
                trip.TripHeadsign,
                departure.StopId,
                departureStop.StopName,
                departureStop.PlatformCode,
                departureSeconds.Value,
                arrival.StopId,
                arrivalStop.StopName,
                arrivalStop.PlatformCode,
                arrivalSeconds.Value
            )
        )
        .ToListAsync(cancellationToken);

        if (secondLegs.Count == 0)
            return [];

        // Group and sort second legs so impossible early departures can be
        // skipped with binary search for each first leg.
        var secondLegsByStation = secondLegs
            .Where(x => transferStopToStation.ContainsKey(x.DepartureStopId))
            .GroupBy(x => transferStopToStation[x.DepartureStopId])
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(x => x.DepartureSeconds)
                    .ThenBy(x => x.ArrivalSeconds)
                    .ToArray());

        var resultLimit = Math.Clamp(limit, 1, 10);
        var bestJourneys = new List<(
            RawJourneyLeg First,
            RawJourneyLeg Second,
            int DurationSeconds,
            string UniqueKey)>(resultLimit);

        // Connect compatible first and second legs while keeping only the
        // best requested number of results in memory.
        foreach (var first in firstLegs)
        {
            if (!arrivalMetadataById.TryGetValue(
                    first.ArrivalStopId,
                    out var firstArrival))
            {
                continue;
            }

            var transferStationId =
                firstArrival.ParentStation ?? firstArrival.StopId;

            if (!secondLegsByStation.TryGetValue(
                    transferStationId,
                    out var possibleSecondLegs))
            {
                continue;
            }

            var minimumSecondDepartureSeconds =
                first.ArrivalSeconds + MinimumTransferSeconds;

            var firstPossibleSecondIndex = FindFirstDepartureAtOrAfter(
                possibleSecondLegs,
                minimumSecondDepartureSeconds);

            for (var i = firstPossibleSecondIndex;
                 i < possibleSecondLegs.Length;
                 i++)
            {
                var second = possibleSecondLegs[i];

                // A transfer must use another trip.
                if (first.TripId == second.TripId)
                    continue;

                // The result query already enforces the arrival deadline.
                // Different platform/child-stop records can describe the same
                // physical journey. Deduplicate by trips, times and transfer
                // station instead of platform stop IDs.
                var uniqueKey =
                    $"{first.TripId}|{first.DepartureSeconds}|{first.ArrivalSeconds}|" +
                    $"{transferStationId}|{second.TripId}|" +
                    $"{second.DepartureSeconds}|{second.ArrivalSeconds}";

                if (bestJourneys.Any(x => x.UniqueKey == uniqueKey))
                    continue;

                var candidateDurationSeconds =
                    second.ArrivalSeconds - first.DepartureSeconds;

                // For departure-after searches, show the earliest feasible
                // departure first. For arrive-by searches, prefer the latest
                // feasible departure, then the shorter journey.
                var insertIndex = bestJourneys.FindIndex(existing =>
                    departAfter.HasValue
                        ? first.DepartureSeconds < existing.First.DepartureSeconds ||
                          (first.DepartureSeconds == existing.First.DepartureSeconds &&
                           (second.ArrivalSeconds < existing.Second.ArrivalSeconds ||
                            (second.ArrivalSeconds == existing.Second.ArrivalSeconds &&
                             candidateDurationSeconds < existing.DurationSeconds)))
                        : first.DepartureSeconds > existing.First.DepartureSeconds ||
                          (first.DepartureSeconds == existing.First.DepartureSeconds &&
                           (candidateDurationSeconds < existing.DurationSeconds ||
                            (candidateDurationSeconds == existing.DurationSeconds &&
                             second.ArrivalSeconds < existing.Second.ArrivalSeconds))));

                if (insertIndex < 0)
                {
                    if (bestJourneys.Count >= resultLimit)
                        continue;

                    bestJourneys.Add((
                        first,
                        second,
                        candidateDurationSeconds,
                        uniqueKey));
                }
                else
                {
                    bestJourneys.Insert(insertIndex, (
                        first,
                        second,
                        candidateDurationSeconds,
                        uniqueKey));
                }

                if (bestJourneys.Count > resultLimit)
                    bestJourneys.RemoveAt(bestJourneys.Count - 1);
            }
        }

        return bestJourneys
            .Select(x => new GtfsJourneyDto
            {
                DepartureStop = x.First.DepartureStop,
                ArrivalStop = x.Second.ArrivalStop,
                DepartureTime = FormatGtfsTime(x.First.DepartureSeconds),
                ArrivalTime = FormatGtfsTime(x.Second.ArrivalSeconds),
                TotalDurationMinutes = x.DurationSeconds / 60,
                TransferCount = 1,
                ServiceDate = travelDate,
                Legs =
                [
                    MapJourneyLeg(x.First, travelDate),
                    MapJourneyLeg(x.Second, travelDate)
                ]
            })
            .ToList();
    }

    private static int FindFirstDepartureAtOrAfter(
        IReadOnlyList<RawJourneyLeg> legs,
        int minimumDepartureSeconds)
    {
        var low = 0;
        var high = legs.Count;

        while (low < high)
        {
            var middle = low + ((high - low) / 2);

            if (legs[middle].DepartureSeconds < minimumDepartureSeconds)
                low = middle + 1;
            else
                high = middle;
        }

        return low;
    }

    private async Task<List<string>> GetBusStationStopIdsAsync(
    string city,
    string[] activeServiceIds,
    CancellationToken cancellationToken)
    {
        var normalizedCity = Normalize(city);

        // Load parent stops and station records.
        var parentStops = await _dbContext.GtfsStops
            .AsNoTracking()
            .Where(stop => stop.ParentStation == null)
            .Select(stop => new
            {
                stop.StopId,
                stop.StopName,
                stop.LocationType
            })
            .ToListAsync(cancellationToken);

        // Match stops by the city prefix in their names.
        var cityStops = parentStops
            .Where(stop =>
            {
                var cityPart = GetCityPrefix(stop.StopName);
                return Normalize(cityPart) == normalizedCity;
            })
            .ToList();

        // Identify station records and standalone stops.
        var stationRows = cityStops
            .Where(stop => stop.LocationType == 1)
            .ToList();

        var standaloneRows = cityStops
            .Where(stop =>
                stop.LocationType == null ||
                stop.LocationType == 0)
            .ToList();

        var stationIds = stationRows
            .Select(stop => stop.StopId)
            .ToArray();

        // Load all child stops belonging to the candidate stations.
        var childStops = await _dbContext.GtfsStops
            .AsNoTracking()
            .Where(stop =>
                stop.ParentStation != null &&
                stationIds.Contains(stop.ParentStation))
            .Select(stop => new
            {
                stop.StopId,
                stop.ParentStation
            })
            .ToListAsync(cancellationToken);

        var candidates = new List<BusStopCandidate>();

        // Build one candidate group for every station.
        foreach (var station in stationRows)
        {
            var stopIds = childStops
                .Where(stop => stop.ParentStation == station.StopId)
                .Select(stop => stop.StopId)
                .Append(station.StopId)
                .Distinct()
                .ToList();

            candidates.Add(new BusStopCandidate(
                station.StopId,
                station.StopName,
                stopIds));
        }

        // Also support GTFS feeds that contain standalone stops.
        foreach (var stop in standaloneRows)
        {
            candidates.Add(new BusStopCandidate(
                stop.StopId,
                stop.StopName,
                [stop.StopId]));
        }

        if (candidates.Count == 0)
        {
            throw new InvalidOperationException(
                $"No GTFS stop candidates were found for city: {city}");
        }

        var candidateStopIds = candidates
            .SelectMany(candidate => candidate.StopIds)
            .Distinct()
            .ToArray();

        // Find active bus services passing through candidate stops.
        var scheduledCalls = await (
            from stopTime in _dbContext.GtfsStopTimes.AsNoTracking()

            join trip in _dbContext.GtfsTrips.AsNoTracking()
                on stopTime.TripId equals trip.TripId

            join route in _dbContext.GtfsRoutes.AsNoTracking()
                on trip.RouteId equals route.RouteId

            where candidateStopIds.Contains(stopTime.StopId)
                  && activeServiceIds.Contains(trip.ServiceId)
                  && (
                      route.RouteType == 3 ||
                      (route.RouteType >= 200 &&
                       route.RouteType <= 299)
                  )

            select new
            {
                stopTime.StopId,
                trip.TripId,
                trip.RouteId
            }
        )
        .ToListAsync(cancellationToken);

        // Rank candidates using actual timetable coverage and station names.
        var rankedCandidates = candidates
            .Select(candidate =>
            {
                var calls = scheduledCalls
                    .Where(call => candidate.StopIds.Contains(call.StopId))
                    .ToList();

                return new
                {
                    Candidate = candidate,
                    RouteCount = calls
                        .Select(call => call.RouteId)
                        .Distinct()
                        .Count(),
                    TripCount = calls
                        .Select(call => call.TripId)
                        .Distinct()
                        .Count(),
                    DepartureCount = calls.Count,
                    NameRank = GetHubNameRank(candidate.StopName)
                };
            })
            .Where(candidate => candidate.TripCount > 0)
            .ToList();

        if (rankedCandidates.Count == 0)
        {
            throw new InvalidOperationException(
                $"No active bus services were found for city: {city}");
        }

        // If the feed identifies one or more actual bus terminals by name,
        // include all of them instead of silently discarding every terminal
        // except the one with the highest timetable count.
        var namedBusStationStopIds = rankedCandidates
            .Where(candidate => candidate.NameRank >= 2)
            .SelectMany(candidate => candidate.Candidate.StopIds)
            .Distinct()
            .ToList();

        if (namedBusStationStopIds.Count > 0)
            return namedBusStationStopIds;

        // Fallback for feeds that do not identify terminals explicitly.
        var bestCandidate = rankedCandidates
            .OrderByDescending(candidate => candidate.NameRank)
            .ThenByDescending(candidate => candidate.RouteCount)
            .ThenByDescending(candidate => candidate.TripCount)
            .ThenByDescending(candidate => candidate.DepartureCount)
            .ThenBy(candidate => Normalize(candidate.Candidate.StopName))
            .First();

        return bestCandidate.Candidate.StopIds;
    }

    private async Task<HashSet<string>> GetActiveServicesAsync(
        DateOnly date,
        CancellationToken cancellationToken)
    {
        var calendars = _dbContext.GtfsCalendars
            .AsNoTracking()
            .Where(calendar =>
                calendar.StartDate <= date &&
                calendar.EndDate >= date);

        calendars = date.DayOfWeek switch
        {
            DayOfWeek.Monday => calendars.Where(x => x.Monday),
            DayOfWeek.Tuesday => calendars.Where(x => x.Tuesday),
            DayOfWeek.Wednesday => calendars.Where(x => x.Wednesday),
            DayOfWeek.Thursday => calendars.Where(x => x.Thursday),
            DayOfWeek.Friday => calendars.Where(x => x.Friday),
            DayOfWeek.Saturday => calendars.Where(x => x.Saturday),
            DayOfWeek.Sunday => calendars.Where(x => x.Sunday),
            _ => calendars.Where(x => false)
        };

        var activeServices = (
            await calendars
                .Select(x => x.ServiceId)
                .ToListAsync(cancellationToken)
        ).ToHashSet();

        var exceptions = await _dbContext.GtfsCalendarDates
            .AsNoTracking()
            .Where(x => x.Date == date)
            .ToListAsync(cancellationToken);

        foreach (var exception in exceptions)
        {
            if (exception.ExceptionType == 1)
            {
                activeServices.Add(exception.ServiceId);
            }
            else if (exception.ExceptionType == 2)
            {
                activeServices.Remove(exception.ServiceId);
            }
        }

        return activeServices;
    }

    private static int GetEarliestDepartureSeconds(DateOnly travelDate)
    {
        var timeZoneId = OperatingSystem.IsWindows()
            ? "Central Europe Standard Time"
            : "Europe/Budapest";

        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var localNow = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, timeZone);
        var today = DateOnly.FromDateTime(localNow.DateTime);

        // Future service dates must not be restricted by the current clock time.
        if (travelDate != today)
            return -1;

        // GTFS times are stored as whole seconds. Exclude departures at or before now.
        return (int)Math.Floor(localNow.TimeOfDay.TotalSeconds);
    }

    private static bool IsInProgress(
        DateOnly travelDate,
        int departureSeconds,
        int arrivalSeconds)
    {
        var currentSeconds = GetEarliestDepartureSeconds(travelDate);

        return currentSeconds >= 0 &&
               departureSeconds <= currentSeconds &&
               arrivalSeconds > currentSeconds;
    }

    private static string FormatGtfsTime(int seconds)
    {
        var hours = seconds / 3600;
        var minutes = (seconds % 3600) / 60;

        return $"{hours:00}:{minutes:00}";
    }

    private static string Normalize(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);

        var builder = new StringBuilder();

        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) !=
                UnicodeCategory.NonSpacingMark)
            {
                if (char.IsLetterOrDigit(character))
                {
                    builder.Append(char.ToLowerInvariant(character));
                }
            }
        }

        return builder.ToString();
    }

    private static string GetCityPrefix(string stopName)
    {
        var commaIndex = stopName.IndexOf(',');

        return commaIndex >= 0
            ? stopName[..commaIndex].Trim()
            : stopName.Trim();
    }

    private static int GetHubNameRank(string stopName)
    {
        var normalized = Normalize(stopName);

        if (normalized.Contains("autobuszallomas"))
            return 3;

        if (normalized.Contains("buszpalyaudvar"))
            return 2;

        if (normalized.Contains("kozpont"))
            return 1;

        return 0;
    }

    private sealed record RawJourneyLeg(
    string TripId,
    string RouteId,
    string? RouteShortName,
    string? RouteLongName,
    string? TripHeadsign,
    string DepartureStopId,
    string DepartureStop,
    string? DeparturePlatform,
    int DepartureSeconds,
    string ArrivalStopId,
    string ArrivalStop,
    string? ArrivalPlatform,
    int ArrivalSeconds);

    private static GtfsJourneyLegDto MapJourneyLeg(
        RawJourneyLeg leg,
        DateOnly serviceDate)
    {
        return new GtfsJourneyLegDto
        {
            TripId = leg.TripId,
            RouteId = leg.RouteId,
            RouteShortName = leg.RouteShortName,
            RouteLongName = leg.RouteLongName,
            TripHeadsign = leg.TripHeadsign,
            DepartureStop = leg.DepartureStop,
            DeparturePlatform = leg.DeparturePlatform,
            DepartureTime = FormatGtfsTime(leg.DepartureSeconds),
            ArrivalStop = leg.ArrivalStop,
            ArrivalPlatform = leg.ArrivalPlatform,
            ArrivalTime = FormatGtfsTime(leg.ArrivalSeconds),
            DurationMinutes =
                (leg.ArrivalSeconds - leg.DepartureSeconds) / 60,
            IsInProgress = IsInProgress(
                serviceDate,
                leg.DepartureSeconds,
                leg.ArrivalSeconds)
        };
    }

    private sealed record BusStopCandidate(
        string StopId,
        string StopName,
        List<string> StopIds);
}