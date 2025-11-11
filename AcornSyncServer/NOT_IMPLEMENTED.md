# AcornSyncServer - Not Implemented

This project is a **placeholder** for future server-side sync infrastructure.

## Status: NOT IMPLEMENTED

The following features are **planned but not yet implemented**:

- Hardwood server (centralized sync coordinator)
- SyncEndpoints (REST/gRPC API for sync operations)
- Server-side conflict resolution
- Multi-client sync orchestration

## Current Alternatives

For sync functionality, use:
- **In-process sync:** `tree.Mesh(otherTree)` - Works locally
- **File-based sync:** Use FileTrunk or CloudTrunk with shared storage
- **GitHub sync:** Use GitHubTrunk for Git-based synchronization

## Roadmap

Server-side sync will be implemented in a future release (v0.7.0+).

See main AcornDB README for current sync capabilities.
