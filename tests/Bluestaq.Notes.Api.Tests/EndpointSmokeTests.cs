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

        var addBob = await alice.PostTeamAsync(
            team.Id,
            "/members",
            new AddTeamMemberRequest { UserId = BobId });
        Assert.Equal(HttpStatusCode.Created, addBob.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await bob.GetAsync($"/api/teams/{team.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await alice.GetAsync("/api/v1/teams")).StatusCode);

        Assert.Equal(
            HttpStatusCode.OK,
            (await alice.PutTeamAsync(
                team.Id,
                "/owner",
                new TransferTeamOwnershipRequest { UserId = BobId })).StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await bob.PutTeamAsync(
                team.Id,
                "/owner",
                new TransferTeamOwnershipRequest { UserId = team.OwnerId })).StatusCode);

        var renameTeam = await alice.PatchTeamAsync(
            team.Id,
            new UpdateTeamRequest { Name = "Renamed smoke team" });
        Assert.Equal(HttpStatusCode.OK, renameTeam.StatusCode);

        var invite = await alice.PostTeamAsync(
            team.Id,
            "/invitations",
            new InviteTeamMemberRequest { DisplayName = "Charlie", KeyName = "Initial" });
        Assert.Equal(HttpStatusCode.Created, invite.StatusCode);
        var invitation = await invite.Content.ReadFromJsonAsync<InvitationResponse>();
        Assert.NotNull(invitation);
        using var charlie = CreateClient(invitation.Credential.ApiKey);
        Assert.Equal(HttpStatusCode.OK, (await charlie.GetAsync($"/api/teams/{team.Id}")).StatusCode);

        var createNote = await alice.PostAsJsonAsync(
            $"/api/teams/{team.Id}/notes",
            new CreateNoteRequest { Title = "Smoke note", Content = "Created by Alice." });
        Assert.Equal(HttpStatusCode.Created, createNote.StatusCode);
        var note = await createNote.Content.ReadFromJsonAsync<NoteResponse>();
        Assert.NotNull(note);

        var notes = await bob.GetFromJsonAsync<PagedNotesResponse>($"/api/teams/{team.Id}/notes");
        Assert.Equal(note.Id, Assert.Single(notes!.Items).Id);
        Assert.Equal(HttpStatusCode.OK, (await bob.GetAsync($"/api/notes/{note.Id}")).StatusCode);

        var grantEditor = await alice.PutNoteAsync(
            note.Id,
            $"/permissions/{BobId}",
            new SetNotePermissionRequest { Role = Bluestaq.Notes.Api.Data.NotePermissionRole.Editor });
        Assert.Equal(HttpStatusCode.OK, grantEditor.StatusCode);
        var listPermissions = await alice.GetAsync($"/api/notes/{note.Id}/permissions");
        Assert.Equal(HttpStatusCode.OK, listPermissions.StatusCode);

        using var currentNote = await bob.GetAsync($"/api/notes/{note.Id}");
        var updateRequest = new HttpRequestMessage(HttpMethod.Put, $"/api/notes/{note.Id}")
        {
            Content = JsonContent.Create(
                new UpdateNoteRequest { Title = "Updated smoke note", Content = "Updated by Bob." }),
        };
        updateRequest.Headers.TryAddWithoutValidation("If-Match", currentNote.Headers.ETag?.Tag);
        var updateNote = await bob.SendAsync(updateRequest);
        Assert.Equal(HttpStatusCode.OK, updateNote.StatusCode);
        Assert.Equal("Updated smoke note", (await updateNote.Content.ReadFromJsonAsync<NoteResponse>())?.Title);

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await alice.DeleteNoteAsync(note.Id, $"/permissions/{BobId}")).StatusCode);

        var createKey = await alice.PostAsJsonAsync(
            "/api/api-keys",
            new CreateApiKeyRequest { Name = "Smoke secondary" });
        Assert.Equal(HttpStatusCode.Created, createKey.StatusCode);
        var createdKey = await createKey.Content.ReadFromJsonAsync<ApiKeyCreatedResponse>();
        Assert.NotNull(createdKey);
        Assert.Contains((await alice.GetFromJsonAsync<ApiKeyResponse[]>("/api/api-keys"))!,
            key => key.Id == createdKey.Id);
        var rotateKey = await alice.PostAsync($"/api/api-keys/{createdKey.Id}/rotate", null);
        Assert.Equal(HttpStatusCode.Created, rotateKey.StatusCode);
        var rotatedKey = await rotateKey.Content.ReadFromJsonAsync<ApiKeyCreatedResponse>();
        Assert.NotNull(rotatedKey);
        using var rotated = CreateClient(rotatedKey.ApiKey);
        Assert.Equal(HttpStatusCode.OK, (await rotated.GetAsync("/api/teams")).StatusCode);
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await alice.DeleteAsync($"/api/api-keys/{rotatedKey.Id}")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await alice.DeleteNoteAsync(note.Id)).StatusCode);
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await alice.DeleteTeamAsync(team.Id, $"/members/{BobId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.GetAsync($"/api/teams/{team.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await alice.GetAsync("/api/security-events")).StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await alice.GetAsync($"/api/teams/{team.Id}/security-events")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await alice.DeleteTeamAsync(team.Id)).StatusCode);
    }

    private HttpClient CreateClient(string apiKey)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, apiKey);
        return client;
    }

    private sealed record HealthResponse(string Status);
}
