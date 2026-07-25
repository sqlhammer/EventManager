using EventManager.Contracts;
using EventManager.Sync;
using Xunit;

namespace EventManager.Contracts.Tests;

public class ContractsTests
{
    [Fact] // BR-1.6 envelope round-trips losslessly
    public void Envelope_RoundTripsLosslessly()
    {
        var payload = new byte[] { 1, 2, 3, 4, 5 };
        var evt = new TournamentEvent(101, 7, 3, "MatchScored", 2, payload, DateTimeOffset.UnixEpoch.AddMinutes(5), 42);

        var back = EventEnvelopeMapper.ToEvent(EventEnvelopeMapper.ToDto(evt));

        Assert.Equal(evt.EventId, back.EventId);
        Assert.Equal(evt.DeviceId, back.DeviceId);
        Assert.Equal(evt.SequenceNumber, back.SequenceNumber);
        Assert.Equal(evt.EventType, back.EventType);
        Assert.Equal(evt.SchemaVersion, back.SchemaVersion);
        Assert.Equal(evt.OccurredAt, back.OccurredAt);
        Assert.Equal(evt.EventScopeId, back.EventScopeId);
        Assert.Equal(payload, back.Payload.ToArray());
    }

    [Fact]
    public void EventEnvelopeValidator_RejectsBadEnvelope()
    {
        var v = new EventEnvelopeDtoValidator();
        var bad = new EventEnvelopeDto(0, 0, 0, "", 0, "not base64!!", default, 0);
        Assert.False(v.Validate(bad).IsValid);
    }

    [Fact]
    public void EventEnvelopeValidator_AcceptsGoodEnvelope()
    {
        var v = new EventEnvelopeDtoValidator();
        var good = EventEnvelopeMapper.ToDto(
            new TournamentEvent(1, 1, 1, "T", 1, new byte[] { 9 }, DateTimeOffset.UnixEpoch, 1));
        Assert.True(v.Validate(good).IsValid);
    }

    [Fact]
    public void PairingResponseValidator_EnforcesWorkerIdRange()
    {
        var v = new PairingResponseDtoValidator();
        Assert.False(v.Validate(new PairingResponseDto(1, 2000, "r", "fp")).IsValid); // worker id out of range
        Assert.True(v.Validate(new PairingResponseDto(1, 5, "r", "fp")).IsValid);
    }
}
