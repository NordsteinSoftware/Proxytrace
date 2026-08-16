namespace Proxytrace.Application.Auth.Local;

/// <summary>
/// Validates a candidate password against the installation's strength requirements before it is hashed and stored.
/// </summary>
public interface IPasswordPolicy
{
    /// <summary>
    /// Returns a result indicating whether the password satisfies all policy rules, with one error message per violated rule.
    /// </summary>
    PasswordValidationResult Validate(string password);
}

/// <summary>
/// Outcome of a password policy check: a pass/fail flag plus the list of violated rule descriptions.
/// </summary>
public sealed record PasswordValidationResult(
    bool IsValid,
    IReadOnlyList<string> Errors)
{
    /// <summary>
    /// Returns a passing result with no errors.
    /// </summary>
    public static PasswordValidationResult Ok() => new(true, []);

    /// <summary>
    /// Returns a failing result carrying the supplied rule-violation messages.
    /// </summary>
    public static PasswordValidationResult Fail(params string[] errors) => new(false, errors);
}
