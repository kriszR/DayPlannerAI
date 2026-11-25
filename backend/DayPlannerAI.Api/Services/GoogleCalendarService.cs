using System.Globalization;

using DayPlannerAI.Api.Configuration;
using DayPlannerAI.Api.Data;
using DayPlannerAI.Api.DTOs;

using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Services;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DayPlannerAI.Api.Services;

public class GoogleCalendarService
{
    private const string UserId = "dayplannerai-single-user";
    private const int MaxResultsPerPage = 2500;

    private readonly ApplicationDbContext _context;
    private readonly GoogleCalendarOptions _options;
    private readonly IDataProtector _protector;

    public GoogleCalendarService(
        ApplicationDbContext context,
        IOptions<GoogleCalendarOptions> options,
        IDataProtectionProvider dataProtectionProvider)
    {
        _context = context;
        _options = options.Value;

        _protector = dataProtectionProvider.CreateProtector(
            "DayPlannerAI.GoogleCalendar.RefreshToken.v1");
    }

    private GoogleAuthorizationCodeFlow CreateFlow()
    {
        return new GoogleAuthorizationCodeFlow(
            new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = new ClientSecrets
                {
                    ClientId = _options.ClientId,
                    ClientSecret = _options.ClientSecret
                },
                Scopes = new[]
                {
                    "https://www.googleapis.com/auth/calendar.readonly"
                }
            });
    }

    public async Task<IReadOnlyList<CalendarEventDto>>
        GetUpcomingEventsAsync(
            int days,
            CancellationToken cancellationToken = default)
    {
        var connection = await _context.GoogleCalendarConnections
            .FirstOrDefaultAsync(
                x => x.Id == 1,
                cancellationToken);

        if (connection == null)
        {
            throw new InvalidOperationException(
                "A Google-naptár nincs csatlakoztatva.");
        }

        var refreshToken = _protector.Unprotect(
            connection.ProtectedRefreshToken);

        using var flow = CreateFlow();

        var token = new TokenResponse
        {
            RefreshToken = refreshToken
        };

        var credential = new UserCredential(
            flow,
            UserId,
            token);

        await credential.GetAccessTokenForRequestAsync(
            cancellationToken: cancellationToken);

        using var calendarService = new CalendarService(
            new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "DayPlannerAI"
            });

        // Use UTC instants for the API bounds. Google returns each event's
        // timezone-aware start/end values, which are preserved in the DTO.
        var now = DateTimeOffset.UtcNow;
        var timeMax = now.AddDays(days);

        var request = calendarService.Events.List("primary");
        request.TimeMinDateTimeOffset = now;
        request.TimeMaxDateTimeOffset = timeMax;
        request.SingleEvents = true;
        request.OrderBy = EventsResource.ListRequest.OrderByEnum.StartTime;
        request.MaxResults = MaxResultsPerPage;

        var events = new List<CalendarEventDto>();
        Events? response;

        do
        {
            response = await request.ExecuteAsync(cancellationToken);

            if (response.Items != null)
            {
                events.AddRange(response.Items.Select(item => new CalendarEventDto
                {
                    Id = item.Id,
                    Summary = item.Summary,
                    Description = item.Description,
                    Location = item.Location,

                    Start = item.Start?.DateTimeDateTimeOffset?.ToString(
                        "O",
                        CultureInfo.InvariantCulture)
                        ?? item.Start?.Date,

                    End = item.End?.DateTimeDateTimeOffset?.ToString(
                        "O",
                        CultureInfo.InvariantCulture)
                        ?? item.End?.Date,

                    IsAllDay = item.Start?.DateTimeDateTimeOffset == null
                        && item.Start?.Date != null,

                    HtmlLink = item.HtmlLink
                }));
            }

            request.PageToken = response.NextPageToken;
        }
        while (!string.IsNullOrWhiteSpace(response.NextPageToken));

        return events;
    }
}
