using System.Text.Json;
using DayPlannerAI.Api.Services.Tools;
using Microsoft.AspNetCore.Mvc;

namespace DayPlannerAI.Api.Controllers;

[ApiController]
[Route("api/travel")]
public sealed class BusSearchController : ControllerBase
{
    private readonly SearchBusJourneysTool _searchTool;

    public BusSearchController(SearchBusJourneysTool searchTool)
    {
        _searchTool = searchTool;
    }

    [HttpPost("search")]
    public async Task<IActionResult> Search(
        [FromBody] JsonElement arguments,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _searchTool.ExecuteAsync(
                arguments,
                cancellationToken);

            return Content(result, "application/json");
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}