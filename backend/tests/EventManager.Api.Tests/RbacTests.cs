using EventManager.Api.Auth;
using EventManager.Api.Services;
using EventManager.Domain;
using FsCheck.Xunit;

namespace EventManager.Api.Tests;

/// <summary>PBT-3 + example: RBAC deny-by-default (BR-RBAC-2) and last-admin guard (BR-RBAC-3).</summary>
public sealed class RbacTests
{
    [Property(MaxTest = 100)] // PBT-3 deny-by-default
    public void No_assignment_denies_every_action(int actionSeed)
    {
        var actions = Enum.GetValues<OrganizerAction>();
        var action = actions[Math.Abs(actionSeed) % actions.Length];
        using var h = new TestHost();
        // account with no organizer row on event 999
        var permitted = h.Authorizer.IsPermittedAsync(accountId: 42, eventId: 999, action).GetAwaiter().GetResult();
        Assert.False(permitted);
    }

    [Fact]
    public async Task CoOrganizer_cannot_perform_full_admin_only_actions()
    {
        using var h = new TestHost();
        var (eventId, _, _, adminAccount) = await h.SeedOpenEventAsync();
        var organizers = MakeOrganizerService(h);
        long coOrg = h.Ids.NextId();
        await organizers.AddExistingAsync(adminAccount, eventId, coOrg);

        Assert.False(await h.Authorizer.IsPermittedAsync(coOrg, eventId, OrganizerAction.DeleteEvent));
        Assert.False(await h.Authorizer.IsPermittedAsync(coOrg, eventId, OrganizerAction.RemoveOrganizer));
        // but a shared action is allowed
        Assert.True(await h.Authorizer.IsPermittedAsync(coOrg, eventId, OrganizerAction.ManageRoster));
    }

    [Fact]
    public async Task Cannot_demote_last_full_admin()
    {
        using var h = new TestHost();
        var (eventId, _, _, adminAccount) = await h.SeedOpenEventAsync();
        var organizers = MakeOrganizerService(h);

        var result = await organizers.ChangeRoleAsync(adminAccount, eventId, adminAccount, nameof(OrganizerRole.CoOrganizer));
        Assert.True(result.IsError);   // last-admin guard
    }

    private static OrganizerRoleService MakeOrganizerService(TestHost h) =>
        new(h.Db, h.Writer, h.Ids, h.Authorizer, new FakeEmail());

    private sealed class FakeEmail : IEmailSender
    {
        public Task SendConfirmationAsync(string toAddress, string token, CancellationToken ct = default) => Task.CompletedTask;
        public Task SendOrganizerInviteAsync(string toAddress, string token, CancellationToken ct = default) => Task.CompletedTask;
    }
}
