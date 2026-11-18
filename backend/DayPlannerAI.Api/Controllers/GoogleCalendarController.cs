
using System.Security.Cryptography;

using DayPlannerAI.Api.Configuration;
using DayPlannerAI.Api.Data;
using DayPlannerAI.Api.Models;

using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Requests;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace DayPlannerAI.Api.Controllers;

[ApiController]
[Route("api/google-calendar")]
public class GoogleCalendarController : ControllerBase
{
    private const string UserId = "dayplannerai-single-user";

    private const string CalendarReadOnlyScope =
        "https://www.googleapis.com/auth/calendar.readonly";

    private readonly ApplicationDbContext _context;
    private readonly GoogleCalendarOptions _options;
    private readonly IMemoryCache _cache;
    private readonly IDataProtector _protector;

    public GoogleCalendarController(
        ApplicationDbContext context,
        IOptions<GoogleCalendarOptions> options,
        IMemoryCache cache,
        IDataProtectionProvider dataProtectionProvider)
    {
        _context = context;
        _options = options.Value;
        _cache = cache;

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
                Scopes = new[] { CalendarReadOnlyScope },
                Prompt = "consent"
            });
    }

    [HttpGet("connect")]
    public IActionResult Connect()
    {
        var state = WebEncoders.Base64UrlEncode(
            RandomNumberGenerator.GetBytes(32));

        _cache.Set(
            $"GoogleCalendarOAuthState:{state}",
            true,
            TimeSpan.FromMinutes(10));

        using var flow = CreateFlow();

        var request = (GoogleAuthorizationCodeRequestUrl)
            flow.CreateAuthorizationCodeRequest(_options.RedirectUri);

        request.State = state;
        request.AccessType = "offline";

        return Redirect(request.Build().ToString());
    }

    [HttpGet("callback")]
    public async Task<IActionResult> Callback(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(error))
        {
            return BadRequest(new
            {
                Message = "A Google-naptár engedélyezése sikertelen.",
                Error = error
            });
        }

        if (string.IsNullOrWhiteSpace(code) ||
            string.IsNullOrWhiteSpace(state))
        {
            return BadRequest(new
            {
                Message = "Hiányzó OAuth paraméter."
            });
        }

        var cacheKey = $"GoogleCalendarOAuthState:{state}";

        if (!_cache.TryGetValue(cacheKey, out bool validState) ||
            !validState)
        {
            return BadRequest(new
            {
                Message = "Érvénytelen vagy lejárt OAuth state."
            });
        }

        _cache.Remove(cacheKey);

        using var flow = CreateFlow();

        var token = await flow.ExchangeCodeForTokenAsync(
            UserId,
            code,
            _options.RedirectUri,
            cancellationToken);

        var connection = await _context.GoogleCalendarConnections
            .FirstOrDefaultAsync(
                x => x.Id == 1,
                cancellationToken);

        string? refreshToken = token.RefreshToken;

        if (string.IsNullOrWhiteSpace(refreshToken) &&
            connection != null)
        {
            refreshToken = _protector.Unprotect(
                connection.ProtectedRefreshToken);
        }

        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return BadRequest(new
            {
                Message =
                    "A Google nem adott refresh tokent. " +
                    "Próbáld meg újra az engedélyezést."
            });
        }

        var protectedToken = _protector.Protect(refreshToken);

        if (connection == null)
        {
            connection = new GoogleCalendarConnection
            {
                Id = 1,
                ProtectedRefreshToken = protectedToken,
                ConnectedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.GoogleCalendarConnections.Add(connection);
        }
        else
        {
            connection.ProtectedRefreshToken = protectedToken;
            connection.UpdatedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync(cancellationToken);

        return Content(
            "A DayPlannerAI sikeresen csatlakozott a Google-naptáradhoz. " +
            "Ezt az ablakot bezárhatod.");
    }
}
