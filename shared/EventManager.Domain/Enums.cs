namespace EventManager.Domain;

public enum OrganizerRole { FullAdmin, CoOrganizer }

public enum BracketFormat { SingleElimination, RoundRobin }

public enum DivisionStatus { NotStarted, Started, Complete }

public enum WeighInPolicyMode { Strict, AutoMove, Tolerance }

/// <summary>Configurable penalty handling for point-sparring (Q2=D).</summary>
public enum PenaltyMode { AwardOpponent, DeductOffender }

public enum MatchMethod { Points, Forfeit, Disqualification, Decision }

public enum WeighInResult { Pass, TolerancePass, Disqualified, Moved }

public enum PaymentStatus { Paid, Owed, Waived }

public enum PaymentMethod { AtDoor, Card }

/// <summary>Organizer actions gated by RBAC (FR-2.8). FullAdminOnly subset enforced by policy.</summary>
public enum OrganizerAction
{
    // Full-Admin-only
    DeleteEvent,
    RemoveOrganizer,
    DemoteOrganizer,
    TransferFullAdmin,
    // Available to both Full Admin and Co-Organizer
    ManageRoster,
    ConfigureDivisions,
    GenerateBracket,
    ResolveWeighIn,
    ResolveDispute,
    ManageDevices,
    FinalizeResults
}
