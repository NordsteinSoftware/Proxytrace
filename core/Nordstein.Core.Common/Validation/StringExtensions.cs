using System.Diagnostics.CodeAnalysis;

namespace Nordstein.Core.Common.Validation;

public static class StringExtensions
{
    public static bool NotNullOrWhiteSpace([NotNullWhen(true)] this string? value)
        => !string.IsNullOrWhiteSpace(value);
    
    public static bool NullOrWhiteSpace([NotNullWhen(false)] this string? value)
        => !value.NotNullOrWhiteSpace();
}