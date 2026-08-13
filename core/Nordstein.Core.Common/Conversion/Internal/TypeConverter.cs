using System.Collections;
using System.Globalization;
using System.Text.Json;

namespace Nordstein.Core.Common.Conversion.Internal;

/// <inheritdoc />
internal class TypeConverter : ITypeConverter
{
    private static readonly IFormatProvider DefaultFormatProvider = new CultureInfo("en");

    /// <inheritdoc />
    public object? ChangeType(object? value, Type targetType)
    {
        if (value == null)
        {
            return null;
        }

        // Handle nullable types
        var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;

        // If the value is already the correct type, return it
        if (value.GetType() == underlyingType)
        {
            return value;
        }
        
        if(value is string strValue && underlyingType == typeof(Guid))
        {
            return Guid.Parse(strValue);
        }

        // Handle JsonElement conversion (common when arguments come from JSON)
        if (value is JsonElement jsonElement)
        {
            value = ConvertJsonElement(jsonElement, underlyingType);
        }

        // Handle collection conversions (e.g., List<object> to List<Guid>)
        if (underlyingType.IsGenericType && value is IEnumerable enumerable)
        {
            var genericTypeDef = underlyingType.GetGenericTypeDefinition();
            
            // Handle List<T>, IList<T>, ICollection<T>, IEnumerable<T>
            if (genericTypeDef == typeof(List<>) || 
                genericTypeDef == typeof(IList<>) || 
                genericTypeDef == typeof(ICollection<>) || 
                genericTypeDef == typeof(IEnumerable<>))
            {
                var elementType = underlyingType.GetGenericArguments()[0];
                var listType = typeof(List<>).MakeGenericType(elementType);
                IList list = Activator.CreateInstance(listType) as IList
                    ?? throw new InvalidCastException($"Cannot create list of type {listType.Name}.");
                
                foreach (var item in enumerable)
                {
                    list.Add(ChangeType(item, elementType));
                }
                
                return list;
            }
        }

        if (underlyingType.IsEnum)
        {
            if (value is string enumString)
            {
                return Enum.Parse(underlyingType, enumString, ignoreCase: true);
            }

            if (int.TryParse(value?.ToString(), out int enumInt))
            {
                return Enum.ToObject(underlyingType, enumInt);
            }
        }

        // Use Convert.ChangeType for basic type conversions
        try
        {
            return Convert.ChangeType(value, underlyingType, DefaultFormatProvider);
        }
        catch (Exception ex)
        {
            throw new ArgumentException($"Cannot convert value '{value}' to type '{targetType.Name}'.", ex);
        }
    }

    private object? ConvertJsonElement(JsonElement jsonElement, Type targetType)
    {
        if (targetType.IsEnum)
        {
            targetType = typeof(string);
        }

        return Type.GetTypeCode(targetType) switch
        {
            TypeCode.Boolean => jsonElement.GetBoolean(),
            TypeCode.Byte => jsonElement.GetByte(),
            TypeCode.SByte => jsonElement.GetSByte(),
            TypeCode.Int16 => jsonElement.GetInt16(),
            TypeCode.UInt16 => jsonElement.GetUInt16(),
            TypeCode.Int32 => jsonElement.GetInt32(),
            TypeCode.UInt32 => jsonElement.GetUInt32(),
            TypeCode.Int64 => jsonElement.GetInt64(),
            TypeCode.UInt64 => jsonElement.GetUInt64(),
            TypeCode.Single => jsonElement.GetSingle(),
            TypeCode.Double => jsonElement.GetDouble(),
            TypeCode.Decimal => jsonElement.GetDecimal(),
            TypeCode.String => jsonElement.GetString(),
            _ when targetType == typeof(Guid) => jsonElement.GetGuid(),
            _ when targetType == typeof(DateTime) => jsonElement.GetDateTime(),
            _ when targetType == typeof(DateTimeOffset) => jsonElement.GetDateTimeOffset(),
            // For complex types, deserialize the JsonElement to the target type
            _ => jsonElement.Deserialize(targetType)
        };
    }
}