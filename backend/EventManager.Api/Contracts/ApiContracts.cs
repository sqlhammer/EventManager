namespace EventManager.Api.Contracts;

// Account / auth
public sealed record RegisterAccountRequest(string Email, string Password);
public sealed record ConfirmEmailRequest(string Email, string Token);
public sealed record LoginRequest(string Email, string Password, string? Totp);
public sealed record TokenResponse(string AccessToken, string RefreshToken, DateTimeOffset AccessExpiresAt);
public sealed record RefreshRequest(string RefreshToken, string Email);
public sealed record MfaEnrollResponse(string SharedKey, string AuthenticatorUri, IReadOnlyList<string> RecoveryCodes);
public sealed record MfaConfirmRequest(string Totp);

// Event / division
public sealed record CreateEventRequest(string Name, string Venue, DateOnly Date, DateOnly RegistrationStart,
    DateOnly RegistrationEnd, decimal EntryFee, string WeighInPolicyMode, double? WeighInTolerancePercent);
public sealed record EditEventRequest(string Name, string Venue, DateOnly Date, DateOnly RegistrationStart,
    DateOnly RegistrationEnd, decimal EntryFee);
public sealed record PaymentOptionsRequest(bool CardEnabled);
public sealed record WeighInPolicyRequest(string Mode, double? TolerancePercent);
public sealed record ConfigureDivisionRequest(double? WeightLower, double WeightUpper, int MinRank, int MaxRank,
    int MinAge, int MaxAge, string Gender, string Format);
public sealed record IdResponse(long Id);

// Organizer
public sealed record AddOrganizerRequest(long? AccountId, string? Email);
public sealed record ChangeRoleRequest(long TargetAccountId, string NewRole);

// Registration
public sealed record ProfileRequest(string Name, DateOnly DateOfBirth, int Rank, double Weight, string Academy, string Gender);
public sealed record RegisterRequest(long EventId, long AthleteId, IReadOnlyList<long> DivisionIds, bool PayByCard);
public sealed record BatchEntryRequest(long AthleteId, IReadOnlyList<long> DivisionIds);
public sealed record BatchRegisterRequest(long EventId, IReadOnlyList<BatchEntryRequest> Entries, bool PayByCard, string IdempotencyKey);
public sealed record EditRegistrationRequest(IReadOnlyList<long> DivisionIds);
public sealed record PaymentStatusRequest(string Status);
