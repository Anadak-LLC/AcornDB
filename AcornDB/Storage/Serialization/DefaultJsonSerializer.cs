using System.Text.Json;

namespace AcornDB.Storage.Serialization;

/// <summary>
/// Default JSON serializer implementation using System.Text.Json.
/// </summary>
public class DefaultJsonSerializer : ISerializer
{
    /// <summary>
    /// Serializes an object to a JSON string.
    /// </summary>
    /// <typeparam name="T">Type of the object to serialize.</typeparam>
    /// <param name="obj">Object to serialize.</param>
    /// <returns>JSON string representation of the object.</returns>
    public string Serialize<T>(T obj)
    {
        return JsonSerializer.Serialize(obj);
    }

    /// <summary>
    /// Deserializes a JSON string to an object of type T.
    /// </summary>
    /// <typeparam name="T">Type to deserialize to.</typeparam>
    /// <param name="data">JSON string data.</param>
    /// <returns>Deserialized object of type T.</returns>
    public T Deserialize<T>(string data)
    {
        return JsonSerializer.Deserialize<T>(data)!;
    }
}