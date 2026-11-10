using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Bluestaq.Notes.Api;
using Bluestaq.Notes.Api.Administration;
using Bluestaq.Notes.Api.Auth;
using Bluestaq.Notes.Api.Data;
using Bluestaq.Notes.Api.Observability;
using Bluestaq.Notes.Api.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Notes")
    ?? throw new InvalidOperationException("ConnectionStrings:Notes is required.");
var hardening = builder.Configuration.GetSection(HardeningOptions.SectionName).Get<HardeningOptions>()
    ?? new HardeningOptions();

builder.WebHost.ConfigureKestrel(options =>
    options.Limits.MaxRequestBodySize = hardening.RequestBodyLimitBytes);

builder.Services.AddOptions<HardeningOptions>()
    .Bind(builder.Configuration.GetSection(HardeningOptions.SectionName))
    .Validate(options => options.IsValid(), "Hardening configuration is invalid.")
    .ValidateOnStart();
builder.Services.AddScoped<SecurityAuditTelemetryInterceptor>();
builder.Services.AddDbContext<NotesDbContext>((services, options) => options
    .UseSqlite(connectionString)
    .AddInterceptors(services.GetRequiredService<SecurityAuditTelemetryInterceptor>()));
builder.Services.AddAuthentication(ApiKeyAuthenticationDefaults.Scheme)
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
        ApiKeyAuthenticationDefaults.Scheme,
        _ => { });
builder.Services.AddAuthorization();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<SecurityAudit>();
builder.Services.AddSingleton<SecurityAuditIntegrity>();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var authenticatedUser = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var partitionKey = authenticatedUser is not null
            ? $"user:{authenticatedUser}"
            : $"ip:{context.Connection.RemoteIpAddress}";
        var permitLimit = authenticatedUser is not null
            ? hardening.AuthenticatedPermitLimit
            : hardening.AnonymousPermitLimit;
        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = TimeSpan.FromSeconds(hardening.RateLimitWindowSeconds),
            QueueLimit = 0,
            AutoReplenishment = true,
        });
    });
    options.OnRejected = async (context, cancellationToken) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter = Math.Ceiling(retryAfter.TotalSeconds).ToString();
        }

        await Results.Problem(
                detail: "The request rate limit was exceeded.",
                statusCode: StatusCodes.Status429TooManyRequests)
            .ExecuteAsync(context.HttpContext);
    };
});
var telemetry = builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddAspNetCoreInstrumentation());
if (hardening.EnableConsoleTracing)
{
    telemetry.WithTracing(tracing => tracing.AddConsoleExporter());
}
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddControllers().AddJsonOptions(options =>
    options.JsonSerializerOptions.Converters.Add(
        new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Bluestaq Notes API",
        Version = "v1",
        Description = "A small-team note service with managed credentials and auditable collaboration.",
    });
    options.IncludeXmlComments(typeof(Program).Assembly);
    options.AddSecurityDefinition(ApiKeyAuthenticationDefaults.Scheme, new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.ApiKey,
        Name = ApiKeyAuthenticationDefaults.HeaderName,
        In = ParameterLocation.Header,
        Description = "Enter an API key from the development fixtures or credential-management endpoints.",
    });
    options.OperationFilter<ApiKeySecurityOperationFilter>();
    options.OperationFilter<ProblemDetailsResponseOperationFilter>();
});

var app = builder.Build();

app.UseExceptionHandler();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<RequestBodyLimitMiddleware>();
app.UseStatusCodePages(async context =>
    await Results.Problem(statusCode: context.HttpContext.Response.StatusCode)
        .ExecuteAsync(context.HttpContext));
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Bluestaq Notes API v1"));
}

app.UseAuthentication();
app.UseRateLimiter();
app.UseMiddleware<LegacyApiDeprecationMiddleware>();
app.UseAuthorization();
app.MapControllers();

await DatabaseInitializer.InitializeAsync(app.Services, app.Configuration);

var commandExitCode = await AdministrativeCommandRunner.TryRunAsync(
    args, app.Services, Console.Out, Console.Error);
if (commandExitCode is not null)
{
    Environment.ExitCode = commandExitCode.Value;
    return;
}

app.Run();

/// <summary>Provides the application entry-point type used by integration-test hosting.</summary>
public partial class Program;
