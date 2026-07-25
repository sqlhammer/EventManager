using EventManager.Api.Persistence;
using EventManager.Api.Services;

namespace EventManager.Api.Tests;

/// <summary>Example: offline breached-password check (SP-5, BR-AUTH-1).</summary>
public sealed class SecurityTests
{
    [Theory]
    [InlineData("password")]
    [InlineData("123456")]
    [InlineData("qwerty")]
    public async Task Breached_passwords_are_rejected(string pwd)
    {
        var validator = new BreachedPasswordValidator();
        var result = await validator.ValidateAsync(null!, new AppUser(), pwd);
        Assert.False(result.Succeeded);
    }

    [Theory]
    [InlineData("Tr0ub4dour&3-correcthorse")]
    [InlineData("a-very-unlikely-passphrase-9271")]
    public async Task Strong_passwords_pass_the_breach_check(string pwd)
    {
        var validator = new BreachedPasswordValidator();
        var result = await validator.ValidateAsync(null!, new AppUser(), pwd);
        Assert.True(result.Succeeded);
    }
}
