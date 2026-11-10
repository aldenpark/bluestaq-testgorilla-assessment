using System.Net;
using System.Net.Http.Json;
using Bluestaq.Notes.Api.Auth;
using Bluestaq.Notes.Api.Contracts;

namespace Bluestaq.Notes.Api.Tests;

[Trait("Category", "Integration")]
public sealed class SecurityIntegrationTests(NotesApiFactory factory) : IClassFixture<NotesApiFactory>
{
    private static readonly Guid AliceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid BobId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task InvalidApiKey_ReturnsUnauthorizedProblemDetails()
    {
        using var client = CreateClient("unknown-api-key-value");

        var response = await client.GetAsync("/api/teams");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task NonOwner_CannotAddOrRemoveTeamMembers()
    {
        using var alice = CreateClient("dev-alice-api-key");
        using var bob = CreateClient("dev-bob-api-key-01");
        var team = await CreateTeam(alice);
        Assert.Equal(HttpStatusCode.Created, (await AddBob(alice, team.Id)).StatusCode);

        var addResponse = await bob.PostAsJsonAsync(
            $"/api/teams/{team.Id}/members",
            new AddTeamMemberRequest { UserId = AliceId });
        var removeResponse = await bob.DeleteAsync($"/api/teams/{team.Id}/members/{AliceId}");

        Assert.Equal(HttpStatusCode.Forbidden, addResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, removeResponse.StatusCode);
    }

    [Fact]
    public async Task RemovingMember_ImmediatelyRevokesExistingNoteAccess()
    {
        using var alice = CreateClient("dev-alice-api-key");
        using var bob = CreateClient("dev-bob-api-key-01");
        var team = await CreateTeam(alice);
        Assert.Equal(HttpStatusCode.Created, (await AddBob(alice, team.Id)).StatusCode);

        var createNote = await alice.PostAsJsonAsync(
            $"/api/teams/{team.Id}/notes",
            new CreateNoteRequest { Title = "Security boundary", Content = "Team members only." });
        var note = await createNote.Content.ReadFromJsonAsync<NoteResponse>();
        Assert.NotNull(note);
        Assert.Equal(HttpStatusCode.OK, (await bob.GetAsync($"/api/notes/{note.Id}")).StatusCode);

        var removeResponse = await alice.DeleteAsync($"/api/teams/{team.Id}/members/{BobId}");

        Assert.Equal(HttpStatusCode.NoContent, removeResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.GetAsync($"/api/notes/{note.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.GetAsync($"/api/teams/{team.Id}/notes")).StatusCode);
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
            new CreateTeamRequest { Name = $"Security team {Guid.NewGuid():N}" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TeamResponse>())!;
    }

    private static Task<HttpResponseMessage> AddBob(HttpClient client, Guid teamId) =>
        client.PostAsJsonAsync(
            $"/api/teams/{teamId}/members",
            new AddTeamMemberRequest { UserId = BobId });
}
