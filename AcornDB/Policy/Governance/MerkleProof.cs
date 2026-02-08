using System;
using System.Collections.Generic;

namespace AcornDB.Policy.Governance;

/// <summary>
/// A Merkle proof for verifying that a leaf exists in a Merkle tree.
/// Contains the sibling hashes needed to reconstruct the root.
/// </summary>
/// <remarks>
/// The proof consists of O(log n) hashes that, combined with the leaf,
/// allow verification against the known root without the full tree.
/// </remarks>
public sealed record MerkleProof
{
    private readonly byte[] _leafHash;
    private readonly byte[] _rootHash;

    /// <summary>
    /// The index of the leaf being proven.
    /// </summary>
    public int LeafIndex { get; }

    /// <summary>
    /// The hash of the leaf being proven.
    /// </summary>
    public byte[] LeafHash => (byte[])_leafHash.Clone();

    /// <summary>
    /// Sibling hashes from leaf to root. Each entry indicates if it's a left sibling.
    /// </summary>
    public IReadOnlyList<(byte[] Hash, bool IsLeft)> Siblings { get; }

    /// <summary>
    /// The expected root hash for verification.
    /// </summary>
    public byte[] RootHash => (byte[])_rootHash.Clone();

    /// <summary>
    /// Creates a new Merkle proof.
    /// </summary>
    /// <param name="leafIndex">Index of the leaf being proven.</param>
    /// <param name="leafHash">Hash of the leaf.</param>
    /// <param name="siblings">Sibling hashes from leaf to root.</param>
    /// <param name="rootHash">Expected root hash.</param>
    public MerkleProof(int leafIndex, byte[] leafHash, IReadOnlyList<(byte[] Hash, bool IsLeft)> siblings, byte[] rootHash)
    {
        LeafIndex = leafIndex;
        _leafHash = (byte[])leafHash.Clone();
        Siblings = siblings;
        _rootHash = (byte[])rootHash.Clone();
    }

    /// <summary>
    /// Verifies this proof against the expected root hash.
    /// </summary>
    /// <returns>True if the proof is valid.</returns>
    public bool Verify()
    {
        var currentHash = (byte[])_leafHash.Clone();

        foreach (var (siblingHash, isLeft) in Siblings)
        {
            currentHash = isLeft
                ? MerkleTree.HashPair(siblingHash, currentHash)
                : MerkleTree.HashPair(currentHash, siblingHash);
        }

        return MerkleTree.HashesEqual(currentHash, _rootHash);
    }
}
