namespace Proxytrace.Application.Auth.Local.Internal;

internal sealed class PasswordPolicy : IPasswordPolicy
{
    /// <summary>
    /// Validates.
    /// </summary>
    public PasswordValidationResult Validate(string password)
    {
        var errors = new List<string>();
        if (string.IsNullOrEmpty(password) || password.Length < 8)
            errors.Add("Password must be at least 8 characters.");
        if (!password.Any(char.IsLower))
            errors.Add("Password must contain a lowercase letter.");
        if (!password.Any(char.IsUpper))
            errors.Add("Password must contain an uppercase letter.");
        if (password.All(char.IsLetterOrDigit))
            errors.Add("Password must contain a special character.");
        return errors.Count == 0 
            ? PasswordValidationResult.Ok() 
            : new(false, errors);
    }
}
