using FluentValidation;

namespace EventManager.Contracts;

/// <summary>FluentValidation validators (U2-TSD-1). Run before the event-log write path (NFR-2.4).</summary>
public sealed class EventEnvelopeDtoValidator : AbstractValidator<EventEnvelopeDto>
{
    public EventEnvelopeDtoValidator()
    {
        RuleFor(x => x.EventId).GreaterThan(0);
        RuleFor(x => x.DeviceId).GreaterThan(0);
        RuleFor(x => x.SequenceNumber).GreaterThan(0);
        RuleFor(x => x.EventType).NotEmpty();
        RuleFor(x => x.SchemaVersion).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PayloadBase64).Must(BeBase64).WithMessage("PayloadBase64 must be valid base64.");
        RuleFor(x => x.OccurredAt).NotEqual(default(DateTimeOffset));
    }

    internal static bool BeBase64(string s)
    {
        if (s is null) return false;
        Span<byte> buffer = Array.Empty<byte>();
        if (s.Length > 0) buffer = new byte[s.Length];
        return Convert.TryFromBase64String(s, buffer, out _);
    }
}

public sealed class ReplicationBatchDtoValidator : AbstractValidator<ReplicationBatchDto>
{
    public ReplicationBatchDtoValidator(int maxBatch = 1000)
    {
        RuleFor(x => x.Events).NotNull();
        RuleFor(x => x.Events.Count).LessThanOrEqualTo(maxBatch);
        RuleForEach(x => x.Events).SetValidator(new EventEnvelopeDtoValidator());
    }
}

public sealed class PairingRequestDtoValidator : AbstractValidator<PairingRequestDto>
{
    public PairingRequestDtoValidator() => RuleFor(x => x.EnrollmentToken).NotEmpty();
}

public sealed class PairingResponseDtoValidator : AbstractValidator<PairingResponseDto>
{
    public PairingResponseDtoValidator()
    {
        RuleFor(x => x.DeviceId).GreaterThan(0);
        RuleFor(x => x.WorkerId).InclusiveBetween(0, 1023);
        RuleFor(x => x.HubCertFingerprint).NotEmpty();
    }
}

public sealed class HubDiscoveryInfoDtoValidator : AbstractValidator<HubDiscoveryInfoDto>
{
    public HubDiscoveryInfoDtoValidator()
    {
        RuleFor(x => x.HubAddress).NotEmpty();
        RuleFor(x => x.Port).InclusiveBetween(1, 65535);
        RuleFor(x => x.CertFingerprint).NotEmpty();
    }
}
