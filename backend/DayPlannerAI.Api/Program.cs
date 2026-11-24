using DayPlannerAI.Api.Data;
using Microsoft.EntityFrameworkCore;
using DayPlannerAI.Api.Configuration;
using DayPlannerAI.Api.Services;
using Microsoft.Extensions.Options;
using DayPlannerAI.Api.Services.Tools;
using DayPlannerAI.Api.Services.Agent;
using DayPlannerAI.Api.Services.Agent.Skills;

var builder = WebApplication.CreateBuilder(args);


builder.Services.AddControllers();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddMemoryCache();
builder.Services.AddDataProtection();

// Add Scoped Services
builder.Services.AddScoped<ConversationManager>();

builder.Services.AddSingleton<ConversationStore>();

builder.Services.AddScoped<GtfsImportService>();

builder.Services.AddScoped<GtfsQueryService>();

builder.Services.AddScoped<GtfsRouteSearchService>();

builder.Services.AddScoped<GoogleCalendarService>();

builder.Services.AddScoped<WeatherService>();

builder.Services.AddScoped<GtfsRealtimeService>();

builder.Services.AddScoped<GetTripRealtimeTool>();

builder.Services.AddScoped<IAgentSkill, LiveTransitSkill>();
builder.Services.AddScoped<SearchBusJourneysTool>();
builder.Services.AddScoped<IAgentSkill, TravelPlanningSkill>();

builder.Services.AddScoped<AgentSkillRegistry>();



builder.Services.AddScoped<GetCalendarEventsTool>();
builder.Services.AddScoped<GetWeatherForecastTool>();

builder.Services.AddScoped<IAgentSkill, DailyPlanningSkill>();

builder.Services.AddScoped<GetUserProfileTool>();
// Http clients

builder.Services.AddHttpClient();

builder.Services.AddHttpClient<InternalApiClient>(client =>
{
    client.BaseAddress = new Uri(
        builder.Configuration["Agent:InternalApiBaseUrl"]
        ?? "http://localhost:5131");

    client.Timeout = TimeSpan.FromSeconds(20);
});

builder.Services.AddHttpClient<OllamaChatService>((serviceProvider, client) =>
{
    var options = serviceProvider
        .GetRequiredService<IOptions<OllamaOptions>>()
        .Value;

    client.BaseAddress = new Uri(options.BaseUrl);
    client.Timeout = TimeSpan.FromMinutes(3);
});

builder.Services.AddHttpClient<MavPluszRealtimeClient>(client =>
{
    client.BaseAddress = new Uri(
        "https://mavplusz.hu/otp2-backend/otp/routers/default/index/graphql");

    client.Timeout = TimeSpan.FromSeconds(10);

    client.DefaultRequestHeaders.UserAgent.ParseAdd(
        "DayPlannerAI/1.0");
});

builder.Services.AddHttpClient<OllamaAgentService>();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidCastException("Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy.WithOrigins("http://localhost:5173")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services
    .AddOptions<GoogleCalendarOptions>()
    .Bind(builder.Configuration.GetSection(
        GoogleCalendarOptions.SectionName))
    .Validate(
        options =>
            !string.IsNullOrWhiteSpace(options.ClientId) &&
            !string.IsNullOrWhiteSpace(options.ClientSecret) &&
            !string.IsNullOrWhiteSpace(options.RedirectUri),
        "A Google Calendar OAuth konfiguráció hiányos.")
    .ValidateOnStart();

builder.Services.Configure<OllamaOptions>(
    builder.Configuration.GetSection("Ollama"));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("Frontend");

app.MapControllers();

app.Run();
