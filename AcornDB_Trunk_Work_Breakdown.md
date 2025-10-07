# 🛠️ AcornDB Trunk Work Breakdown (as of 2025-10-07)

Ranked by highest blend of 🍯 Nuttiness (Cool+Useful) and 🛠️ Feasibility.

|    | Task              | Description                                                                                      |   NuttyScore |   Feasibility |   Priority |
|---:|:------------------|:-------------------------------------------------------------------------------------------------|-------------:|--------------:|-----------:|
|  0 | TrunkCapabilities | Create unified capability enum + behaviors for Trunks.                                           |           10 |            10 |       10   |
|  1 | GitHubTrunk       | Use GitHub/Git as a backing store. Track changes as commits. Squash, PR for conflict resolution. |           10 |             6 |        8.4 |
|  2 | S3Trunk           | Support S3 or compatible object store (MinIO) as a remote Trunk.                                 |            8 |             9 |        8.4 |
|  3 | TrunkRegistry     | Dynamic registration/discovery of available Trunk types.                                         |            8 |             9 |        8.4 |
|  4 | RDBMSTrunk        | Map Tree<T> to a SQL table. Listen to table changes, sync to Grove.                              |            9 |             7 |        8.2 |
|  5 | NoSQLTrunk        | MongoDB/Cosmos DB connector Trunk. Bi-directional sync, TTL, ID mapping.                         |            8 |             8 |        8   |
|  6 | BTreeTrunk        | Append-only persistent store with B-Tree indexing. Full on-disk DB.                              |            9 |             6 |        7.8 |
|  7 | ForestMesh        | Support multiple Groves forming a Forest with mesh topology sync.                                |           10 |             4 |        7.6 |
|  8 | ObjectMeshTrunk   | P2P object mesh trunk with auto redundancy and gossip sync.                                      |           10 |             4 |        7.6 |
|  9 | BlockchainTrunk   | Immutable log-based nut shelling with verifiability on-chain.                                    |            9 |             3 |        6.6 |
| 10 | MobileTrunk       | Local SQLite/Realm Trunk for mobile (Xamarin/Maui/Uno).                                          |            7 |             6 |        6.6 |
| 11 | PackageTrunk      | Support bundled Trees as acornpkg files. Share via CLI.                                          |            6 |             7 |        6.4 |
| 12 | ParquetTrunk      | Columnar Trunk for analytical use cases. Stream/batch write support.                             |            7 |             5 |        6.2 |