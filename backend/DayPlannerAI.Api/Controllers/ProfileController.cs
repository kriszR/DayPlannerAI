
using DayPlannerAI.Api.Data;
using DayPlannerAI.Api.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DayPlannerAI.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProfileController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public ProfileController(ApplicationDbContext dbContext)
    {
        _context = dbContext;
    }

    [HttpGet]
    public async Task<IActionResult> GetProfile()
    {
        var profile = await _context.UserProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync();

        if (profile is null)
            return NotFound();

        return Ok(profile);
    }

    [HttpPut]
    public async Task<IActionResult> UpdateProfile(
    [FromBody] UpdateProfileRequest request)
    {
        var profile = await _context.UserProfiles
            .FirstOrDefaultAsync();

        if (profile == null)
        {
            return NotFound(new
            {
                Message = "A felhasználói profil nem található."
            });
        }

        profile.DisplayName = request.DisplayName.Trim();
        profile.HomeLocation = string.IsNullOrWhiteSpace(request.HomeLocation)
            ? null
            : request.HomeLocation.Trim();
        profile.TimeZone = request.TimeZone.Trim();
        if (request.PreparationMinutes.HasValue)
        {
            profile.PreparationMinutes = request.PreparationMinutes.Value;
        }

        await _context.SaveChangesAsync();

        return Ok(new
        {
            Message = "A profil sikeresen frissült.",
            Profile = profile
        });
    }
}
