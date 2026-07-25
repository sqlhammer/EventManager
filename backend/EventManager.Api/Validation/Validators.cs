using EventManager.Api.Contracts;
using EventManager.Domain;
using FluentValidation;

namespace EventManager.Api.Validation;

// FluentValidation runs before controller logic / the write path (SP-3, U3-NFR-S9, BR-X-3).

public sealed class RegisterAccountValidator : AbstractValidator<RegisterAccountRequest>
{
    public RegisterAccountValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8); // breach check runs in Identity validator
    }
}

public sealed class LoginValidator : AbstractValidator<LoginRequest>
{
    public LoginValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty();
    }
}

public sealed class CreateEventValidator : AbstractValidator<CreateEventRequest>
{
    public CreateEventValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Venue).NotEmpty().MaximumLength(200);
        RuleFor(x => x.EntryFee).GreaterThanOrEqualTo(0);
        RuleFor(x => x.RegistrationEnd).GreaterThanOrEqualTo(x => x.RegistrationStart)
            .WithMessage("Registration window start must be on/before end.");
        RuleFor(x => x.WeighInPolicyMode).Must(m => Enum.TryParse<WeighInPolicyMode>(m, out _));
        RuleFor(x => x.WeighInTolerancePercent)
            .NotNull().When(x => x.WeighInPolicyMode == nameof(WeighInPolicyMode.Tolerance))
            .WithMessage("Tolerance policy requires a percentage.");
    }
}

public sealed class EditEventValidator : AbstractValidator<EditEventRequest>
{
    public EditEventValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.EntryFee).GreaterThanOrEqualTo(0);
        RuleFor(x => x.RegistrationEnd).GreaterThanOrEqualTo(x => x.RegistrationStart);
    }
}

public sealed class ConfigureDivisionValidator : AbstractValidator<ConfigureDivisionRequest>
{
    public ConfigureDivisionValidator()
    {
        RuleFor(x => x.WeightUpper).GreaterThan(0);
        RuleFor(x => x.MaxRank).GreaterThanOrEqualTo(x => x.MinRank);
        RuleFor(x => x.MaxAge).GreaterThanOrEqualTo(x => x.MinAge);
        RuleFor(x => x.Gender).NotEmpty();
        RuleFor(x => x.Format).Must(f => Enum.TryParse<BracketFormat>(f, out _));
    }
}

public sealed class ProfileValidator : AbstractValidator<ProfileRequest>
{
    public ProfileValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Weight).GreaterThan(0).LessThanOrEqualTo(500);
        RuleFor(x => x.Gender).NotEmpty();
        RuleFor(x => x.DateOfBirth).LessThan(DateOnly.FromDateTime(DateTime.UtcNow));
    }
}

public sealed class RegisterValidator : AbstractValidator<RegisterRequest>
{
    public RegisterValidator() => RuleFor(x => x.DivisionIds).NotEmpty().WithMessage("Select at least one division.");
}

public sealed class BatchRegisterValidator : AbstractValidator<BatchRegisterRequest>
{
    public BatchRegisterValidator()
    {
        RuleFor(x => x.IdempotencyKey).NotEmpty();
        RuleFor(x => x.Entries).NotEmpty();
        RuleForEach(x => x.Entries).ChildRules(e => e.RuleFor(x => x.DivisionIds).NotEmpty());
        RuleFor(x => x.Entries).Must(e => e.Count <= 200).WithMessage("Batch exceeds the 200-athlete cap."); // U3-NFR-P2
    }
}

public sealed class AddOrganizerValidator : AbstractValidator<AddOrganizerRequest>
{
    public AddOrganizerValidator() =>
        RuleFor(x => x).Must(x => x.AccountId is not null || !string.IsNullOrWhiteSpace(x.Email))
            .WithMessage("Provide either an existing account id or an invitee email.");
}

public sealed class ChangeRoleValidator : AbstractValidator<ChangeRoleRequest>
{
    public ChangeRoleValidator() => RuleFor(x => x.NewRole).Must(r => Enum.TryParse<OrganizerRole>(r, out _));
}
