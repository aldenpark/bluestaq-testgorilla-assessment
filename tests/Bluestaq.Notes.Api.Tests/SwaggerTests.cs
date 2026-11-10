using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace Bluestaq.Notes.Api.Tests;

[Trait("Category", "Integration")]
public sealed class SwaggerTests(NotesApiFactory factory) : IClassFixture<NotesApiFactory>
{
    [Fact]
    public async Task Swagger_ExposesUiDocumentAndApiKeySecurity()
    {
        using var client = factory.CreateClient();

        var uiResponse = await client.GetAsync("/swagger/index.html");
        Assert.Equal(HttpStatusCode.OK, uiResponse.StatusCode);
        Assert.Contains("Swagger UI", await uiResponse.Content.ReadAsStringAsync());

        var documentResponse = await client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, documentResponse.StatusCode);
        using var document = JsonDocument.Parse(await documentResponse.Content.ReadAsStringAsync());
        var root = document.RootElement;

        var apiKeyScheme = root
            .GetProperty("components")
            .GetProperty("securitySchemes")
            .GetProperty("ApiKey");
        Assert.Equal("apiKey", apiKeyScheme.GetProperty("type").GetString());
        Assert.Equal("X-Api-Key", apiKeyScheme.GetProperty("name").GetString());
        Assert.Equal("header", apiKeyScheme.GetProperty("in").GetString());

        var paths = root.GetProperty("paths");
        var createTeam = paths.GetProperty("/api/teams").GetProperty("post");
        Assert.True(paths.TryGetProperty("/api/v1/teams", out _));
        Assert.Equal(
            "Creates a team owned by the authenticated user and adds that user as its first member.",
            createTeam.GetProperty("summary").GetString());
        Assert.True(createTeam.GetProperty("security")[0].TryGetProperty("ApiKey", out _));
        Assert.False(paths.GetProperty("/health").GetProperty("get").TryGetProperty("security", out _));
        var updateNote = paths.GetProperty("/api/notes/{noteId}").GetProperty("put");
        Assert.True(updateNote.GetProperty("responses").TryGetProperty("412", out _));
        Assert.True(updateNote.GetProperty("responses").TryGetProperty("428", out _));
        Assert.True(updateNote.GetProperty("responses").TryGetProperty("413", out _));
        Assert.True(updateNote.GetProperty("responses").TryGetProperty("429", out _));
    }

    [Fact]
    public async Task Swagger_IsDisabledOutsideDevelopment()
    {
        using var productionFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Hardening:AuditIntegrityKey"] =
                        "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=",
                }));
        });
        using var client = productionFactory.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/swagger/index.html")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/swagger/v1/swagger.json")).StatusCode);
    }
}
