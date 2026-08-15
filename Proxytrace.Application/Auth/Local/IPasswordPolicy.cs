namespace Proxytrace.Application.Auth.Local;

/// <summary>
/// Represents a password policy.
/// </summary>
public interface IPasswordPolicy
{
    PasswordValidationResult Validate(string password);
}

/// <summary>
/// Encapsulates the result of a password validation operation.
/// </summary>
public sealed record PasswordValidationResult(
    bool IsValid,
    IReadOnlyList<string> Errors)
{
    /// <summary>
    /// Ok.
    /// </summary>
    public static PasswordValidationResult Ok() => new(true, []);
    /// <summary>
    /// Fail.
    /// </summary>
    public static PasswordValidationResult Fail(params string[] errors) => new(false, errors);
}
