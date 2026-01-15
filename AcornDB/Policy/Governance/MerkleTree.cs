using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace AcornDB.Policy.Governance;

/// <summary>
/// SHA-256 based binary Merkle tree for efficient proof generation.
/// Enables O(log n) proof of policy existence at a specific point.
/// </summary>
/// <remarks>
/// <para>
/// A Merkle tree is a binary tree where each leaf is a hash of data,
/// and each internal node is the hash of its two children. The root
/// hash represents all data in the tree.
/// </para>
/// <para>
/// Use case: Prove a specific policy seal exists in the log without
/// transmitting the entire chain. Proof size is O(log n) hashes.
/// </para>
/// </remarks>
public sealed class MerkleTree
{
    private readonly List<byte[]> _leaves = new();
    private readonly List<List<byte[]>> _levels = new();
    private byte[]? _rootHash;
    private bool _isDirty = true;

    /// <summary>
    /// Gets the root hash of the Merkle tree.
    /// Returns null if the tree is empty.
    /// </summary>
    public byte[]? RootHash
    {
        get
        {
            if (_isDirty) Rebuild();
            return _rootHash is null ? null : (byte[])_rootHash.Clone();
        }
    }

    /// <summary>
    /// Gets the number of leaves in the tree.
    /// </summary>
    public int LeafCount => _leaves.Count;

    /// <summary>
    /// Adds a leaf to the tree. The tree must be rebuilt before accessing RootHash or generating proofs.
    /// </summary>
    /// <param name="data">Raw data to hash and add as a leaf.</param>
    /// <returns>The index of the added leaf.</returns>
    public int AddLeaf(byte[] data)
    {
        if (data is null)
            throw new ArgumentException("Leaf data cannot be null.", nameof(data));

        var leafHash = SHA256.HashData(data);
        _leaves.Add(leafHash);
        _isDirty = true;
        return _leaves.Count - 1;
    }

    /// <summary>
    /// Adds a pre-computed hash as a leaf.
    /// </summary>
    /// <param name="hash">32-byte SHA-256 hash.</param>
    /// <returns>The index of the added leaf.</returns>
    public int AddLeafHash(byte[] hash)
    {
        if (hash is null)
            throw new ArgumentException("Leaf hash cannot be null.", nameof(hash));

        if (hash.Length != 32)
            throw new ArgumentException("Leaf hash must be 32 bytes.", nameof(hash));

        _leaves.Add((byte[])hash.Clone());
        _isDirty = true;
        return _leaves.Count - 1;
    }

    /// <summary>
    /// Generates a proof that a leaf at the given index exists in the tree.
    /// </summary>
    /// <param name="leafIndex">Index of the leaf to prove.</param>
    /// <returns>A MerkleProof that can verify the leaf against the root.</returns>
    /// <exception cref="ArgumentOutOfRangeException">If index is out of range.</exception>
    /// <exception cref="InvalidOperationException">If tree is empty.</exception>
    public MerkleProof GenerateProof(int leafIndex)
    {
        if (_isDirty) Rebuild();

        if (_leaves.Count == 0)
            throw new InvalidOperationException("Cannot generate proof for empty tree.");

        if (leafIndex < 0 || leafIndex >= _leaves.Count)
            throw new ArgumentOutOfRangeException(nameof(leafIndex), "Leaf index out of range.");

        var siblings = new List<(byte[] Hash, bool IsLeft)>();
        var currentIndex = leafIndex;

        for (var level = 0; level < _levels.Count - 1; level++)
        {
            var levelNodes = _levels[level];
            var siblingIndex = currentIndex % 2 == 0 ? currentIndex + 1 : currentIndex - 1;
            var isLeft = currentIndex % 2 == 1;

            if (siblingIndex < levelNodes.Count)
            {
                siblings.Add(((byte[])levelNodes[siblingIndex].Clone(), isLeft));
            }
            else
            {
                // Odd node count at this level - sibling is self (duplicated)
                siblings.Add(((byte[])levelNodes[currentIndex].Clone(), isLeft));
            }

            currentIndex /= 2;
        }

        return new MerkleProof(
            leafIndex,
            (byte[])_leaves[leafIndex].Clone(),
            siblings.AsReadOnly(),
            (byte[])_rootHash!.Clone()
        );
    }

    /// <summary>
    /// Verifies a proof against this tree's root.
    /// </summary>
    /// <param name="proof">The proof to verify.</param>
    /// <returns>True if the proof is valid for this tree's current root.</returns>
    public bool VerifyProof(MerkleProof proof)
    {
        if (proof is null)
            throw new ArgumentException("Proof cannot be null.", nameof(proof));

        if (_isDirty) Rebuild();

        if (_rootHash is null)
            return false;

        return proof.Verify() && HashesEqual(proof.RootHash, _rootHash);
    }

    /// <summary>
    /// Rebuilds the tree from the current leaves.
    /// </summary>
    private void Rebuild()
    {
        _levels.Clear();

        if (_leaves.Count == 0)
        {
            _rootHash = null;
            _isDirty = false;
            return;
        }

        // Level 0 is the leaves
        _levels.Add(new List<byte[]>(_leaves));

        var currentLevel = _levels[0];

        while (currentLevel.Count > 1)
        {
            var nextLevel = new List<byte[]>();

            for (var i = 0; i < currentLevel.Count; i += 2)
            {
                if (i + 1 < currentLevel.Count)
                {
                    nextLevel.Add(HashPair(currentLevel[i], currentLevel[i + 1]));
                }
                else
                {
                    // Odd number of nodes - promote the last one by hashing with itself
                    nextLevel.Add(HashPair(currentLevel[i], currentLevel[i]));
                }
            }

            _levels.Add(nextLevel);
            currentLevel = nextLevel;
        }

        _rootHash = currentLevel[0];
        _isDirty = false;
    }

    /// <summary>
    /// Computes SHA-256 hash of two hashes concatenated.
    /// </summary>
    /// <param name="left">Left 32-byte hash.</param>
    /// <param name="right">Right 32-byte hash.</param>
    /// <returns>SHA-256 hash of the concatenation.</returns>
    public static byte[] HashPair(byte[] left, byte[] right)
    {
        var combined = new byte[64];
        Buffer.BlockCopy(left, 0, combined, 0, 32);
        Buffer.BlockCopy(right, 0, combined, 32, 32);
        return SHA256.HashData(combined);
    }

    /// <summary>
    /// Constant-time comparison of two hashes.
    /// </summary>
    /// <param name="a">First hash.</param>
    /// <param name="b">Second hash.</param>
    /// <returns>True if equal.</returns>
    public static bool HashesEqual(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        return CryptographicOperations.FixedTimeEquals(a, b);
    }

    /// <summary>
    /// Creates a MerkleTree from PolicySeal signatures.
    /// </summary>
    /// <param name="seals">The policy seals to include.</param>
    /// <returns>A new MerkleTree containing the seal signatures.</returns>
    public static MerkleTree FromSeals(IEnumerable<PolicySeal> seals)
    {
        if (seals is null)
            throw new ArgumentException("Seals cannot be null.", nameof(seals));

        var tree = new MerkleTree();
        foreach (var seal in seals)
        {
            tree.AddLeafHash(seal.Signature);
        }
        return tree;
    }
}
