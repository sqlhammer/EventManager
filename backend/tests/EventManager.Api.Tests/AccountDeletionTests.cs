using EventManager.Api.Persistence;
using EventManager.Api.Services;
using EventManager.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EventManager.Api.Tests;

/// <summary>
/// Self-service account deletion (US-110): sole-Full-Admin guard, re-authentication, and the
/// soft-delete + anonymize + detach-roles + revoke-tokens flow. Guard tests are DB-only; the
/// full-flow tests wire a real <see cref="UserManager{TUser}"/> over the in-memory store.
/// </summary>
public sealed class AccountDeletionTests
{
    private const string Password = "Str0ng-Passphrase-92!";

    // ---- Guard (DB-only) ----

    [Fact]
    public async Task Guard_flags_event_where_account_is_sole_full_admin()
    {
        using var h = new TestHost();
        var (eventId, _, _, admin) = await h.SeedOpenEventAsync();

        var blocked = await new AccountDeletionGuard(h.Db).SoleFullAdminEventsAsync(admin);

        Assert.Contains(eventId, blocked);
    }

    [Fact]
    public async Task Guard_does_not_flag_when_a_second_full_admin_exists()
    {
        using var h = new TestHost();
        var (eventId, _, _, admin) = await h.SeedOpenEventAsync();
        long second = await AddSecondFullAdminAsync(h, admin, eventId);

        var guard = new AccountDeletionGuard(h.Db);

        Assert.Empty(await guard.SoleFullAdminEventsAsync(admin));   // original admin no longer sole
        Assert.Empty(await guard.SoleFullAdminEventsAsync(second));  // neither is sole
    }

    // ---- Full flow (real UserManager) ----

    [Fact]
    public async Task Delete_anonymizes_account_disables_login_and_detaches_roles()
    {
        using var h = new TestHost();
        var users = CreateUserManager(h.Db);
        var user = await CreateUserAsync(users, h.Ids.NextId(), "org@example.com");

        var eventId = await CreateEventOwnedByAsync(h, user.AccountId);
        await AddSecondFullAdminAsync(h, user.AccountId, eventId);   // so the account isn't the sole admin
        Assert.True(await h.Db.OrganizerRows.AnyAsync(o => o.AccountId == user.AccountId));

        var svc = MakeService(h, users);
        var result = await svc.DeleteOwnAccountAsync(user.AccountId, Password, totp: null);
        Assert.False(result.IsError);

        var reloaded = await users.FindByIdAsync(user.Id.ToString());
        Assert.NotNull(reloaded);
        Assert.NotNull(reloaded!.DeletedAt);
        Assert.Null(reloaded.PasswordHash);
        Assert.Equal($"deleted-{user.AccountId}@deleted.invalid", reloaded.Email);
        Assert.False(await users.CheckPasswordAsync(reloaded, Password));          // cannot authenticate
        Assert.False(await h.Db.OrganizerRows.AnyAsync(o => o.AccountId == user.AccountId)); // role detached
    }

    [Fact]
    public async Task Delete_is_blocked_when_account_is_sole_full_admin()
    {
        using var h = new TestHost();
        var users = CreateUserManager(h.Db);
        var user = await CreateUserAsync(users, h.Ids.NextId(), "solo@example.com");
        await CreateEventOwnedByAsync(h, user.AccountId);   // sole Full Admin

        var result = await MakeService(h, users).DeleteOwnAccountAsync(user.AccountId, Password, totp: null);

        Assert.True(result.IsError);
        Assert.Equal("Account.SoleFullAdmin", result.FirstError.Code);
        var reloaded = await users.FindByIdAsync(user.Id.ToString());
        Assert.Null(reloaded!.DeletedAt);   // untouched — nothing was deleted
    }

    [Fact]
    public async Task Delete_rejects_a_wrong_password()
    {
        using var h = new TestHost();
        var users = CreateUserManager(h.Db);
        var user = await CreateUserAsync(users, h.Ids.NextId(), "pw@example.com");

        var result = await MakeService(h, users).DeleteOwnAccountAsync(user.AccountId, "not-my-password", totp: null);

        Assert.True(result.IsError);
        Assert.Equal("Account.Delete", result.FirstError.Code);
        Assert.Null((await users.FindByIdAsync(user.Id.ToString()))!.DeletedAt);
    }

    // ---- helpers ----

    private static AccountDeletionService MakeService(TestHost h, UserManager<AppUser> users) =>
        new(users, new AccountDeletionGuard(h.Db), h.Writer, new TokenService(new JwtOptions { SigningKey = "test-key" }, new RefreshTokenStore(h.Db)), h.Db);

    private static async Task<AppUser> CreateUserAsync(UserManager<AppUser> users, long accountId, string email)
    {
        var user = new AppUser { UserName = email, Email = email, AccountId = accountId };
        var created = await users.CreateAsync(user, Password);
        Assert.True(created.Succeeded);
        return user;
    }

    private static async Task<long> CreateEventOwnedByAsync(TestHost h, long accountId)
    {
        var create = await h.Events.CreateEventAsync(accountId, new CreateEventInput(
            "Owned Event", "Dojo", new DateOnly(2026, 9, 1), new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 20),
            50m, nameof(WeighInPolicyMode.Strict), null));
        return create.Value;
    }

    private static async Task<long> AddSecondFullAdminAsync(TestHost h, long callerAdmin, long eventId)
    {
        var organizers = new OrganizerRoleService(h.Db, h.Writer, h.Ids, h.Authorizer, new NoopEmail());
        long second = h.Ids.NextId();
        await organizers.AddExistingAsync(callerAdmin, eventId, second);
        await organizers.ChangeRoleAsync(callerAdmin, eventId, second, nameof(OrganizerRole.FullAdmin));
        return second;
    }

    private static UserManager<AppUser> CreateUserManager(AppDbContext db)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(db);   // share the test's DbContext instance with the identity store
        services.AddIdentityCore<AppUser>(o => o.Password.RequiredLength = 8)
            .AddEntityFrameworkStores<AppDbContext>();
        return services.BuildServiceProvider().GetRequiredService<UserManager<AppUser>>();
    }

    private sealed class NoopEmail : IEmailSender
    {
        public Task SendConfirmationAsync(string toAddress, string token, CancellationToken ct = default) => Task.CompletedTask;
        public Task SendOrganizerInviteAsync(string toAddress, string token, CancellationToken ct = default) => Task.CompletedTask;
    }
}
