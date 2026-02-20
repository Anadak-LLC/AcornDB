using Newtonsoft.Json;

namespace AcornDB.Storage.Serialization
{
    /// <summary>
    /// JSON serializer implementation using Newtonsoft.Json.
    /// </summary>
    /// <remarks>
    /// NewtonsoftJsonSerializer is provided for compatibility and advanced scenarios. However, for most use cases,
    /// prefer <see cref="DefaultJsonSerializer"/> (System.Text.Json) for better performance and lower memory usage.
    /// </remarks>
    public class NewtonsoftJsonSerializer : ISerializer
    {
        /// <summary>
        /// Serializes an object to a JSON string using Newtonsoft.Json.
        /// </summary>
        /// <typeparam name="T">Type of the object to serialize.</typeparam>
        /// <param name="obj">Object to serialize.</param>
        /// <returns>JSON string representation of the object.</returns>
        public string Serialize<T>(T obj)
        {
            return JsonConvert.SerializeObject(obj, Formatting.Indented);
        }

        /// <summary>
        /// Deserializes a JSON string to an object of type T using Newtonsoft.Json.
        /// </summary>
        /// <typeparam name="T">Type to deserialize to.</typeparam>
        /// <param name="data">JSON string data.</param>
        /// <returns>Deserialized object of type T.</returns>
        public T Deserialize<T>(string data)
        {
            return JsonConvert.DeserializeObject<T>(data)!;
        }
    }
}
