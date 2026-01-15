using System;
using System.IO;
using AcornDB.Policy;
using AcornDB.Policy.Governance;
using AcornDB.Security;
using Xunit;

namespace AcornDB.Test.Policy;

/// <summary>
/// Unit tests for <see cref="FilePolicyLog"/>.
/// </summary>
public class FilePolicyLogTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _testFilePath;
    private readonly IPolicySigner _signer = new Sha256PolicySigner();

    public FilePolicyLogTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "AcornDB_FilePolicyLogTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDir);
        _testFilePath = Path.Combine(_testDir, "policy.log");
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, recursive: true);
    }

    [Fact]
    public void Constructor_LoadsExistingFile()
    {
        // Arrange - create log with entries
        using (var log1 = new FilePolicyLog(_testFilePath, _signer))
        {
            log1.Append(new TestPolicy("Policy1"), DateTime.UtcNow);
            log1.Append(new TestPolicy("Policy2"), DateTime.UtcNow.AddMinutes(1));
        }

        // Act - create new instance from same file
        using var log2 = new FilePolicyLog(_testFilePath, _signer);

        // Assert
        Assert.Equal(2, log2.Count);
        var seals = log2.GetAllSeals();
        Assert.Equal("Policy1", seals[0].Policy.Name);
        Assert.Equal("Policy2", seals[1].Policy.Name);
    }

    [Fact]
    public void Constructor_ValidatesChainOnLoad()
    {
        // Arrange
        using (var log = new FilePolicyLog(_testFilePath, _signer))
        {
            log.Append(new TestPolicy("Policy1"), DateTime.UtcNow);
        }

        // Act - reload
        using var log2 = new FilePolicyLog(_testFilePath, _signer);
        var result = log2.VerifyChain();

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Constructor_TruncatesAtCorruptedEntry()
    {
        // Arrange - create valid log then corrupt it
        using (var log = new FilePolicyLog(_testFilePath, _signer))
        {
            log.Append(new TestPolicy("Policy1"), DateTime.UtcNow);
            log.Append(new TestPolicy("Policy2"), DateTime.UtcNow.AddMinutes(1));
        }

        // Corrupt the file by appending invalid JSON
        File.AppendAllText(_testFilePath, "INVALID JSON LINE\n");

        // Act - reload (should truncate corrupted line)
        using var log2 = new FilePolicyLog(_testFilePath, _signer);

        // Assert - should have only 2 valid entries
        Assert.Equal(2, log2.Count);
    }

    [Fact]
    public void Append_PersistsToFile()
    {
        // Arrange & Act
        using (var log = new FilePolicyLog(_testFilePath, _signer))
        {
            log.Append(new TestPolicy("Persisted"), DateTime.UtcNow);
        }

        // Assert - file should contain the entry
        var fileContent = File.ReadAllText(_testFilePath);
        Assert.Contains("Persisted", fileContent);
    }

    [Fact]
    public void Append_SurvivesRestart()
    {
        // Arrange
        var effectiveAt = new DateTime(2026, 1, 14, 12, 0, 0, DateTimeKind.Utc);
        using (var log = new FilePolicyLog(_testFilePath, _signer))
        {
            log.Append(new TestPolicy("Survivor"), effectiveAt);
        }

        // Act - create new instance (simulates restart)
        using var log2 = new FilePolicyLog(_testFilePath, _signer);

        // Assert
        Assert.Equal(1, log2.Count);
        var policy = log2.GetPolicyAt(effectiveAt);
        Assert.NotNull(policy);
        Assert.Equal("Survivor", policy.Name);
    }

    [Fact]
    public void VerifyChain_WorksAfterReload()
    {
        // Arrange
        var time = DateTime.UtcNow;
        using (var log = new FilePolicyLog(_testFilePath, _signer))
        {
            log.Append(new TestPolicy("P1"), time);
            log.Append(new TestPolicy("P2"), time.AddMinutes(1));
            log.Append(new TestPolicy("P3"), time.AddMinutes(2));
        }

        // Act
        using var log2 = new FilePolicyLog(_testFilePath, _signer);
        var result = log2.VerifyChain();

        // Assert
        Assert.True(result.IsValid);
        Assert.Equal(3, log2.Count);
    }

    [Fact]
    public void CrashRecovery_PartialWrite_Recovers()
    {
        // Arrange - create valid entries
        using (var log = new FilePolicyLog(_testFilePath, _signer))
        {
            log.Append(new TestPolicy("Valid1"), DateTime.UtcNow);
            log.Append(new TestPolicy("Valid2"), DateTime.UtcNow.AddMinutes(1));
        }

        // Simulate partial/corrupt write at end (no newline, truncated)
        File.AppendAllText(_testFilePath, "{\"Signature\":\"AAAA\",\"Effectiv");

        // Act - recovery should truncate
        using var log2 = new FilePolicyLog(_testFilePath, _signer);

        // Assert - only valid entries remain
        Assert.Equal(2, log2.Count);
        Assert.True(log2.VerifyChain().IsValid);
    }

    [Fact]
    public void GetPolicyAt_ReturnsCorrectPolicy()
    {
        var time1 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var time2 = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        var time3 = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);

        using var log = new FilePolicyLog(_testFilePath, _signer);
        log.Append(new TestPolicy("Jan"), time1);
        log.Append(new TestPolicy("Feb"), time2);
        log.Append(new TestPolicy("Mar"), time3);

        // Query mid-January - should get Jan policy
        var policy = log.GetPolicyAt(new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal("Jan", policy!.Name);

        // Query mid-February - should get Feb policy
        policy = log.GetPolicyAt(new DateTime(2026, 2, 15, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal("Feb", policy!.Name);

        // Query April - should get Mar policy (latest)
        policy = log.GetPolicyAt(new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal("Mar", policy!.Name);
    }

    [Fact]
    public void GetPolicyAt_ReturnsNull_BeforeFirstPolicy()
    {
        var time = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        using var log = new FilePolicyLog(_testFilePath, _signer);
        log.Append(new TestPolicy("Future"), time);

        // Query before first policy
        var policy = log.GetPolicyAt(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        Assert.Null(policy);
    }

    [Fact]
    public void ChainIntegrity_DetectsTampering_WhenFileModified()
    {
        // Arrange - create valid chain
        using (var log = new FilePolicyLog(_testFilePath, _signer))
        {
            log.Append(new TestPolicy("A"), DateTime.UtcNow);
            log.Append(new TestPolicy("B"), DateTime.UtcNow.AddMinutes(1));
        }

        // Tamper with file - swap lines so PreviousHash won't match
        var lines = File.ReadAllLines(_testFilePath);
        (lines[0], lines[1]) = (lines[1], lines[0]);
        File.WriteAllLines(_testFilePath, lines);

        // Act - reload (chain will be broken due to hash mismatch)
        using var log2 = new FilePolicyLog(_testFilePath, _signer);

        // Assert - only first entry (which was originally second) should load
        // because its PreviousHash won't match the expected genesis hash
        // The loader truncates at the first invalid entry
        Assert.True(log2.Count < 2);
    }

    [Fact]
    public void ThreadSafety_ConcurrentReads_Succeed()
    {
        // Arrange - create log with entries
        var baseTime = DateTime.UtcNow;
        using var log = new FilePolicyLog(_testFilePath, _signer);

        // Add entries sequentially first
        for (var i = 0; i < 50; i++)
        {
            log.Append(new TestPolicy($"Policy{i}"), baseTime.AddMilliseconds(i));
        }

        // Act - concurrent reads
        var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();
        System.Threading.Tasks.Parallel.For(0, 100, i =>
        {
            try
            {
                var count = log.Count;
                var seals = log.GetAllSeals();
                var policy = log.GetPolicyAt(baseTime.AddMilliseconds(i % 50));
                var result = log.VerifyChain();

                Assert.Equal(50, count);
                Assert.Equal(50, seals.Count);
                Assert.NotNull(policy);
                Assert.True(result.IsValid);
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        });

        // Assert
        Assert.Empty(exceptions);
    }

    private sealed class TestPolicy : IPolicyRule
    {
        public TestPolicy(string name) => Name = name;
        public string Name { get; }
        public string Description => "Test policy for FilePolicyLog";
        public int Priority => 50;
        public PolicyEvaluationResult Evaluate<T>(T entity, PolicyContext context)
            => PolicyEvaluationResult.Success();
    }
}
