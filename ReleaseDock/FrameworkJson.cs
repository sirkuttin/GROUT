using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Web.Script.Serialization;

namespace ReleaseDock.Json;

[AttributeUsage(AttributeTargets.Property)]
public sealed class JsonIgnoreAttribute : Attribute { }
public sealed class JsonException : Exception { public JsonException(string message, Exception? inner = null) : base(message, inner) { } }
public sealed class JsonSerializerOptions { public bool WriteIndented { get; set; } }

// Use Windows' JSON parser; map only public data properties, never runtime type names.
// ISO dates and constructor binding preserve the existing GROUT data/plan format.
public static class JsonSerializer
{
    internal static object? Parse(string text)
    {
        try { return new JavaScriptSerializer { MaxJsonLength = 32 * 1024 * 1024, RecursionLimit = 64 }.DeserializeObject(text); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { throw new JsonException("Invalid JSON data.", ex); }
    }
    public static string Serialize<T>(T value, JsonSerializerOptions? options = null) => new JavaScriptSerializer { MaxJsonLength = 32 * 1024 * 1024, RecursionLimit = 100 }.Serialize(Data(value));
    private static object? Data(object? value)
    {
        if (value == null || value is string or bool || value.GetType().IsPrimitive || value is decimal) return value;
        if (value is DateTimeOffset offset) return offset.ToString("O", CultureInfo.InvariantCulture);
        if (value is DateTime date) return date.ToString("O", CultureInfo.InvariantCulture);
        if (value is IDictionary dictionary)
        {
            var result = new Dictionary<string, object?>();
            foreach (DictionaryEntry entry in dictionary) result.Add((string)entry.Key, Data(entry.Value));
            return result;
        }
        if (value is IEnumerable sequence) return sequence.Cast<object?>().Select(Data).ToArray();
        return value.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0 && !p.IsDefined(typeof(JsonIgnoreAttribute), true))
            .ToDictionary(p => p.Name, p => Data(p.GetValue(value)));
    }
    public static T? Deserialize<T>(string text)
    {
        try { return (T?)Bind(Parse(text), typeof(T)); }
        catch (Exception ex) when (ex is ArgumentException or FormatException or InvalidCastException or TargetInvocationException or OverflowException) { throw new JsonException("JSON data has an invalid value.", ex); }
    }
    private static object? Bind(object? value, Type type)
    {
        if (value == null) return type.IsValueType && Nullable.GetUnderlyingType(type) == null ? Activator.CreateInstance(type) : null;
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type == typeof(string)) return value is string ? value : throw new JsonException("Expected JSON string.");
        if (type == typeof(DateTimeOffset)) return DateTimeOffset.Parse((string)value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        if (type == typeof(DateTime)) return DateTime.Parse((string)value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        if (type == typeof(bool)) return value is bool ? value : throw new JsonException("Expected JSON boolean.");
        if (type.IsPrimitive || type == typeof(decimal))
        {
            if (value is string or bool) throw new JsonException("Expected JSON number.");
            var number = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
            if (type != typeof(float) && type != typeof(double) && type != typeof(decimal) && number != decimal.Truncate(number)) throw new JsonException("Expected JSON integer.");
            return Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
        }
        if (type.IsArray) { var items = ((IEnumerable)value).Cast<object?>().ToArray(); var array = Array.CreateInstance(type.GetElementType()!, items.Length); for (var i = 0; i < items.Length; i++) array.SetValue(Bind(items[i], type.GetElementType()!), i); return array; }
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
        {
            var dictionary = (IDictionary)Activator.CreateInstance(type)!; var args = type.GetGenericArguments();
            foreach (var item in (IDictionary<string, object>)value) dictionary.Add(item.Key, Bind(item.Value, args[1]));
            return dictionary;
        }
        if (type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(List<>) || type.GetGenericTypeDefinition() == typeof(IReadOnlyList<>)))
        {
            var element = type.GetGenericArguments()[0]; var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(element))!;
            foreach (var item in (IEnumerable)value) list.Add(Bind(item, element));
            return list;
        }
        var fields = value as IDictionary<string, object> ?? throw new JsonException("Expected JSON object.");
        var ctor = type.GetConstructor(Type.EmptyTypes);
        object target;
        if (ctor != null) target = ctor.Invoke(null);
        else
        {
            ctor = type.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
            target = ctor.Invoke(ctor.GetParameters().Select(p => { var key = fields.Keys.FirstOrDefault(k => k.Equals(p.Name, StringComparison.OrdinalIgnoreCase)); return key != null ? Bind(fields[key], p.ParameterType) : p.HasDefaultValue ? p.DefaultValue : Bind(null, p.ParameterType); }).ToArray());
        }
        foreach (var property in type.GetProperties().Where(p => p.CanWrite && !p.IsDefined(typeof(JsonIgnoreAttribute), true)))
            if (fields.TryGetValue(property.Name, out var item)) property.SetValue(target, Bind(item, property.PropertyType));
        return target;
    }
}
public enum JsonValueKind { Undefined, Object, Array, String, Number, True, False, Null }
public readonly struct JsonElement
{
    private readonly object? value;
    private readonly bool exists;
    internal JsonElement(object? value) { this.value = value; exists = true; }
    public JsonValueKind ValueKind => !exists ? JsonValueKind.Undefined : value == null ? JsonValueKind.Null : value is string ? JsonValueKind.String : value is bool boolean ? boolean ? JsonValueKind.True : JsonValueKind.False : value is IDictionary ? JsonValueKind.Object : value is IEnumerable ? JsonValueKind.Array : JsonValueKind.Number;
    public bool TryGetProperty(string name, out JsonElement element) { if (value is IDictionary<string, object> dictionary && dictionary.TryGetValue(name, out var item)) { element = new(item); return true; } element = default; return false; }
    public JsonElement GetProperty(string name) => TryGetProperty(name, out var result) ? result : throw new JsonException("Missing JSON property: " + name);
    public IEnumerable<JsonElement> EnumerateArray() => ValueKind == JsonValueKind.Array ? ((IEnumerable)value!).Cast<object?>().Select(item => new JsonElement(item)) : throw new JsonException("Expected JSON array.");
    public string? GetString() => value == null ? null : value as string ?? throw new JsonException("Expected JSON string.");
    public bool GetBoolean() => value is bool result ? result : throw new JsonException("Expected JSON boolean.");
    public long GetInt64()
    {
        if (ValueKind != JsonValueKind.Number) throw new JsonException("Expected JSON integer.");
        var number = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
        if (number != decimal.Truncate(number)) throw new JsonException("Expected JSON integer.");
        return checked((long)number);
    }
    public int GetInt32() => checked((int)GetInt64());
}
public sealed class JsonDocument : IDisposable
{
    public JsonElement RootElement { get; }
    private JsonDocument(object? value) { RootElement = new(value); }
    public static JsonDocument Parse(string text) => new(JsonSerializer.Parse(text));
    public static JsonDocument Parse(Stream stream) { using var reader = new StreamReader(stream); return Parse(reader.ReadToEnd()); }
    public void Dispose() { }
}
