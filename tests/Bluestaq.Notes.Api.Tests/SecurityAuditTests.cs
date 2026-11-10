using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bluestaq.Notes.Api.Auth;
using Bluestaq.Notes.Api.Contracts;
using Bluestaq.Notes.Api.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Bluestaq.Notes.Api.Tests;

[Trait("Category", "Integration")]
public sealed class SecurityAuditTests(NotesApiFactory factory) : IClassFixture<NotesApiFactory>
{
    private static readonly Guid BobId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task SecurityChanges_AreScopedPaginatedAndSanitized()
    {
        using var alice = CreateClient("dev-alice-api-key");
        using var bob = CreateClient("dev-bob-api-key-01");
        var team = await CreateTeam(alice);
        Assert.Equal(
            HttpStatusCode.Created,
            (await alice.PostAsJsonAsync(
                $"/api/teams/{team.Id}/members",
                new AddTeamMemberRequest { UserId = BobId })).StatusCode);
        var note = await CreateNote(alice, team.Id);
        Assert.Equal(
            HttpStatusCode.OK,
            (await alice.PutAsJsonAsync(
                $"/api/notes/{note.Id}/permissions/{BobId}",
                new SetNotePermissionRequest { Role = NotePermissionRole.Editor })).StatusCode);

        var createKeyResponse = await alice.PostAsJsonAsync(
            "/api/api-keys",
            new CreateApiKeyRequest { Name = "Audited key" });
        var createdKey = await createKeyResponse.Content.ReadFromJsonAsync<ApiKeyCreatedResponse>();
        Assert.NotNull(createdKey);

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await bob.GetAsync($"/api/teams/{team.Id}/security-events")).StatusCode);

        var teamEvents = await ReadAllEvents(alice, $"/api/teams/{team.Id}/security-events", 1);
        Assert.Contains(teamEvents, auditEvent => auditEvent.Action == "team.created");
        Assert.Contains(teamEvents, auditEvent =>
            auditEvent.Action == "team.member-added" && auditEvent.TargetId == BobId);
        Assert.Contains(teamEvents, auditEvent =>
            auditEvent.Action == "note.permission-set" && auditEvent.TargetId == BobId);

        var personalEvents = await ReadAllEvents(alice, "/api/security-events", 2);
        Assert.Contains(personalEvents, auditEvent =>
            auditEvent.Action == "api-key.created" && auditEvent.TargetId == createdKey.Id);
        Assert.All(personalEvents, auditEvent =>
        {
            Assert.NotEqual(Guid.Empty, auditEvent.ActorUserId);
            Assert.NotEqual(Guid.Empty, auditEvent.ActorCredentialId);
            Assert.False(string.IsNullOrWhiteSpace(auditEvent.TraceId));
        });

        var bobEvents = await ReadAllEvents(bob, "/api/security-events", 100);
        Assert.Contains(bobEvents, auditEvent =>
            auditEvent.Action == "team.member-added" && auditEvent.TargetId == BobId);
        Assert.Contains(bobEvents, auditEvent =>
            auditEvent.Action == "note.permission-set" && auditEvent.TargetId == BobId);

        var serialized = JsonSerializer.Serialize(personalEvents, JsonSerializerOptions.Web);
        Assert.DoesNotContain(createdKey.ApiKey, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("keyHash", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await alice.GetAsync("/api/security-events?pageSize=0")).StatusCode);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await alice.GetAsync("/api/security-events?cursor=invalid")).StatusCode);
    }

    [Fact]
    public async Task AuditEvents_SurviveTargetDeletionAndRejectDatabaseMutation()
    {
        using var alice = CreateClient("dev-alice-api-key");
        var team = await CreateTeam(alice);
        Assert.Equal(HttpStatusCode.NoContent, (await alice.DeleteAsync($"/api/teams/{team.Id}")).StatusCode);

        var personalEvents = await ReadAllEvents(alice, "/api/security-events", 100);
        var deleted = Assert.Single(
            personalEvents,
            auditEvent => auditEvent.Action == "team.deleted" && auditEvent.TargetId == team.Id);

        await using var db = factory.CreateDbContext();
        var count = await db.Database.SqlQuery<int>($$"""
            SELECT COUNT(*) AS "Value"
            FROM "SecurityAuditEvents"
            WHERE "Id" = {{deleted.Id}}
            """).SingleAsync();
        Assert.Equal(1, count);

        var tracked = await db.SecurityAuditEvents.SingleAsync(auditEvent => auditEvent.Id == deleted.Id);
        tracked.Action = "tampered";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        db.Entry(tracked).State = EntityState.Unchanged;

        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlInterpolatedAsync($$"""
            UPDATE "SecurityAuditEvents" SET "Action" = 'tampered' WHERE "Id" = {{deleted.Id}}
            """));
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlInterpolatedAsync($$"""
            DELETE FROM "SecurityAuditEvents" WHERE "Id" = {{deleted.Id}}
            """));
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
            new CreateTeamRequest { Name = $"Audit {Guid.NewGuid():N}" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TeamResponse>())!;
    }

    private static async Task<NoteResponse> CreateNote(HttpClient client, Guid teamId)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/teams/{teamId}/notes",
            new CreateNoteRequest { Title = "Audited permissions", Content = "No secrets." });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<NoteResponse>())!;
    }

    private static async Task<IReadOnlyCollection<SecurityAuditEventResponse>> ReadAllEvents(
        HttpClient client,
        string path,
        int pageSize)
    {
        var events = new List<SecurityAuditEventResponse>();
        string? cursor = null;
        do
        {
            var separator = path.Contains('?', StringComparison.Ordinal) ? '&' : '?';
            var requestPath = $"{path}{separator}pageSize={pageSize}";
            if (cursor is not null)
            {
                requestPath += $"&cursor={Uri.EscapeDataString(cursor)}";
            }

            var response = await client.GetAsync(requestPath);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var page = await response.Content.ReadFromJsonAsync<PagedSecurityAuditEventsResponse>();
            Assert.NotNull(page);
            events.AddRange(page.Items);
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        return events;
    }

    private sealed record SecurityAuditEventResponse(
        Guid Id,
        DateTime OccurredAt,
        Guid ActorUserId,
        Guid ActorCredentialId,
        string Action,
        Guid? TeamId,
        string TargetType,
        Guid TargetId,
        string TraceId,
        JsonElement Details);

    private sealed record PagedSecurityAuditEventsResponse(
        IReadOnlyCollection<SecurityAuditEventResponse> Items,
        string? NextCursor);
}
