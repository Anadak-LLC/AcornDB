using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using AcornDB.Policy;
using AcornDB.Policy.Governance;
using AcornDB.Security;
using Xunit;

namespace AcornDB.Test.Policy;

/// <summary>
/// Unit tests for <see cref="MerkleTree"/> and <see cref="MerkleProof"/>.
/// </summary>
public class MerkleTreeTests
{
    [Fact]
    public void EmptyTree_HasNullRootHash()
    {
        var tree = new MerkleTree();

        Assert.Null(tree.RootHash);
        Assert.Equal(0, tree.LeafCount);
    }

    [Fact]
    public void SingleLeaf_RootIsLeafHash()
    {
        var tree = new MerkleTree();
        var data = Encoding.UTF8.GetBytes("Single leaf");
        tree.AddLeaf(data);

        var expectedHash = SHA256.HashData(data);

        Assert.NotNull(tree.RootHash);
        Assert.Equal(expectedHash, tree.RootHash);
        Assert.Equal(1, tree.LeafCount);
    }

    [Fact]
    public void TwoLeaves_RootIsCombinedHash()
    {
        var tree = new MerkleTree();
        var data1 = Encoding.UTF8.GetBytes("Leaf 1");
        var data2 = Encoding.UTF8.GetBytes("Leaf 2");
        tree.AddLeaf(data1);
        tree.AddLeaf(data2);

        var hash1 = SHA256.HashData(data1);
        var hash2 = SHA256.HashData(data2);
        var expectedRoot = MerkleTree.HashPair(hash1, hash2);

        Assert.Equal(expectedRoot, tree.RootHash);
        Assert.Equal(2, tree.LeafCount);
    }

    [Fact]
    public void ThreeLeaves_HandlesOddCount()
    {
        var tree = new MerkleTree();
        tree.AddLeaf(Encoding.UTF8.GetBytes("A"));
        tree.AddLeaf(Encoding.UTF8.GetBytes("B"));
        tree.AddLeaf(Encoding.UTF8.GetBytes("C"));

        Assert.NotNull(tree.RootHash);
        Assert.Equal(3, tree.LeafCount);
    }

    [Fact]
    public void AddLeaf_ReturnsCorrectIndex()
    {
        var tree = new MerkleTree();

        var idx0 = tree.AddLeaf(Encoding.UTF8.GetBytes("A"));
        var idx1 = tree.AddLeaf(Encoding.UTF8.GetBytes("B"));
        var idx2 = tree.AddLeaf(Encoding.UTF8.GetBytes("C"));

        Assert.Equal(0, idx0);
        Assert.Equal(1, idx1);
        Assert.Equal(2, idx2);
    }

    [Fact]
    public void AddLeafHash_Works()
    {
        var tree = new MerkleTree();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("Data"));

        var idx = tree.AddLeafHash(hash);

        Assert.Equal(0, idx);
        Assert.Equal(hash, tree.RootHash);
    }

    [Fact]
    public void AddLeaf_ThrowsOnNull()
    {
        var tree = new MerkleTree();

        Assert.Throws<ArgumentException>(() => tree.AddLeaf(null!));
    }

    [Fact]
    public void AddLeafHash_ThrowsOnNull()
    {
        var tree = new MerkleTree();

        Assert.Throws<ArgumentException>(() => tree.AddLeafHash(null!));
    }

    [Fact]
    public void AddLeafHash_ThrowsOnWrongLength()
    {
        var tree = new MerkleTree();

        Assert.Throws<ArgumentException>(() => tree.AddLeafHash(new byte[16]));
    }

    [Fact]
    public void GenerateProof_ThrowsOnEmptyTree()
    {
        var tree = new MerkleTree();

        Assert.Throws<InvalidOperationException>(() => tree.GenerateProof(0));
    }

    [Fact]
    public void GenerateProof_ThrowsOnOutOfRangeIndex()
    {
        var tree = new MerkleTree();
        tree.AddLeaf(Encoding.UTF8.GetBytes("A"));

        Assert.Throws<ArgumentOutOfRangeException>(() => tree.GenerateProof(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => tree.GenerateProof(1));
    }

    [Fact]
    public void GenerateProof_SingleLeaf_EmptySiblings()
    {
        var tree = new MerkleTree();
        tree.AddLeaf(Encoding.UTF8.GetBytes("Only leaf"));

        var proof = tree.GenerateProof(0);

        Assert.Equal(0, proof.LeafIndex);
        Assert.Empty(proof.Siblings);
        Assert.Equal(tree.RootHash, proof.RootHash);
    }

    [Fact]
    public void GenerateProof_TwoLeaves_OneSibling()
    {
        var tree = new MerkleTree();
        tree.AddLeaf(Encoding.UTF8.GetBytes("A"));
        tree.AddLeaf(Encoding.UTF8.GetBytes("B"));

        var proof0 = tree.GenerateProof(0);
        var proof1 = tree.GenerateProof(1);

        Assert.Single(proof0.Siblings);
        Assert.Single(proof1.Siblings);
    }

    [Fact]
    public void Proof_Verify_ReturnsTrue_ForValidProof()
    {
        var tree = new MerkleTree();
        tree.AddLeaf(Encoding.UTF8.GetBytes("A"));
        tree.AddLeaf(Encoding.UTF8.GetBytes("B"));
        tree.AddLeaf(Encoding.UTF8.GetBytes("C"));
        tree.AddLeaf(Encoding.UTF8.GetBytes("D"));

        for (var i = 0; i < 4; i++)
        {
            var proof = tree.GenerateProof(i);
            Assert.True(proof.Verify(), $"Proof for leaf {i} should be valid");
        }
    }

    [Fact]
    public void Proof_Verify_ReturnsFalse_ForTamperedLeaf()
    {
        var tree = new MerkleTree();
        tree.AddLeaf(Encoding.UTF8.GetBytes("A"));
        tree.AddLeaf(Encoding.UTF8.GetBytes("B"));

        var proof = tree.GenerateProof(0);

        // Tamper with the proof by creating a fake one with wrong leaf hash
        var tamperedProof = new MerkleProof(
            proof.LeafIndex,
            SHA256.HashData(Encoding.UTF8.GetBytes("FAKE")),
            proof.Siblings,
            proof.RootHash
        );

        Assert.False(tamperedProof.Verify());
    }

    [Fact]
    public void VerifyProof_ValidProof_ReturnsTrue()
    {
        var tree = new MerkleTree();
        tree.AddLeaf(Encoding.UTF8.GetBytes("A"));
        tree.AddLeaf(Encoding.UTF8.GetBytes("B"));

        var proof = tree.GenerateProof(1);

        Assert.True(tree.VerifyProof(proof));
    }

    [Fact]
    public void VerifyProof_ThrowsOnNull()
    {
        var tree = new MerkleTree();
        tree.AddLeaf(Encoding.UTF8.GetBytes("A"));

        Assert.Throws<ArgumentException>(() => tree.VerifyProof(null!));
    }

    [Fact]
    public void VerifyProof_ReturnsFalse_ForProofFromDifferentTree()
    {
        var tree1 = new MerkleTree();
        tree1.AddLeaf(Encoding.UTF8.GetBytes("A"));
        tree1.AddLeaf(Encoding.UTF8.GetBytes("B"));

        var tree2 = new MerkleTree();
        tree2.AddLeaf(Encoding.UTF8.GetBytes("X"));
        tree2.AddLeaf(Encoding.UTF8.GetBytes("Y"));

        var proof = tree1.GenerateProof(0);

        Assert.False(tree2.VerifyProof(proof));
    }

    [Fact]
    public void LargeTree_ProofsAreValid()
    {
        var tree = new MerkleTree();
        for (var i = 0; i < 100; i++)
        {
            tree.AddLeaf(Encoding.UTF8.GetBytes($"Leaf {i}"));
        }

        // Verify proofs for several leaves
        foreach (var idx in new[] { 0, 1, 49, 50, 99 })
        {
            var proof = tree.GenerateProof(idx);
            Assert.True(proof.Verify(), $"Proof for leaf {idx} should be valid");
            Assert.True(tree.VerifyProof(proof), $"Tree should verify proof for leaf {idx}");
        }
    }

    [Fact]
    public void FromSeals_CreatesTreeFromPolicySeals()
    {
        var signer = new Sha256PolicySigner();
        var policy = new TestPolicy("Test");

        var seal1 = PolicySeal.Create(policy, DateTime.UtcNow, null, signer);
        var seal2 = PolicySeal.Create(policy, DateTime.UtcNow.AddMinutes(1), seal1, signer);

        var tree = MerkleTree.FromSeals(new[] { seal1, seal2 });

        Assert.Equal(2, tree.LeafCount);
        Assert.NotNull(tree.RootHash);
    }

    [Fact]
    public void FromSeals_ThrowsOnNull()
    {
        Assert.Throws<ArgumentException>(() => MerkleTree.FromSeals(null!));
    }

    [Fact]
    public void RootHash_ReturnsDefensiveCopy()
    {
        var tree = new MerkleTree();
        tree.AddLeaf(Encoding.UTF8.GetBytes("A"));

        var root1 = tree.RootHash;
        var root2 = tree.RootHash;

        Assert.NotSame(root1, root2);
        Assert.Equal(root1, root2);
    }

    [Fact]
    public void ProofSiblings_AreDefensiveCopies()
    {
        var tree = new MerkleTree();
        tree.AddLeaf(Encoding.UTF8.GetBytes("A"));
        tree.AddLeaf(Encoding.UTF8.GetBytes("B"));

        var proof = tree.GenerateProof(0);
        var siblingHash = proof.Siblings[0].Hash;

        // Modify the sibling hash
        siblingHash[0] ^= 0xFF;

        // Original proof should still verify
        var proof2 = tree.GenerateProof(0);
        Assert.NotEqual(siblingHash, proof2.Siblings[0].Hash);
    }

    // Helper class for testing
    private sealed class TestPolicy : IPolicyRule
    {
        public string Name { get; }
        public string Description => "Test policy";
        public int Priority => 1;

        public TestPolicy(string name) => Name = name;

        public PolicyEvaluationResult Evaluate<T>(T entity, PolicyContext context) =>
            PolicyEvaluationResult.Success();
    }
}
