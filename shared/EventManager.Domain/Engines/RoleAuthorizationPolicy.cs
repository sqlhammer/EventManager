namespace EventManager.Domain.Engines;

public interface IRoleAuthorizationPolicy
{
    bool IsPermitted(OrganizerRoleAssignment? assignment, OrganizerAction action);
}

/// <summary>
/// Pure, deny-by-default RBAC policy (FR-2.8, BR-6.x). The identical instance is used by the
/// cloud (U3) and the hub (U4a) so authorization cannot diverge.
/// </summary>
public sealed class RoleAuthorizationPolicy : IRoleAuthorizationPolicy
{
    private static readonly HashSet<OrganizerAction> FullAdminOnly =
    [
        OrganizerAction.DeleteEvent,
        OrganizerAction.RemoveOrganizer,
        OrganizerAction.DemoteOrganizer,
        OrganizerAction.TransferFullAdmin
    ];

    public bool IsPermitted(OrganizerRoleAssignment? assignment, OrganizerAction action)
    {
        if (assignment is null) return false; // deny by default (BR-6.1)

        if (FullAdminOnly.Contains(action))
            return assignment.Role == OrganizerRole.FullAdmin; // BR-6.2

        // All other organizer actions available to both roles (BR-6.3).
        return assignment.Role is OrganizerRole.FullAdmin or OrganizerRole.CoOrganizer;
    }
}
