using System.Globalization;
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
        var text = string.Join(
            " ",
            messages
                .Where(message => message.Role == "user")
                .Select(message => message.Content))
            .ToLowerInvariant();

        return text.Contains("bus") ||
               text.Contains("menetrend") ||
               text.Contains("utaz") ||
               text.Contains("eljut") ||
               text.Contains("vonat") ||
               text.Contains("indulás");
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
            allUserText,
            isDestination: false);

        var extractedDestination = ValidateMentionedLocation(
            extraction.Destination,
            allUserText,
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