namespace TLogin.Server.Security;

/// <summary>HWID gate (anti-cheat). The C++ flow is wired but effectively disabled; default auto-passes.</summary>
public interface IHwidValidator
{
    bool IsAllowed(string userId, string hwid, string macAddress, string pcName);
}

/// <summary>2FA "trusted device" gate. Default auto-passes (treats every device as trusted).</summary>
public interface ITwoFactorValidator
{
    bool IsTrusted(string userId, string hwid, string macAddress, string pcName);
}

/// <summary>Security-code email sender. Default is a no-op (the C++ SMTP path is commented out).</summary>
public interface IEmailSender
{
    Task SendSecurityCodeAsync(string email, string code, CancellationToken ct = default);
}

public sealed class AutoPassHwidValidator : IHwidValidator
{
    public bool IsAllowed(string userId, string hwid, string macAddress, string pcName) => true;
}

public sealed class AutoPassTwoFactorValidator : ITwoFactorValidator
{
    public bool IsTrusted(string userId, string hwid, string macAddress, string pcName) => true;
}

public sealed class NoOpEmailSender : IEmailSender
{
    public Task SendSecurityCodeAsync(string email, string code, CancellationToken ct = default) => Task.CompletedTask;
}
