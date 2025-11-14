using System;
using AcornDB.Storage;
using Xunit;

namespace AcornDB.Test
{
    public class TrunkCapabilitiesTests
    {
        [Fact]
        public void MemoryTrunk_Has_Correct_Capabilities()
        {
            var trunk = new MemoryTrunk<string>();
            var caps = trunk.Capabilities;

            Assert.NotNull(caps);
            Assert.False(caps.SupportsHistory);
            Assert.True(caps.SupportsSync);
            Assert.False(caps.IsDurable);
            Assert.False(caps.SupportsAsync);
            Assert.Equal("MemoryTrunk", caps.TrunkType);
        }

        [Fact]
        public void FileTrunk_Has_Correct_Capabilities()
        {
            var trunk = new FileTrunk<string>($"data/test-{Guid.NewGuid():N}");
            var caps = trunk.Capabilities;

            Assert.NotNull(caps);
            Assert.False(caps.SupportsHistory);
            Assert.True(caps.SupportsSync);
            Assert.True(caps.IsDurable);
            Assert.False(caps.SupportsAsync);
            Assert.Equal("FileTrunk", caps.TrunkType);
        }

        [Fact]
        public void DocumentStoreTrunk_Has_Correct_Capabilities()
        {
            var trunk = new DocumentStoreTrunk<string>($"data/test-{Guid.NewGuid():N}");
            var caps = trunk.Capabilities;

            Assert.NotNull(caps);
            Assert.True(caps.SupportsHistory);
            Assert.True(caps.SupportsSync);
            Assert.True(caps.IsDurable);
            Assert.False(caps.SupportsAsync);
            Assert.Equal("DocumentStoreTrunk", caps.TrunkType);

            trunk.Dispose();
        }

        [Fact]
        public void BTreeTrunk_Has_Correct_Capabilities()
        {
            var trunk = new BTreeTrunk<string>($"data/test-{Guid.NewGuid():N}");
            var caps = trunk.Capabilities;

            Assert.NotNull(caps);
            Assert.False(caps.SupportsHistory);
            Assert.True(caps.SupportsSync);
            Assert.True(caps.IsDurable);
            Assert.False(caps.SupportsAsync);
            Assert.Equal("BTreeTrunk", caps.TrunkType);
        }

        [Fact]
        public void CachedTrunk_Reports_Backing_Store_Capabilities()
        {
            var backingStore = new FileTrunk<string>($"data/test-{Guid.NewGuid():N}");
            var cached = new CachedTrunk<string>(backingStore);
            var caps = cached.Capabilities;

            Assert.NotNull(caps);
            Assert.Contains("CachedTrunk", caps.TrunkType);
            Assert.Contains("FileTrunk", caps.TrunkType);
        }

        [Fact]
        public void ResilientTrunk_Reports_Primary_Trunk_Capabilities()
        {
            var primary = new MemoryTrunk<string>();
            var resilient = new ResilientTrunk<string>(primary);
            var caps = resilient.Capabilities;

            Assert.NotNull(caps);
            Assert.Contains("ResilientTrunk", caps.TrunkType);
            Assert.Contains("MemoryTrunk", caps.TrunkType);
        }

        [Fact]
        public void NearFarTrunk_Reports_Combined_Capabilities()
        {
            var near = new MemoryTrunk<string>();
            var far = new FileTrunk<string>($"data/test-{Guid.NewGuid():N}");
            var backingStore = new MemoryTrunk<string>();
            var nearFar = new NearFarTrunk<string>(near, far, backingStore);
            var caps = nearFar.Capabilities;

            Assert.NotNull(caps);
            Assert.Contains("NearFarTrunk", caps.TrunkType);
        }
    }
}
