using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Bluestaq.Notes.Api.Auth;
using Bluestaq.Notes.Api.Contracts;

namespace Bluestaq.Notes.Api.Tests;

[Trait("Category", "Integration")]
public sealed class OperationalHardeningTests(NotesApiFactory factory) : IClassFixture<NotesApiFactory>
{
    private static readonly Guid BobId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task VersionedRoute_PreservesBehaviorAndLegacyAdvertisesDeprecation()
    {
        using var client = CreateClient("dev-alice-api-key");
        const string correlationId = "hardening-test-correlation";
        client.DefaultRequestHeaders.Add("X-Correlation-ID", correlationId);

        var legacy = await client.GetAsync("/api/teams");
        var versioned = await client.GetAsync("/api/v1/teams");

        Assert.Equal(HttpStatusCode.OK, legacy.StatusCode);
        Assert.Equal(HttpStatusCode.OK, versioned.StatusCode);
        Assert.Equal("true", legacy.Headers.GetValues("Deprecation").Single());
        Assert.False(versioned.Headers.Contains("Deprecation"));
        Assert.Equal(correlationId, versioned.Headers.GetValues("X-Correlation-ID").Single());
    }

    [Fact]
    public async Task InvalidEnumAndOversizedBody_AreRejected()
    {
        using var client = CreateClient("dev-alice-api-key");
        var team = await CreateTeam(client);

        var invalidEnum = await client.PostAsJsonAsync(
            $"/api/teams/{team.Id}/notes",
            new { title = "Invalid", content = "No write", visibility = 999 });
        Assert.Equal(HttpStatusCode.BadRequest, invalidEnum.StatusCode);

        using var oversized = new StringContent(
            JsonSerializer.Serialize(new { title = "Large", content = new string('x', 20_000) }),
            Encoding.UTF8,
            "application/json");
        var oversizedResponse = await client.PostAsync($"/api/teams/{team.Id}/notes", oversized);
        Assert.Equal((HttpStatusCode)413, oversizedResponse.StatusCode);
    }

    [Fact]
    public async Task AuthenticatedRequests_AreRateLimitedByUserWithoutLeakingTheApiKey()
    {
        await using var rateFactory = new NotesApiFactory();
        using var client = rateFactory.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, "dev-alice-api-key");

        HttpResponseMessage? rejected = null;
        for (var request = 0; request <= 300; request++)
        {
            var response = await client.GetAsync("/api/teams");
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                rejected = response;
                break;
            }

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            response.Dispose();
        }

        Assert.NotNull(rejected);
        Assert.DoesNotContain("dev-alice-api-key", await rejected.Content.ReadAsStringAsync());
        rejected.Dispose();
    }

    [Fact]
    public async Task StaleNoteEtag_CannotDeleteOrChangePermissions()
    {
        using var client = CreateClient("dev-alice-api-key");
        var team = await CreateTeam(client);
        using var addMember = new HttpRequestMessage(HttpMethod.Post, $"/api/teams/{team.Id}/members")
        {
            Content = JsonContent.Create(new AddTeamMemberRequest { UserId = BobId }),
        };
        addMember.Headers.TryAddWithoutValidation("If-Match", $"\"{team.Version}\"");
        Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(addMember)).StatusCode);
        var create = await client.PostAsJsonAsync(
            $"/api/teams/{team.Id}/notes",
            new CreateNoteRequest { Title = "Versioned", Content = "one" });
        var note = await create.Content.ReadFromJsonAsync<NoteResponse>();
        Assert.NotNull(note);
        var stale = create.Headers.ETag!.Tag;

        using var update = new HttpRequestMessage(HttpMethod.Put, $"/api/notes/{note.Id}")
        {
            Content = JsonContent.Create(new UpdateNoteRequest { Title = "Versioned", Content = "two" }),
        };
        update.Headers.TryAddWithoutValidation("If-Match", stale);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(update)).StatusCode);

        using var permission = new HttpRequestMessage(
            HttpMethod.Put,
            $"/api/notes/{note.Id}/permissions/{BobId}")
        {
            Content = JsonContent.Create(new { role = "reader" }),
        };
        permission.Headers.TryAddWithoutValidation("If-Match", stale);
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await client.SendAsync(permission)).StatusCode);

        using var delete = new HttpRequestMessage(HttpMethod.Delete, $"/api/notes/{note.Id}");
        delete.Headers.TryAddWithoutValidation("If-Match", stale);
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await client.SendAsync(delete)).StatusCode);
    }

    [Fact]
    public async Task Owner_CanTransferTeamToCurrentMemberWithCurrentEtag()
    {
        using var alice = CreateClient("dev-alice-api-key");
        using var bob = CreateClient("dev-bob-api-key-01");
        var team = await CreateTeam(alice);
        using var addMember = new HttpRequestMessage(HttpMethod.Post, $"/api/teams/{team.Id}/members")
        {
            Content = JsonContent.Create(new AddTeamMemberRequest { UserId = BobId }),
        };
        addMember.Headers.TryAddWithoutValidation("If-Match", $"\"{team.Version}\"");
        Assert.Equal(HttpStatusCode.Created, (await alice.SendAsync(addMember)).StatusCode);
        using var current = await alice.GetAsync($"/api/teams/{team.Id}");
        using var transfer = new HttpRequestMessage(HttpMethod.Put, $"/api/teams/{team.Id}/owner")
        {
            Content = JsonContent.Create(new { userId = BobId }),
        };
        transfer.Headers.TryAddWithoutValidation("If-Match", current.Headers.ETag?.Tag);

        using var transferResponse = await alice.SendAsync(transfer);
        Assert.Equal(HttpStatusCode.OK, transferResponse.StatusCode);
        using var removePreviousOwner = new HttpRequestMessage(
            HttpMethod.Delete,
            $"/api/teams/{team.Id}/members/{team.OwnerId}");
        removePreviousOwner.Headers.TryAddWithoutValidation("If-Match", transferResponse.Headers.ETag?.Tag);
        Assert.Equal(HttpStatusCode.NoContent, (await bob.SendAsync(removePreviousOwner)).StatusCode);
    }

    [Fact]
    public async Task ConcurrentTeamWrites_WithOneEtagHaveOneWinner()
    {
        using var client = CreateClient("dev-alice-api-key");
        var team = await CreateTeam(client);
        using var first = new HttpRequestMessage(HttpMethod.Patch, $"/api/teams/{team.Id}")
        {
            Content = JsonContent.Create(new UpdateTeamRequest { Name = "First winner" }),
        };
        using var second = new HttpRequestMessage(HttpMethod.Patch, $"/api/teams/{team.Id}")
        {
            Content = JsonContent.Create(new UpdateTeamRequest { Name = "Second winner" }),
        };
        first.Headers.TryAddWithoutValidation("If-Match", $"\"{team.Version}\"");
        second.Headers.TryAddWithoutValidation("If-Match", $"\"{team.Version}\"");

        var responses = await Task.WhenAll(client.SendAsync(first), client.SendAsync(second));

        Assert.Equal(
            [HttpStatusCode.OK, HttpStatusCode.PreconditionFailed],
            responses.Select(response => response.StatusCode).Order().ToArray());
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    private HttpClient CreateClient(string apiKey)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, apiKey);
        return client;
    }

    private static async Task<TeamResponse> CreateTeam(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(
            "/api/teams",
            new CreateTeamRequest { Name = $"Hardened {Guid.NewGuid():N}" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TeamResponse>())!;
    }
}
