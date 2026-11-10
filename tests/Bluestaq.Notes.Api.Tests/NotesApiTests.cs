using System.Net;
using System.Net.Http.Json;
using Bluestaq.Notes.Api.Auth;
using Bluestaq.Notes.Api.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Bluestaq.Notes.Api.Tests;

public sealed class NotesApiTests(NotesApiFactory factory) : IClassFixture<NotesApiFactory>
{
    private static readonly Guid BobId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task ProtectedEndpoint_WithoutApiKey_ReturnsUnauthorized()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/teams");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task NoteLifecycle_CreatesReadsUpdatesAndDeletes()
    {
        using var client = CreateClient("dev-alice-api-key");
        var team = await CreateTeam(client);

        var createResponse = await client.PostAsJsonAsync(
            $"/api/teams/{team.Id}/notes",
            new CreateNoteRequest { Title = "Decision", Content = "Use SQLite." });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<NoteResponse>();
        Assert.NotNull(created);

        var getResponse = await client.GetAsync($"/api/notes/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.Equal("Decision", (await getResponse.Content.ReadFromJsonAsync<NoteResponse>())?.Title);

        var updateResponse = await client.PutAsJsonAsync(
            $"/api/notes/{created.Id}",
            new UpdateNoteRequest { Title = "Updated decision", Content = "Use SQLite locally." });
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await updateResponse.Content.ReadFromJsonAsync<NoteResponse>();
        Assert.Equal("Updated decision", updated?.Title);
        Assert.True(updated?.UpdatedAt >= created.UpdatedAt);

        var list = await client.GetFromJsonAsync<PagedNotesResponse>($"/api/teams/{team.Id}/notes");
        Assert.NotNull(list);
        Assert.Equal(1, list.TotalCount);
        Assert.Equal(created.Id, Assert.Single(list.Items).Id);

        var deleteResponse = await client.DeleteAsync($"/api/notes/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/notes/{created.Id}")).StatusCode);
    }

    [Fact]
    public async Task Note_PersistsAcrossDbContexts()
    {
        using var client = CreateClient("dev-alice-api-key");
        var team = await CreateTeam(client);
        var response = await client.PostAsJsonAsync(
            $"/api/teams/{team.Id}/notes",
            new CreateNoteRequest { Title = "Persisted", Content = "Stored on disk." });
        var note = await response.Content.ReadFromJsonAsync<NoteResponse>();
        Assert.NotNull(note);

        await using var freshContext = factory.CreateDbContext();
        var persisted = await freshContext.Notes.AsNoTracking().SingleAsync(candidate => candidate.Id == note.Id);

        Assert.Equal("Persisted", persisted.Title);
        Assert.Equal("Stored on disk.", persisted.Content);
    }

    [Theory]
    [InlineData("", "content")]
    [InlineData("   ", "content")]
    public async Task CreateNote_WithInvalidTitle_ReturnsBadRequest(string title, string content)
    {
        using var client = CreateClient("dev-alice-api-key");
        var team = await CreateTeam(client);

        var response = await client.PostAsJsonAsync(
            $"/api/teams/{team.Id}/notes",
            new { title, content });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateNote_OverContentLimit_ReturnsBadRequest()
    {
        using var client = CreateClient("dev-alice-api-key");
        var team = await CreateTeam(client);

        var response = await client.PostAsJsonAsync(
            $"/api/teams/{team.Id}/notes",
            new CreateNoteRequest { Title = "Too long", Content = new string('x', 10_001) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task MissingResources_ReturnNotFound()
    {
        using var client = CreateClient("dev-alice-api-key");

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/notes/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/teams/{Guid.NewGuid()}/notes")).StatusCode);
    }

    [Fact]
    public async Task NonMember_CannotAccessAnotherTeamsNotes()
    {
        using var alice = CreateClient("dev-alice-api-key");
        using var bob = CreateClient("dev-bob-api-key-01");
        var team = await CreateTeam(alice);
        var createResponse = await alice.PostAsJsonAsync(
            $"/api/teams/{team.Id}/notes",
            new CreateNoteRequest { Title = "Private", Content = "Team only." });
        var note = await createResponse.Content.ReadFromJsonAsync<NoteResponse>();
        Assert.NotNull(note);

        Assert.Equal(HttpStatusCode.Forbidden, (await bob.GetAsync($"/api/notes/{note.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.GetAsync($"/api/teams/{team.Id}/notes")).StatusCode);
    }

    [Fact]
    public async Task Membership_RequiresOwnerAndControlsAccess()
    {
        using var alice = CreateClient("dev-alice-api-key");
        using var bob = CreateClient("dev-bob-api-key-01");
        var team = await CreateTeam(alice);

        var addResponse = await alice.PostAsJsonAsync(
            $"/api/teams/{team.Id}/members",
            new AddTeamMemberRequest { UserId = BobId });
        Assert.Equal(HttpStatusCode.Created, addResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await bob.GetAsync($"/api/teams/{team.Id}")).StatusCode);

        var bobManageResponse = await bob.PostAsJsonAsync(
            $"/api/teams/{team.Id}/members",
            new AddTeamMemberRequest { UserId = team.OwnerId });
        Assert.Equal(HttpStatusCode.Forbidden, bobManageResponse.StatusCode);

        Assert.Equal(
            HttpStatusCode.Conflict,
            (await alice.DeleteAsync($"/api/teams/{team.Id}/members/{team.OwnerId}")).StatusCode);
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await alice.DeleteAsync($"/api/teams/{team.Id}/members/{BobId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.GetAsync($"/api/teams/{team.Id}")).StatusCode);
    }

    [Fact]
    public async Task PaginationOutsideLimits_ReturnsBadRequest()
    {
        using var client = CreateClient("dev-alice-api-key");
        var team = await CreateTeam(client);

        var response = await client.GetAsync($"/api/teams/{team.Id}/notes?page=0&pageSize=101");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
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
            new CreateTeamRequest { Name = $"Team {Guid.NewGuid():N}" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TeamResponse>())!;
    }
}
