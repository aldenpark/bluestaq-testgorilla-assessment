using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;
using Bluestaq.Notes.Api.Auth;
using Bluestaq.Notes.Api.Contracts;
using Bluestaq.Notes.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Bluestaq.Notes.Api.Tests;

[Trait("Category", "Integration")]
public sealed class OperationalReadinessTests(NotesApiFactory factory) : IClassFixture<NotesApiFactory>
{
    [Fact]
    public async Task HealthEndpoints_SeparateLivenessAndDatabaseReadiness()
    {
        using var client = factory.CreateClient();
        foreach (var path in new[] { "/health", "/health/live", "/health/ready" })
        {
            var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("healthy", (await response.Content.ReadFromJsonAsync<HealthResponse>())?.Status);
        }
    }

    [Fact]
    public async Task SecurityMutation_EmitsPostCommitMetric()
    {
        long measurements = 0;
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == "Bluestaq.Notes.Api.Security")
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) => measurements += value);
        listener.Start();

        using var alice = factory.CreateClient();
        alice.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, "dev-alice-api-key");
        var response = await alice.PostAsJsonAsync(
            "/api/teams",
            new CreateTeamRequest { Name = $"Metric {Guid.NewGuid():N}" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(measurements > 0);
    }

    [Fact]
    public async Task DatabaseHealthCheck_UnavailableDatabaseIsUnhealthy()
    {
        var missingDirectory = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}");
        var services = new ServiceCollection();
        services.AddDbContext<NotesDbContext>(options => options.UseSqlite(
            $"Data Source={Path.Combine(missingDirectory, "notes.db")};Mode=ReadOnly"));
        await using var provider = services.BuildServiceProvider();
        var healthCheck = new DatabaseHealthCheck(
            provider.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    private sealed record HealthResponse(string Status);
}
