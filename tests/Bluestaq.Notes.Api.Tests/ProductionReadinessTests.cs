using System.Net;
using System.Net.Http.Json;
using Bluestaq.Notes.Api.Auth;
using Bluestaq.Notes.Api.Contracts;
using Bluestaq.Notes.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Bluestaq.Notes.Api.Tests;

[Trait("Category", "Integration")]
public sealed class ProductionReadinessTests(NotesApiFactory factory) : IClassFixture<NotesApiFactory>
{
    private static readonly Guid BobId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task RestrictedNote_RequiresOwnerCreatorOrExplicitRole()
    {
        using var alice = CreateClient("dev-alice-api-key");
        using var bob = CreateClient("dev-bob-api-key-01");
        var team = await CreateTeam(alice);
        Assert.Equal(
            HttpStatusCode.Created,
            (await alice.PostAsJsonAsync(
                $"/api/teams/{team.Id}/members",
                new AddTeamMemberRequest { UserId = BobId })).StatusCode);

        var create = await alice.PostAsJsonAsync(
            $"/api/teams/{team.Id}/notes",
            new CreateNoteRequest
            {
                Title = "Restricted",
                Content = "Explicit access only.",
                Visibility = NoteVisibility.Restricted,
            });
        var note = await create.Content.ReadFromJsonAsync<NoteResponse>();
        Assert.NotNull(note);

        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/notes/{note.Id}")).StatusCode);
        Assert.Empty((await bob.GetFromJsonAsync<PagedNotesResponse>($"/api/teams/{team.Id}/notes"))!.Items);

        await alice.PutAsJsonAsync(
            $"/api/notes/{note.Id}/permissions/{BobId}",
            new SetNotePermissionRequest { Role = NotePermissionRole.Reader });
        Assert.Equal(HttpStatusCode.OK, (await bob.GetAsync($"/api/notes/{note.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await UpdateNote(bob, note.Id, "\"1\"")).StatusCode);

        await alice.PutAsJsonAsync(
            $"/api/notes/{note.Id}/permissions/{BobId}",
            new SetNotePermissionRequest { Role = NotePermissionRole.Editor });
        Assert.Equal(HttpStatusCode.OK, (await UpdateNote(bob, note.Id, "\"1\"")).StatusCode);
    }

    [Fact]
    public async Task NoteUpdate_RequiresCurrentEtagAndRejectsStaleWrites()
    {
        using var alice = CreateClient("dev-alice-api-key");
        var team = await CreateTeam(alice);
        var create = await alice.PostAsJsonAsync(
            $"/api/teams/{team.Id}/notes",
            new CreateNoteRequest { Title = "Versioned", Content = "v1" });
        var note = await create.Content.ReadFromJsonAsync<NoteResponse>();
        Assert.NotNull(note);
        Assert.Equal("\"1\"", create.Headers.ETag?.Tag);

        var missing = await alice.PutAsJsonAsync(
            $"/api/notes/{note.Id}",
            new UpdateNoteRequest { Title = "No precondition", Content = "ignored" });
        Assert.Equal((HttpStatusCode)428, missing.StatusCode);

        var updated = await UpdateNote(alice, note.Id, "\"1\"");
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal("\"2\"", updated.Headers.ETag?.Tag);
        Assert.Equal(2, (await updated.Content.ReadFromJsonAsync<NoteResponse>())?.Version);
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await UpdateNote(alice, note.Id, "\"1\"")).StatusCode);
    }

    [Fact]
    public async Task ApiKeyRotation_PreservesExpiration()
    {
        using var alice = CreateClient("dev-alice-api-key");
        var expiresAt = DateTime.UtcNow.AddHours(2);
        var createdResponse = await alice.PostAsJsonAsync(
            "/api/api-keys",
            new CreateApiKeyRequest { Name = "Expiring", ExpiresAt = expiresAt });
        var created = await createdResponse.Content.ReadFromJsonAsync<ApiKeyCreatedResponse>();
        Assert.NotNull(created);

        var rotateResponse = await alice.PostAsync($"/api/api-keys/{created.Id}/rotate", null);
        var rotated = await rotateResponse.Content.ReadFromJsonAsync<ApiKeyCreatedResponse>();
        Assert.Equal(HttpStatusCode.Created, rotateResponse.StatusCode);
        Assert.Equal(created.ExpiresAt, rotated?.ExpiresAt);
    }

    [Fact]
    public async Task ConcurrentCredentialRevocations_LeaveAUsableCredential()
    {
        using var alice = CreateClient("dev-alice-api-key");
        var team = await CreateTeam(alice);
        var inviteResponse = await alice.PostAsJsonAsync(
            $"/api/teams/{team.Id}/invitations",
            new InviteTeamMemberRequest { DisplayName = "Concurrent user", KeyName = "First" });
        var invitation = await inviteResponse.Content.ReadFromJsonAsync<InvitationResponse>();
        Assert.NotNull(invitation);
        using var invited = CreateClient(invitation.Credential.ApiKey);
        var secondResponse = await invited.PostAsJsonAsync(
            "/api/api-keys",
            new CreateApiKeyRequest { Name = "Second" });
        var second = await secondResponse.Content.ReadFromJsonAsync<ApiKeyCreatedResponse>();
        Assert.NotNull(second);

        await using var firstWriter = factory.CreateDbContext();
        await using var secondWriter = factory.CreateDbContext();
        var firstUser = await firstWriter.Users.Include(user => user.ApiKeys)
            .SingleAsync(user => user.Id == invitation.UserId);
        var secondUser = await secondWriter.Users.Include(user => user.ApiKeys)
            .SingleAsync(user => user.Id == invitation.UserId);
        firstUser.ApiKeys.Single(key => key.Id == invitation.Credential.Id).RevokedAt = DateTime.UtcNow;
        secondUser.ApiKeys.Single(key => key.Id == second.Id).RevokedAt = DateTime.UtcNow;
        firstUser.CredentialVersion++;
        secondUser.CredentialVersion++;

        await firstWriter.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => secondWriter.SaveChangesAsync());

        await using var verification = factory.CreateDbContext();
        Assert.True(await verification.ApiKeys.AnyAsync(
            key => key.UserId == invitation.UserId && key.RevokedAt == null));
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
            new CreateTeamRequest { Name = $"Ready {Guid.NewGuid():N}" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TeamResponse>())!;
    }

    private static Task<HttpResponseMessage> UpdateNote(HttpClient client, Guid noteId, string etag)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/notes/{noteId}")
        {
            Content = JsonContent.Create(new UpdateNoteRequest { Title = "Updated", Content = "v2" }),
        };
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        return client.SendAsync(request);
    }
}
