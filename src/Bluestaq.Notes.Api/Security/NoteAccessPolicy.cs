using Bluestaq.Notes.Api.Data;

namespace Bluestaq.Notes.Api.Security;

/// <summary>Defines the read, edit, and administration rules for team notes.</summary>
public static class NoteAccessPolicy
{
    /// <summary>Returns whether a team member may read a note.</summary>
    public static bool CanRead(
        NoteVisibility visibility,
        Guid createdById,
        Guid ownerId,
        Guid userId,
        NotePermissionRole? role) =>
        visibility == NoteVisibility.Team
        || createdById == userId
        || ownerId == userId
        || role is NotePermissionRole.Reader or NotePermissionRole.Editor;

    /// <summary>Returns whether a team member may replace a note's content.</summary>
    public static bool CanEdit(Guid createdById, Guid ownerId, Guid userId, NotePermissionRole? role) =>
        createdById == userId || ownerId == userId || role == NotePermissionRole.Editor;

    /// <summary>Returns whether a team member may delete a note or manage its permissions.</summary>
    public static bool CanAdminister(Guid createdById, Guid ownerId, Guid userId) =>
        createdById == userId || ownerId == userId;
}
