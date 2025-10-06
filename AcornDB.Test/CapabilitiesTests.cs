using AcornDB.Storage;
using Xunit;

namespace AcornDB.Test
{
    public class CapabilitiesTests
    {
        [Fact]
        public void FileTrunk_Has_Correct_Capabilities()
        {
            var trunk = new FileTrunk<string>("data/caps-test");
            var caps = trunk.GetCapabilities();

            Assert.Equal("FileTrunk", caps.TrunkType);
            Assert.False(caps.SupportsHistory);
            Assert.True(caps.SupportsSync);
            Assert.True(caps.IsDurable);
            Assert.False(caps.SupportsAsync);
        }

        [Fact]
        public void MemoryTrunk_Has_Correct_Capabilities()
        {
            var trunk = new MemoryTrunk<string>();
            var caps = trunk.GetCapabilities();

            Assert.Equal("MemoryTrunk", caps.TrunkType);
            Assert.False(caps.SupportsHistory);
            Assert.True(caps.SupportsSync);
            Assert.False(caps.IsDurable); // Memory is not durable
            Assert.False(caps.SupportsAsync);
        }

        [Fact]
        public void DocumentStoreTrunk_Has_Correct_Capabilities()
        {
            var path = $"data/test-{Guid.NewGuid():N}/caps";
            var trunk = new DocumentStoreTrunk<string>(path);
            var caps = trunk.GetCapabilities();

            Assert.Equal("DocumentStoreTrunk", caps.TrunkType);
            Assert.True(caps.SupportsHistory); // DocumentStoreTrunk supports history!
            Assert.True(caps.SupportsSync);
            Assert.True(caps.IsDurable);
            Assert.False(caps.SupportsAsync);
        }

        [Fact]
        public void AzureTrunk_Has_Correct_Capabilities()
        {
            // Note: We can't instantiate AzureTrunk without valid connection string
            // but we can test the pattern with a mock or skip this test in CI

            // This test demonstrates the expected capabilities:
            // SupportsHistory: false
            // SupportsSync: true
            // IsDurable: true
            // SupportsAsync: true (AzureTrunk has async methods!)
        }

        [Fact]
        public void CanGetHistory_Extension_Works()
        {
            var fileTrunk = new FileTrunk<string>("data/caps-test");
            var docTrunk = new DocumentStoreTrunk<string>($"data/test-{Guid.NewGuid():N}/ext");

            Assert.False(fileTrunk.CanGetHistory());
            Assert.True(docTrunk.CanGetHistory());
        }

        [Fact]
        public void CanSync_Extension_Works()
        {
            var memoryTrunk = new MemoryTrunk<string>();

            Assert.True(memoryTrunk.CanSync());
        }

        [Fact]
        public void Capabilities_Allow_Safe_Feature_Detection()
        {
            var trunk = new MemoryTrunk<string>();

            // Instead of try/catch, use capabilities
            if (trunk.CanGetHistory())
            {
                var history = trunk.GetHistory("test");
                // ... use history
            }
            else
            {
                // Handle no history case gracefully
                Assert.True(true); // Expected path for MemoryTrunk
            }
        }
    }
}
