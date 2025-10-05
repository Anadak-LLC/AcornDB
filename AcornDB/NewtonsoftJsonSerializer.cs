using Newtonsoft.Json;

namespace AcornDB
{
    public class NewtonsoftJsonSerializer : ISerializer
    {
        public string Serialize<T>(T obj)
        {
            return JsonConvert.SerializeObject(obj, Formatting.Indented);
        }

        public T Deserialize<T>(string data)
        {
            return JsonConvert.DeserializeObject<T>(data)!;
        }
    }
}
