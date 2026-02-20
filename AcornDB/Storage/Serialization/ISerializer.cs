namespace AcornDB.Storage.Serialization
{
    /// <summary>
    /// Defines methods for serializing and deserializing objects to and from JSON.
    /// </summary>
    public interface ISerializer
    {
        /// <summary>
        /// Serializes an object to a JSON string.
        /// </summary>
        /// <typeparam name="T">Type of the object to serialize.</typeparam>
        /// <param name="obj">Object to serialize.</param>
        /// <returns>JSON string representation of the object.</returns>
        string Serialize<T>(T obj);

        /// <summary>
        /// Deserializes a JSON string to an object of type T.
        /// </summary>
        /// <typeparam name="T">Type to deserialize to.</typeparam>
        /// <param name="data">JSON string data.</param>
        /// <returns>Deserialized object of type T.</returns>
        T Deserialize<T>(string data);
    }
}
