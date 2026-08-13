namespace Nordstein.Core.Common.Conversion;

/// <summary>
/// Service responsible for converting values to the types expected by tool parameters
/// </summary>
public interface ITypeConverter
{
    /// <summary>
    /// Convert the given value to the specified target type
    /// </summary>
    object? ChangeType(object? value, Type targetType);
}