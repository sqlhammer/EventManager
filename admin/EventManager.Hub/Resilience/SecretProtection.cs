using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace EventManager.Hub.Resilience;

/// <summary>
/// Protects a secret at rest on the hub (D-U10-02, F1=B). The seam exists so
/// <see cref="HubCredentialStore"/> carries no platform dependency of its own and can be tested
/// anywhere; only the composition root names a concrete implementation.
///
/// Note on U10-CON-1: <c>EventManager.Hub</c> is a single project that is both library and host, so
/// the DPAPI package reference lives here rather than in a separate host project. The package builds
/// on every platform — <see cref="DpapiSecretProtector"/> only fails at runtime off Windows — so the
/// project still compiles anywhere, and a future library/host split is a project-file change rather
/// than a refactor.
/// </summary>
public interface ISecretProtector
{
    byte[] Protect(byte[] plaintext);
    byte[] Unprotect(byte[] protectedBytes);
}

/// <summary>
/// Windows DPAPI, <see cref="DataProtectionScope.CurrentUser"/> (ND-Q5=A). A copied hub database is
/// useless on another machine AND under another account on the same machine.
///
/// Caveat worth knowing before diagnosing: if the hub is run as a service under a different account
/// than the one that installed the credential, unprotection fails cleanly — it surfaces as "no usable
/// credential" rather than as corruption, and the remedy is to re-install under the running account.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiSecretProtector : ISecretProtector
{
    public byte[] Protect(byte[] plaintext) => ProtectedData.Protect(plaintext, null, DataProtectionScope.CurrentUser);

    public byte[] Unprotect(byte[] protectedBytes) =>
        ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
}

/// <summary>
/// No protection. For tests, and for non-Windows hosts where DPAPI is unavailable. Selecting this in
/// production would reopen the SECURITY-12 finding that F1=B closed, so the composition root logs a
/// warning when it is used.
/// </summary>
public sealed class PassthroughSecretProtector : ISecretProtector
{
    public byte[] Protect(byte[] plaintext) => plaintext;
    public byte[] Unprotect(byte[] protectedBytes) => protectedBytes;
}
