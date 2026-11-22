namespace DayPlannerAI.Api.DTOs.Realtime;

public sealed class GtfsRealtimeVehicleDto
{
    public string VehicleId { get; set; } = string.Empty;

    public string TripId { get; set; } = string.Empty;

    public double Latitude { get; set; }

    public double Longitude { get; set; }

    public double? Heading { get; set; }

    public double? SpeedMetersPerSecond { get; set; }

    public double? SpeedKilometersPerHour =>
        SpeedMetersPerSecond * 3.6;

    public DateTimeOffset LastUpdatedUtc { get; set; }

    public bool IsFresh { get; set; }
}