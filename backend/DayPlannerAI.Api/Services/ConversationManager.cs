using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DayPlannerAI.Api.DTOs;
using DayPlannerAI.Api.Models;

namespace DayPlannerAI.Api.Services;

public sealed class ConversationManager
{
    private static readonly TimeZoneInfo BudapestTimeZone =
        GetBudapestTimeZone();

    public bool IsTravelConversation(
    IReadOnlyList<AiChatMessageRequest> messages)
    {
        var text = NormalizeForComparison(
            string.Join(
                " ",
                messages
                    .Where(message => message.Role == "user")
                    .Select(message => message.Content)));

        string[] travelTerms =
        [
            "busz",
        "jarat",
        "menetrend",
        "utaz",
        "eljut",
        "vonat",
        "indul",
        "erkez",
        "kozleked",
        "megallo",
        "menni",
        "mennek"
        ];

        return travelTerms.Any(term =>
            text.Contains(term, StringComparison.Ordinal));
    }

    public TravelPlan BuildTravelPlan(
    TravelPlan? previousPlan,
    TravelPlanExtraction extraction,
    IReadOnlyList<AiChatMessageRequest> messages)
    {
        var userMessages = messages
            .Where(message => message.Role == "user")
            .ToList();

        var allUserText = string.Join(
            " ",
            userMessages.Select(message => message.Content));

        var latestUserText = userMessages.LastOrDefault()?.Content ?? "";

        var extractedDeparture = ValidateMentionedLocation(
    extraction.DepartureLocation,
    latestUserText,
    isDestination: false);

        var extractedDestination = ValidateMentionedLocation(
            extraction.Destination,
            latestUserText,
            isDestination: true);

        var newDate = ResolveTravelDate(latestUserText);
        var newArrivalTime = ResolveArrivalTime(latestUserText);
        var newTransportMode = ResolveTransportMode(latestUserText);

        return new TravelPlan
        {
            DepartureLocation =
                extractedDeparture ?? previousPlan?.DepartureLocation,

            Destination =
                extractedDestination ?? previousPlan?.Destination,

            TravelDate =
                newDate ?? previousPlan?.TravelDate,

            ArrivalTime =
                newArrivalTime ?? previousPlan?.ArrivalTime,

            TransportMode =
                newTransportMode != "unknown"
                    ? newTransportMode
                    : previousPlan?.TransportMode ?? "unknown"
        };
    }

    public string BuildTransferTimetableResponse(
    TravelPlan plan,
    IReadOnlyList<GtfsJourneyDto> journeys)
    {
        if (plan.DepartureLocation is null ||
            plan.Destination is null ||
            plan.TravelDate is null ||
            plan.ArrivalTime is null)
        {
            throw new InvalidOperationException(
                "The travel plan is incomplete.");
        }

        if (journeys.Count == 0)
        {
            return
                $"Nem találtam közvetlen vagy egy átszállással teljesíthető " +
                $"buszjáratot {plan.DepartureLocation} és {plan.Destination} között, " +
                $"amely legkésőbb {plan.ArrivalTime.Value:HH:mm}-ra megérkezne. " +
                "Ez nem zárja ki a több átszállással elérhető lehetőségeket.";
        }

        var orderedJourneys = journeys
            .OrderByDescending(x => ParseGtfsTimeSeconds(x.ArrivalTime))
            .ThenBy(x => x.TotalDurationMinutes)
            .ToList();

        var result = new StringBuilder();

        result.AppendLine(
            $"Nem találtam megfelelő közvetlen buszjáratot, " +
            $"de találtam {journeys.Count} átszállásos lehetőséget.");

        result.AppendLine();

        result.AppendLine("Lehetséges útvonalak:");

        for (var i = 0; i < orderedJourneys.Count; i++)
        {
            var journey = orderedJourneys[i];

            result.AppendLine();
            result.AppendLine(
                $"{i + 1}. útvonal: " +
                $"{journey.DepartureTime} – {journey.ArrivalTime}, " +
                $"{journey.TotalDurationMinutes} perc, " +
                $"{journey.TransferCount} átszállás.");

            for (var legIndex = 0;
                 legIndex < journey.Legs.Count;
                 legIndex++)
            {
                var leg = journey.Legs[legIndex];

                var routeName =
                    leg.RouteShortName ??
                    leg.RouteLongName ??
                    leg.TripId;

                result.AppendLine(
                    $"  {legIndex + 1}. járat ({routeName}): " +
                    $"{leg.DepartureTime} {leg.DepartureStop} → " +
                    $"{leg.ArrivalTime} {leg.ArrivalStop}");

                if (legIndex < journey.Legs.Count - 1)
                {
                    var nextLeg = journey.Legs[legIndex + 1];

                    var waitingMinutes =
                        (ParseGtfsTimeSeconds(nextLeg.DepartureTime) -
                         ParseGtfsTimeSeconds(leg.ArrivalTime)) / 60;

                    result.AppendLine(
                        $"  Átszállási idő: {waitingMinutes} perc.");
                }
            }
        }

        var bestJourney = orderedJourneys[0];

        var earlyMinutes = Math.Max(
            0,
            (int)(plan.ArrivalTime.Value.ToTimeSpan().TotalSeconds -
                  ParseGtfsTimeSeconds(bestJourney.ArrivalTime)) / 60);

        result.AppendLine();

        result.AppendLine(
            $"A legkésőbb érkező ajánlott útvonal " +
            $"{earlyMinutes} perccel a kívánt időpont előtt érkezik.");

        return result.ToString().Trim();
    }

    /* public string BuildNextResponse(
        TravelPlan plan,
        IReadOnlyList<AiChatMessageRequest> messages)
    {
        var lastUserMessage = messages
            .LastOrDefault(message => message.Role == "user")
            ?.Content ?? string.Empty;

        if (plan.Destination is not null &&
            lastUserMessage.Contains(
                "melyik város",
                StringComparison.OrdinalIgnoreCase))
        {
            return $"{plan.Destination} a célállomásod.";
        }

        if (plan.Destination is null)
        {
            return "Hová szeretnél eljutni?";
        }

        if (plan.DepartureLocation is null)
        {
            return "Honnan indulsz?";
        }

        if (plan.TravelDate is null)
        {
            return "Melyik nap szeretnél utazni?";
        }

        if (plan.ArrivalTime is null)
        {
            return "Mikorra kell odaérned?";
        }

        if (plan.TransportMode == "unknown")
        {
            return "Milyen közlekedési eszközzel szeretnél utazni?";
        }

        var date = plan.TravelDate.Value.ToString(
            "yyyy. MMMM d.",
            CultureInfo.GetCultureInfo("hu-HU"));

        var arrivalTime = plan.ArrivalTime.Value.ToString("HH:mm");

        var transport = plan.TransportMode switch
        {
            "bus" => "busszal",
            "train" => "vonattal",
            "car" => "autóval",
            "walking" => "gyalog",
            _ => "ismeretlen közlekedési móddal"
        };

        return
            $"Rendben! {date} {plan.DepartureLocation} és " +
            $"{plan.Destination} között szeretnél utazni {transport}, " +
            $"és {arrivalTime}-ra kell odaérned. " +
            "A konkrét járatot még nem tudom ellenőrizni, " +
            "mert a menetrendi adatforrás nincs bekötve.";
    } */

    public void ApplyLatestTimeIntent(
        TravelPlan plan,
        string latestUserText)
    {
        if (string.IsNullOrWhiteSpace(latestUserText))
            return;

        var text = NormalizeTravelIntent(latestUserText);

        var now = GetBudapestNow();
        var today = DateOnly.FromDateTime(now.DateTime);

        // Explicit date recognition.
        if (Regex.IsMatch(text, @"\bholnaputan\b"))
        {
            plan.TravelDate = today.AddDays(2);
        }
        else if (Regex.IsMatch(text, @"\bholnap\b"))
        {
            plan.TravelDate = today.AddDays(1);
        }
        else if (Regex.IsMatch(text, @"\bma\b|\bmai nap\b"))
        {
            plan.TravelDate = today;
        }

        // Current departure intent.
        var wantsToDepartNow = Regex.IsMatch(
            text,
            @"\b(most|azonnal|jelenleg|ebben a pillanatban)\b");

        if (wantsToDepartNow)
        {
            plan.TravelDate = today;
            plan.DepartureTime = TimeOnly.FromDateTime(now.DateTime);
            plan.ArrivalTime = null;
            plan.SearchMode = "departAfter";

            return;
        }

        // Explicit clock-time recognition.
        var timeMatch = Regex.Match(
            text,
            @"(?<!\d)(?<hour>[01]?\d|2[0-3])[:.](?<minute>[0-5]\d)(?:\s*-?\s*(?<suffix>ra|re|kor))?(?!\d)");

        if (!timeMatch.Success)
            return;

        if (!int.TryParse(timeMatch.Groups["hour"].Value, out var hour) ||
            !int.TryParse(timeMatch.Groups["minute"].Value, out var minute))
        {
            return;
        }

        var requestedTime = new TimeOnly(hour, minute);
        var suffix = timeMatch.Groups["suffix"].Value;

        var arrivalIntent =
            suffix is "ra" or "re" ||
            Regex.IsMatch(text, @"\b(erjek|odaerjek|erkezzen|erkezni|erkezes)\b");

        var departureIntent =
            suffix == "kor" ||
            Regex.IsMatch(text, @"\b(indulok|indulni|induljak|indulas)\b");

        if (arrivalIntent)
        {
            plan.ArrivalTime = requestedTime;
            plan.DepartureTime = null;
            plan.SearchMode = "arriveBy";
        }
        else if (departureIntent)
        {
            plan.DepartureTime = requestedTime;
            plan.ArrivalTime = null;
            plan.SearchMode = "departAfter";
        }
    }

    public string BuildDepartureTimetableResponse(
    TravelPlan plan,
    IReadOnlyList<GtfsDirectRouteDto> routes)
    {
        if (plan.DepartureLocation is null ||
            plan.Destination is null ||
            plan.TravelDate is null ||
            plan.DepartureTime is null)
        {
            throw new InvalidOperationException(
                "The travel plan is incomplete.");
        }

        if (routes.Count == 0)
        {
            return
                $"Nem találtam a {plan.DepartureTime.Value:HH:mm} utáni " +
                $"közvetlen buszjáratot {plan.DepartureLocation} és " +
                $"{plan.Destination} között. Átszállásos lehetőségek még elérhetők lehetnek.";
        }

        var result = new StringBuilder();

        result.AppendLine(
            $"A {plan.DepartureTime.Value:HH:mm} utáni közvetlen járatok:");

        result.AppendLine();

        foreach (var route in routes)
        {
            var routeName =
                route.RouteShortName ??
                route.RouteLongName ??
                route.TripId;

            result.AppendLine(
                $"- {routeName}: {route.DepartureTime} → {route.ArrivalTime}, " +
                $"{route.DurationMinutes} perc.");

            result.AppendLine(
                $"  {route.DepartureStop} → {route.ArrivalStop}");
        }

        result.AppendLine();

        result.AppendLine(
            $"Összesen {routes.Count} megfelelő közvetlen járatot találtam.");

        return result.ToString().Trim();
    }

    private static string NormalizeTravelIntent(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);

        var result = new string(
            normalized
                .Where(character =>
                    CharUnicodeInfo.GetUnicodeCategory(character) !=
                    UnicodeCategory.NonSpacingMark)
                .ToArray());

        return result.ToLowerInvariant();
    }

    private static DateTimeOffset GetBudapestNow()
    {
        var timeZoneId = OperatingSystem.IsWindows()
            ? "Central Europe Standard Time"
            : "Europe/Budapest";

        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);

        return TimeZoneInfo.ConvertTime(
            DateTimeOffset.UtcNow,
            timeZone);
    }

    private static string? ValidateMentionedLocation(
     string? candidate,
     string userText,
     bool isDestination)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        var normalizedCandidate = NormalizeForComparison(candidate);
        var normalizedText = NormalizeForComparison(userText);

        var suffixes = isDestination
            ? @"ba|be|ra|re|hoz|hez|ben|ban|on|en|ig"
            : @"rol|bol|tol|hoz|hez";

        var suffixPattern =
            $@"(?<!\p{{L}}){Regex.Escape(normalizedCandidate)}(?:{suffixes})(?!\p{{L}})";

        if (Regex.IsMatch(normalizedText, suffixPattern))
        {
            return candidate.Trim();
        }

        var explicitLabelPattern = isDestination
            ? $@"(?:celallomas|cel)\s*:?\s*{Regex.Escape(normalizedCandidate)}(?!\p{{L}})"
            : $@"(?:indulasi hely|indulasi helyem|honnan)\s*:?\s*{Regex.Escape(normalizedCandidate)}(?!\p{{L}})";

        return Regex.IsMatch(normalizedText, explicitLabelPattern)
            ? candidate.Trim()
            : null;
    }

    private static string NormalizeForComparison(string value)
    {
        var normalized = value
            .Normalize(System.Text.NormalizationForm.FormD);

        var builder = new System.Text.StringBuilder();

        foreach (var character in normalized)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(character)
                != System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString().Normalize(
            System.Text.NormalizationForm.FormC);
    }

    public string BuildTimetableResponse(
    TravelPlan plan,
    IReadOnlyList<GtfsDirectRouteDto> routes)
    {
        if (plan.DepartureLocation is null ||
            plan.Destination is null ||
            plan.TravelDate is null ||
            plan.ArrivalTime is null)
        {
            throw new InvalidOperationException(
                "The travel plan is incomplete.");
        }

        if (routes.Count == 0)
        {
            return
                $"Nem találtam olyan közvetlen buszjáratot, amely " +
                $"{plan.ArrivalTime.Value:HH:mm} előtt megérkezne " +
                $"{plan.Destination} településre. " +
                "Ez nem zárja ki az átszállásos lehetőségeket.";
        }

        var bestRoute = routes
            .OrderByDescending(x => ParseGtfsTimeSeconds(x.ArrivalTime))
            .First();

        var requestedArrivalSeconds =
            (int)plan.ArrivalTime.Value.ToTimeSpan().TotalSeconds;

        var actualArrivalSeconds =
            ParseGtfsTimeSeconds(bestRoute.ArrivalTime);

        var earlyMinutes = Math.Max(
            0,
            (requestedArrivalSeconds - actualArrivalSeconds) / 60);

        var routeName = bestRoute.RouteShortName
            ?? bestRoute.RouteLongName
            ?? bestRoute.TripId;

        var result = new StringBuilder();

        result.AppendLine(
            $"Találtam {routes.Count} megfelelő közvetlen buszjáratot.");

        result.AppendLine();

        result.AppendLine(
            $"A legkésőbb érkező megfelelő járat a {routeName}, " +
            $"amely {bestRoute.DepartureTime}-kor indul " +
            $"{bestRoute.DepartureStop} megállóból, " +
            $"és {bestRoute.ArrivalTime}-kor érkezik " +
            $"{bestRoute.ArrivalStop} megállóba.");

        result.AppendLine();

        result.AppendLine(
            $"Ez {earlyMinutes} perccel a kívánt " +
            $"{plan.ArrivalTime.Value:HH:mm}-s érkezés előtt van.");

        result.AppendLine();

        result.AppendLine("További megfelelő járatok:");

        foreach (var route in routes.Take(5))
        {
            var name = route.RouteShortName
                ?? route.RouteLongName
                ?? route.TripId;

            result.AppendLine(
                $"- {name}: {route.DepartureTime} → {route.ArrivalTime}, " +
                $"{route.DurationMinutes} perc.");
        }

        return result.ToString().Trim();
    }

    private static int ParseGtfsTimeSeconds(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new FormatException("GTFS time cannot be empty.");

        var parts = value.Split(':');

        if (parts.Length is not (2 or 3))
            throw new FormatException($"Invalid GTFS time: {value}");

        if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var hours) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minutes))
        {
            throw new FormatException($"Invalid GTFS time: {value}");
        }

        var seconds = 0;

        if (parts.Length == 3 &&
            !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out seconds))
        {
            throw new FormatException($"Invalid GTFS time: {value}");
        }

        if (hours < 0 || minutes is < 0 or > 59 || seconds is < 0 or > 59)
            throw new FormatException($"Invalid GTFS time: {value}");

        return checked(hours * 3600 + minutes * 60 + seconds);
    }

    private static DateOnly? ResolveTravelDate(string text)
    {
        var currentDate = GetCurrentBudapestDate();

        if (Regex.IsMatch(
            text,
            @"\bholnapután\b",
            RegexOptions.IgnoreCase))
        {
            return currentDate.AddDays(2);
        }

        if (Regex.IsMatch(
            text,
            @"\bholnap\b",
            RegexOptions.IgnoreCase))
        {
            return currentDate.AddDays(1);
        }

        if (Regex.IsMatch(
            text,
            @"\bma\b",
            RegexOptions.IgnoreCase))
        {
            return currentDate;
        }

        var isoMatch = Regex.Match(
            text,
            @"(?<!\d)\d{4}-\d{2}-\d{2}(?!\d)");

        if (isoMatch.Success &&
            DateOnly.TryParseExact(
                isoMatch.Value,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var isoDate))
        {
            return isoDate;
        }

        var numericDateMatch = Regex.Match(
            text,
            @"(?<!\d)(?<year>\d{4})\.\s*(?<month>\d{1,2})\.\s*(?<day>\d{1,2})\.?");

        if (numericDateMatch.Success &&
            int.TryParse(numericDateMatch.Groups["year"].Value, out var year) &&
            int.TryParse(numericDateMatch.Groups["month"].Value, out var month) &&
            int.TryParse(numericDateMatch.Groups["day"].Value, out var day))
        {
            try
            {
                return new DateOnly(year, month, day);
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }

        return null;
    }

    private static bool HasExplicitTime(string text)
    {
        return Regex.IsMatch(
            text,
            @"(?<!\d)\d{1,2}:\d{2}(?!\d)|(?<!\d)\d{1,2}\s*ór(?:a|akor|ára)(?!\w)",
            RegexOptions.IgnoreCase);
    }

    private static TimeOnly? ResolveArrivalTime(string text)
    {
        var normalizedText = NormalizeForComparison(text);

        var colonMatch = Regex.Match(
            normalizedText,
            @"(?<!\d)(?<hour>\d{1,2}):(?<minute>[0-5]\d)(?!\d)");

        if (colonMatch.Success &&
            int.TryParse(colonMatch.Groups["hour"].Value, out var colonHour) &&
            colonHour <= 23 &&
            int.TryParse(colonMatch.Groups["minute"].Value, out var minute))
        {
            return new TimeOnly(colonHour, minute);
        }

        var hourMatch = Regex.Match(
            normalizedText,
            @"(?<!\d)(?<hour>\d{1,2})\s*or(?:a|ara|akor)\b");

        if (!hourMatch.Success)
        {
            hourMatch = Regex.Match(
                normalizedText,
                @"(?<!\d)(?<hour>\d{1,2})\s*-\s*ra\b");
        }

        if (hourMatch.Success &&
            int.TryParse(hourMatch.Groups["hour"].Value, out var hour) &&
            hour <= 23)
        {
            return new TimeOnly(hour, 0);
        }

        return null;
    }

    public IReadOnlyList<string> GetMissingTravelInformation(
    TravelPlan plan)
    {
        var missing = new List<string>();

        if (string.IsNullOrWhiteSpace(plan.Destination))
            missing.Add("célállomás");

        if (string.IsNullOrWhiteSpace(plan.DepartureLocation))
            missing.Add("indulási hely");

        if (plan.TravelDate is null)
            missing.Add("utazás dátuma");

        if (plan.ArrivalTime is null)
            missing.Add("érkezési idő");

        if (plan.TransportMode == "unknown")
            missing.Add("közlekedési mód");

        return missing;
    }

    private static string ResolveTransportMode(string text)
    {
        if (text.Contains("bus", StringComparison.OrdinalIgnoreCase))
        {
            return "bus";
        }

        if (text.Contains("vonat", StringComparison.OrdinalIgnoreCase))
        {
            return "train";
        }

        if (text.Contains("autó", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("kocs", StringComparison.OrdinalIgnoreCase))
        {
            return "car";
        }

        if (text.Contains("gyalog", StringComparison.OrdinalIgnoreCase))
        {
            return "walking";
        }

        return "unknown";
    }

    private static DateOnly GetCurrentBudapestDate()
    {
        var localTime = TimeZoneInfo.ConvertTime(
            DateTimeOffset.UtcNow,
            BudapestTimeZone);

        return DateOnly.FromDateTime(localTime.DateTime);
    }

    private static TimeZoneInfo GetBudapestTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Budapest");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById(
                "Central Europe Standard Time");
        }
    }
}