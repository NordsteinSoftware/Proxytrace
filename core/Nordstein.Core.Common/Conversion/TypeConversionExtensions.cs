namespace Nordstein.Core.Common.Conversion;

/// <summary>
/// Extensions for type conversion, providing a method to convert an object to a specified type with error handling. This class is designed
/// </summary>
public static class TypeConversionExtensions
{
    /// <summary>
    /// Converts an object to a specified type, throwing an exception if the conversion fails. This method is useful for ensuring that an object is of a specific type before using it, and provides a clear error message if the conversion is not possible.
    /// </summary>
    public static T As<T>(this object obj) where T : class 
        => obj as T 
            ?? throw new InvalidCastException($"Cannot cast object of type {obj.GetType().FullName} to type {typeof(T).FullName}");
}