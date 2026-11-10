using System.Net;
using System.Net.Http.Json;
using Bluestaq.Notes.Api.Auth;
using Bluestaq.Notes.Api.Contracts;

namespace Bluestaq.Notes.Api.Tests;

[Trait("Category", "Smoke")]
public sealed class EndpointSmokeTests(NotesApiFactory factory) : IClassFixture<NotesApiFactory>
{
    private static readonly Guid BobId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task AllEndpoints_CompleteAuthenticatedTeamNoteWorkflow()
    {
        using var anonymous = factory.CreateClient();
        using var invalid = CreateClient("invalid-api-key-value");
        using var alice = CreateClient("dev-alice-api-key");
        using var bob = CreateClient("dev-bob-api-key-01");

        var health = await anonymous.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal("healthy", (await health.Content.ReadFromJsonAsync<HealthResponse>())?.Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/teams")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await invalid.GetAsync("/api/teams")).StatusCode);

        var createTeam = await alice.PostAsJsonAsync(
            "/api/teams",
            new CreateTeamRequest { Name = "Smoke team" });
        Assert.Equal(HttpStatusCode.Created, createTeam.StatusCode);
        var team = await createTeam.Content.ReadFromJsonAsync<TeamResponse>();
        Assert.NotNull(team);

        var teams = await alice.GetFromJsonAsync<TeamResponse[]>("/api/teams");
        Assert.Contains(teams!, candidate => candidate.Id == team.Id);
        Assert.Equal(HttpStatusCode.OK, (await alice.GetAsync($"/api/teams/{team.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.GetAsync($"/api/teams/{team.Id}")).StatusCode);

        var addBob = await alice.PostAsJsonAsync(
            $"/api/teams/{team.Id}/members",
            new AddTeamMemberRequest { UserId = BobId });
        Assert.Equal(HttpStatusCode.Created, addBob.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await bob.GetAsync($"/api/teams/{team.Id}")).StatusCode);

        var createNote = await alice.PostAsJsonAsync(
            $"/api/teams/{team.Id}/notes",
            new CreateNoteRequest { Title = "Smoke note", Content = "Created by Alice." });
        Assert.Equal(HttpStatusCode.Created, createNote.StatusCode);
        var note = await createNote.Content.ReadFromJsonAsync<NoteResponse>();
        Assert.NotNull(note);

        var notes = await bob.GetFromJsonAsync<PagedNotesResponse>($"/api/teams/{team.Id}/notes");
        Assert.Equal(note.Id, Assert.Single(notes!.Items).Id);
        Assert.Equal(HttpStatusCode.OK, (await bob.GetAsync($"/api/notes/{note.Id}")).StatusCode);

        var updateNote = await bob.PutAsJsonAsync(
            $"/api/notes/{note.Id}",
            new UpdateNoteRequest { Title = "Updated smoke note", Content = "Updated by Bob." });
        Assert.Equal(HttpStatusCode.OK, updateNote.StatusCode);
        Assert.Equal("Updated smoke note", (await updateNote.Content.ReadFromJsonAsync<NoteResponse>())?.Title);

        Assert.Equal(HttpStatusCode.NoContent, (await alice.DeleteAsync($"/api/notes/{note.Id}")).StatusCode);
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await alice.DeleteAsync($"/api/teams/{team.Id}/members/{BobId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.GetAsync($"/api/teams/{team.Id}")).StatusCode);
    }

    private HttpClient CreateClient(string apiKey)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, apiKey);
        return client;
    }

    private sealed record HealthResponse(string Status);
}
