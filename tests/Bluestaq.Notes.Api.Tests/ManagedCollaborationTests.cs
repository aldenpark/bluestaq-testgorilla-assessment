using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bluestaq.Notes.Api.Auth;
using Bluestaq.Notes.Api.Contracts;
using Bluestaq.Notes.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Bluestaq.Notes.Api.Tests;

[Trait("Category", "Integration")]
public sealed class ManagedCollaborationTests(NotesApiFactory factory) : IClassFixture<NotesApiFactory>
{
    private static readonly Guid BobId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task InvitationAndApiKeyLifecycle_ReturnSecretsOnceAndRevokeImmediately()
    {
        using var alice = CreateClient("dev-alice-api-key");
        var team = await CreateTeam(alice);

        var inviteResponse = await alice.PostTeamAsync(
            team.Id,
            "/invitations",
            new InviteTeamMemberRequest { DisplayName = "Invited user", KeyName = "Initial" });
        Assert.Equal(HttpStatusCode.Created, inviteResponse.StatusCode);
        var invitation = await inviteResponse.Content.ReadFromJsonAsync<InvitationResponse>();
        Assert.NotNull(invitation);
        Assert.StartsWith("bns_", invitation.Credential.ApiKey);

        using var invited = CreateClient(invitation.Credential.ApiKey);
        Assert.Equal(HttpStatusCode.OK, (await invited.GetAsync($"/api/teams/{team.Id}")).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await alice.DeleteAsync($"/api/api-keys/{invitation.Credential.Id}")).StatusCode);

        var listJson = await invited.GetStringAsync("/api/api-keys");
        Assert.DoesNotContain(invitation.Credential.ApiKey, listJson, StringComparison.Ordinal);
        using (var document = JsonDocument.Parse(listJson))
        {
            Assert.False(document.RootElement[0].TryGetProperty("apiKey", out _));
        }

        var createResponse = await invited.PostAsJsonAsync(
            "/api/api-keys",
            new CreateApiKeyRequest { Name = "Automation" });
        var created = await createResponse.Content.ReadFromJsonAsync<ApiKeyCreatedResponse>();
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.NotNull(created);

        var rotateResponse = await invited.PostAsync(
            $"/api/api-keys/{invitation.Credential.Id}/rotate",
            null);
        var rotated = await rotateResponse.Content.ReadFromJsonAsync<ApiKeyCreatedResponse>();
        Assert.Equal(HttpStatusCode.Created, rotateResponse.StatusCode);
        Assert.NotNull(rotated);
        Assert.Equal(HttpStatusCode.Unauthorized, (await invited.GetAsync("/api/teams")).StatusCode);

        using var replacement = CreateClient(rotated.ApiKey);
        Assert.Equal(HttpStatusCode.OK, (await replacement.GetAsync("/api/teams")).StatusCode);
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await replacement.DeleteAsync($"/api/api-keys/{created.Id}")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Conflict,
            (await replacement.DeleteAsync($"/api/api-keys/{rotated.Id}")).StatusCode);
    }

    [Fact]
    public async Task ExpiredApiKey_IsRejectedImmediately()
    {
        using var alice = CreateClient("dev-alice-api-key");
        var createResponse = await alice.PostAsJsonAsync(
            "/api/api-keys",
            new CreateApiKeyRequest { Name = "Expiring", ExpiresAt = DateTime.UtcNow.AddMinutes(5) });
        var created = await createResponse.Content.ReadFromJsonAsync<ApiKeyCreatedResponse>();
        Assert.NotNull(created);

        await using (var db = factory.CreateDbContext())
        {
            var credential = await db.ApiKeys.SingleAsync(key => key.Id == created.Id);
            credential.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        using var expired = CreateClient(created.ApiKey);
        Assert.Equal(HttpStatusCode.Unauthorized, (await expired.GetAsync("/api/teams")).StatusCode);
    }

    [Fact]
    public async Task NotePermissions_EnforceReaderEditorCreatorAndOwnerBoundaries()
    {
        using var alice = CreateClient("dev-alice-api-key");
        using var bob = CreateClient("dev-bob-api-key-01");
        var team = await CreateTeam(alice);
        Assert.Equal(HttpStatusCode.Created, (await AddBob(alice, team.Id)).StatusCode);
        var bobNote = await CreateNote(bob, team.Id, "Member note", "Owner can administer");
        Assert.Equal(HttpStatusCode.OK, (await UpdateNote(alice, bobNote.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await alice.DeleteNoteAsync(bobNote.Id)).StatusCode);
        var note = await CreateNote(alice, team.Id, "Permission test", "Owner content");

        Assert.Equal(HttpStatusCode.OK, (await bob.GetAsync($"/api/notes/{note.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await UpdateNote(bob, note.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.DeleteAsync($"/api/notes/{note.Id}")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await bob.GetAsync($"/api/notes/{note.Id}/permissions")).StatusCode);

        var grant = await alice.PutNoteAsync(
            note.Id,
            $"/permissions/{BobId}",
            new SetNotePermissionRequest { Role = NotePermissionRole.Editor });
        Assert.Equal(HttpStatusCode.OK, grant.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await UpdateNote(bob, note.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.DeleteAsync($"/api/notes/{note.Id}")).StatusCode);

        var downgrade = await alice.PutNoteAsync(
            note.Id,
            $"/permissions/{BobId}",
            new SetNotePermissionRequest { Role = NotePermissionRole.Reader });
        Assert.Equal(HttpStatusCode.OK, downgrade.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await UpdateNote(bob, note.Id)).StatusCode);

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await alice.DeleteTeamAsync(team.Id, $"/members/{BobId}")).StatusCode);
        await using var db = factory.CreateDbContext();
        Assert.False(await db.NotePermissions.AnyAsync(
            permission => permission.NoteId == note.Id && permission.UserId == BobId));
    }

    [Fact]
    public async Task TeamMutation_IsOwnerOnlyAndDeleteCascades()
    {
        using var alice = CreateClient("dev-alice-api-key");
        using var bob = CreateClient("dev-bob-api-key-01");
        var team = await CreateTeam(alice);
        Assert.Equal(HttpStatusCode.Created, (await AddBob(alice, team.Id)).StatusCode);
        var note = await CreateNote(alice, team.Id, "Cascade", "Delete with team");

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await bob.PatchAsJsonAsync($"/api/teams/{team.Id}", new UpdateTeamRequest { Name = "No" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.DeleteAsync($"/api/teams/{team.Id}")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await bob.PostAsJsonAsync(
                $"/api/teams/{team.Id}/invitations",
                new InviteTeamMemberRequest { DisplayName = "Denied", KeyName = "Denied" })).StatusCode);

        var rename = await alice.PatchTeamAsync(
            team.Id,
            new UpdateTeamRequest { Name = "Renamed" });
        Assert.Equal("Renamed", (await rename.Content.ReadFromJsonAsync<TeamResponse>())?.Name);
        Assert.Equal(HttpStatusCode.NoContent, (await alice.DeleteTeamAsync(team.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync($"/api/teams/{team.Id}")).StatusCode);

        await using var db = factory.CreateDbContext();
        Assert.False(await db.Notes.AnyAsync(candidate => candidate.Id == note.Id));
    }

    [Fact]
    public async Task NoteDiscovery_SearchesFiltersAndTraversesOpaqueCursors()
    {
        using var alice = CreateClient("dev-alice-api-key");
        var team = await CreateTeam(alice);
        var first = await CreateNote(alice, team.Id, "Alpha one", "shared phrase");
        await Task.Delay(5);
        var second = await CreateNote(alice, team.Id, "Alpha two", "shared phrase");
        await CreateNote(alice, team.Id, "Unrelated", "different content");
        var otherTeam = await CreateTeam(alice);
        await CreateNote(alice, otherTeam.Id, "Other team", "shared phrase");
        await using (var db = factory.CreateDbContext())
        {
            var tiedNotes = await db.Notes.Where(note => note.Id == first.Id || note.Id == second.Id).ToArrayAsync();
            var tiedTimestamp = DateTime.UtcNow.AddMinutes(-1);
            foreach (var note in tiedNotes)
            {
                note.UpdatedAt = tiedTimestamp;
            }

            await db.SaveChangesAsync();
        }

        var firstPage = await alice.GetFromJsonAsync<PagedNotesResponse>(
            $"/api/teams/{team.Id}/notes?q=shared%20phrase&pageSize=1");
        Assert.NotNull(firstPage);
        Assert.Single(firstPage.Items);
        Assert.NotNull(firstPage.NextCursor);

        var secondPage = await alice.GetFromJsonAsync<PagedNotesResponse>(
            $"/api/teams/{team.Id}/notes?q=shared%20phrase&pageSize=1&cursor={Uri.EscapeDataString(firstPage.NextCursor)}");
        Assert.NotNull(secondPage);
        Assert.Single(secondPage.Items);
        Assert.Null(secondPage.NextCursor);
        var expectedOrder = new[] { first.Id, second.Id }.OrderByDescending(id => id);
        Assert.Equal(expectedOrder, firstPage.Items.Concat(secondPage.Items).Select(note => note.Id));

        var filtered = await alice.GetFromJsonAsync<PagedNotesResponse>(
            $"/api/teams/{team.Id}/notes?createdById={team.OwnerId}&updatedAfter={Uri.EscapeDataString(first.CreatedAt.AddMinutes(-2).ToString("O"))}");
        Assert.NotNull(filtered);
        Assert.Equal(3, filtered.Items.Count);

        var punctuation = await alice.GetAsync($"/api/teams/{team.Id}/notes?q=%22");
        Assert.Equal(HttpStatusCode.OK, punctuation.StatusCode);
    }

    [Fact]
    public async Task ManagedCollaborationMigration_PreservesLegacyCredentialInBothDirections()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"bluestaq-migration-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<NotesDbContext>()
                .UseSqlite($"Data Source={databasePath}")
                .Options;
            await using var db = new NotesDbContext(options);
            var migrator = db.Database.GetService<IMigrator>();
            await migrator.MigrateAsync("20261110173858_InitialCreate");

            var userId = Guid.NewGuid();
            var teamId = Guid.NewGuid();
            var noteId = Guid.NewGuid();
            const string legacyHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
            await db.Database.ExecuteSqlInterpolatedAsync($$"""
                INSERT INTO "Users" ("Id", "DisplayName", "ApiKeyHash")
                VALUES ({{userId}}, 'Legacy user', {{legacyHash}})
                """);
            await db.Database.ExecuteSqlInterpolatedAsync($$"""
                INSERT INTO "Teams" ("Id", "Name", "OwnerId", "CreatedAt")
                VALUES ({{teamId}}, 'Legacy team', {{userId}}, {{DateTime.UtcNow}})
                """);
            await db.Database.ExecuteSqlInterpolatedAsync($$"""
                INSERT INTO "Notes" (
                    "Id", "TeamId", "Title", "Content", "CreatedById", "UpdatedById", "CreatedAt", "UpdatedAt")
                VALUES (
                    {{noteId}}, {{teamId}}, 'Legacy note', 'Legacy content', {{userId}}, {{userId}},
                    {{DateTime.UtcNow}}, {{DateTime.UtcNow}})
                """);

            await migrator.MigrateAsync();
            Assert.Equal(
                legacyHash,
                await db.ApiKeys.Where(key => key.UserId == userId).Select(key => key.KeyHash).SingleAsync());
            var migratedNote = await db.Notes.SingleAsync(note => note.Id == noteId);
            Assert.Equal(NoteVisibility.Team, migratedNote.Visibility);
            Assert.Equal(1, migratedNote.Version);
            Assert.Equal(1, await db.Users.Where(user => user.Id == userId).Select(user => user.CredentialVersion).SingleAsync());

            await migrator.MigrateAsync("20261110173858_InitialCreate");
            var restoredHash = await db.Database.SqlQuery<string>($$"""
                SELECT "ApiKeyHash" AS "Value" FROM "Users" WHERE "Id" = {{userId}}
                """).SingleAsync();
            Assert.Equal(legacyHash, restoredHash);
        }
        finally
        {
            foreach (var path in new[] { databasePath, $"{databasePath}-shm", $"{databasePath}-wal" })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
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
            new CreateTeamRequest { Name = $"Managed {Guid.NewGuid():N}" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TeamResponse>())!;
    }

    private static Task<HttpResponseMessage> AddBob(HttpClient client, Guid teamId) =>
        client.PostTeamAsync(teamId, "/members", new AddTeamMemberRequest { UserId = BobId });

    private static async Task<NoteResponse> CreateNote(
        HttpClient client,
        Guid teamId,
        string title,
        string content)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/teams/{teamId}/notes",
            new CreateNoteRequest { Title = title, Content = content });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<NoteResponse>())!;
    }

    private static async Task<HttpResponseMessage> UpdateNote(HttpClient client, Guid noteId)
    {
        using var current = await client.GetAsync($"/api/notes/{noteId}");
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/notes/{noteId}")
        {
            Content = JsonContent.Create(
                new UpdateNoteRequest { Title = "Edited", Content = "Editor content" }),
        };
        request.Headers.TryAddWithoutValidation("If-Match", current.Headers.ETag?.Tag);
        return await client.SendAsync(request);
    }
}
