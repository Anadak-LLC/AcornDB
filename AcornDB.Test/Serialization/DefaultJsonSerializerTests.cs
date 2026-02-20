using System;
using Xunit;
using AcornDB.Storage.Serialization;

namespace AcornDB.Test.Serialization
{
    public class DefaultJsonSerializerTests
    {
        private readonly DefaultJsonSerializer _serializer = new DefaultJsonSerializer();

        private class TestObject
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
        }

        [Fact]
        public void Serialize_And_Deserialize_Works_Correctly()
        {
            var obj = new TestObject { Id = 42, Name = "Acorn" };
            var json = _serializer.Serialize(obj);
            Assert.Contains("\"Id\":42", json);
            Assert.Contains("\"Name\":\"Acorn\"", json);

            var deserialized = _serializer.Deserialize<TestObject>(json);
            Assert.Equal(obj.Id, deserialized.Id);
            Assert.Equal(obj.Name, deserialized.Name);
        }

        [Fact]
        public void Deserialize_InvalidJson_Throws()
        {
            Assert.ThrowsAny<Exception>(() => _serializer.Deserialize<TestObject>("not a json"));
        }
    }
}

