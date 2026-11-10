using Bluestaq.Notes.Api.Auth;
using Bluestaq.Notes.Api.Data;
using Bluestaq.Notes.Api.Security;

namespace Bluestaq.Notes.Api.Tests;

[Trait("Category", "Unit")]
public sealed class PolicyTests
{
    private static readonly DateTime Now = new(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void CredentialPolicy_RequiresAnotherUsableKeyBeforeRevocation()
    {
        var target = Credential(Guid.NewGuid());
        Assert.False(CredentialPolicy.CanRevoke(target.Id, [target], Now));
        Assert.True(CredentialPolicy.CanRevoke(target.Id, [target, Credential(Guid.NewGuid())], Now));
        Assert.False(CredentialPolicy.CanRevoke(
            target.Id,
            [target, Credential(Guid.NewGuid(), expiresAt: Now.AddSeconds(-1))],
            Now));
    }

    [Fact]
    public void CredentialPolicy_ThrottlesLastUsedWrites()
    {
        Assert.True(CredentialPolicy.ShouldUpdateLastUsed(null, Now));
        Assert.False(CredentialPolicy.ShouldUpdateLastUsed(Now.AddMinutes(-14), Now));
        Assert.True(CredentialPolicy.ShouldUpdateLastUsed(Now.AddMinutes(-15), Now));
    }

    [Fact]
    public void NoteAccessPolicy_MakesReaderMeaningfulForRestrictedNotes()
    {
        var creator = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var member = Guid.NewGuid();
        Assert.True(NoteAccessPolicy.CanRead(NoteVisibility.Team, creator, owner, member, null));
        Assert.False(NoteAccessPolicy.CanRead(NoteVisibility.Restricted, creator, owner, member, null));
        Assert.True(NoteAccessPolicy.CanRead(
            NoteVisibility.Restricted,
            creator,
            owner,
            member,
            NotePermissionRole.Reader));
        Assert.False(NoteAccessPolicy.CanEdit(creator, owner, member, NotePermissionRole.Reader));
        Assert.True(NoteAccessPolicy.CanEdit(creator, owner, member, NotePermissionRole.Editor));
    }

    private static ApiKeyCredential Credential(Guid id, DateTime? expiresAt = null) => new()
    {
        Id = id,
        UserId = Guid.NewGuid(),
        KeyHash = new string('a', 64),
        Name = "test",
        CreatedAt = Now,
        ExpiresAt = expiresAt,
    };
}
