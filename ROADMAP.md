# AcornDB Roadmap

This document tracks planned work items and open issues for the AcornDB project. Each entry includes a short description and suggested labels to help triage when creating GitHub issues.

## 🔧 Core / Infrastructure

- **Refactor `ITrunk<T>` for optional history & sync**  
  Expand `ITrunk<T>` with optional members such as `GetHistory`, `ExportChanges`, and `ImportChanges`. Default trunk implementations should throw `NotSupportedException` for features they do not support.  
  _Labels:_ `refactor`, `trunk`, `backend`

- **Implement `DocumentStoreTrunk<T>` for append-only persistence**  
  Build a `DocumentStoreTrunk` that follows the `DocumentStore<T>` snapshot-plus-log model with replay and history support. Existing trunks should be replaceable or wrappable to use this storage option.  
  _Labels:_ `storage`, `persistence`

- **Add `BlobTrunk<T>` backend using Azure Blob / S3**  
  Provide a trunk implementation that stores NutShells in cloud blob storage with async batching, incremental uploads, and optional history export.  
  _Labels:_ `storage`, `cloud`

- **Refactor `Tree<T>` to be trunk-agnostic & delegate history**  
  Update `Tree<T>` so that it no longer assumes any specific file or JSON layout. Delegate storage, history, and sync responsibilities entirely to trunk implementations.  
  _Labels:_ `refactor`, `core`

- **Support capability introspection on trunks**  
  Introduce an interface or method (for example, `ITrunkCapabilities`) that reports which features—history, sync, import/export—a trunk supports. Surface this information in the UI and CLI.  
  _Labels:_ `design`, `api`

## 🧯 Sync & Networking

- **Complete Tangle mesh synchronization**  
  Extend the current one-to-one synchronization into a many-to-many mesh with directionality, loop prevention, and deterministic merge ordering.  
  _Labels:_ `sync`, `mesh`

- **Enhance `Branch` to support pull mode and conflict direction flags**  
  Add push-only, pull-only, and bidirectional modes so a branch does not re-push the same changes it just received.  
  _Labels:_ `sync`, `branch`

- **Implement robust loop detection / deduping**  
  Use `ChangeId`s or vector clocks to avoid sync cycles and redundant pushes during trunk imports.  
  _Labels:_ `sync`, `safety`

- **Support incremental / delta sync (`ExportChanges` / `ImportChanges`)**  
  Allow trunks to export only changed NutShells and apply incremental merge logic during import.  
  _Labels:_ `sync`, `delta`

## 🖥 UI / Observability / Dashboard

- **Enable “Nut Inspector” UI actions: Stash / Toss / Crack buttons wired**  
  Connect the visualizer sidebar controls to live API endpoints for create, update, and delete operations.  
  _Labels:_ `ui`, `frontend`

- **Animate graph actions (stash, toss, shake, conflict) in UI**  
  Provide visual feedback in the D3 graph for node events, such as pulse, disappear, and glow animations.  
  _Labels:_ `ui`, `visualization`

- **Enable real-time updates of graph / node state**  
  Use SignalR or server push from Canopy/Hardwood so the graph automatically refreshes when changes occur.  
  _Labels:_ `ui`, `realtime`

- **Enable plant / uproot / entangle via UI**  
  Allow users to add new trees, remove existing ones, or connect to remote branches directly from the visualizer.  
  _Labels:_ `ui`, `management`

- **Add filtering / coloring options in visualizer**  
  Offer toggles to hide remote trees, color nodes by nut count, or filter by type to improve clarity.  
  _Labels:_ `ui`, `usability`

## 🛠 Tooling & Packaging

- **Build a CLI tool (Acorn CLI)**  
  Deliver a command-line interface to manage trees, groves, synchronization, backups, and other operations. Integrate with the Codex CLI where applicable.  
  _Labels:_ `tooling`, `cli`

- **Publish to NuGet package**  
  Package AcornDB for NuGet with versioning, dependency management, and CI/CD publishing automation.  
  _Labels:_ `packaging`, `release`

- **Write full integration tests (sync, conflict, multi-node)**  
  Expand the test suite to cover trunk behaviors, cross-branch synchronization, conflict resolution, and network partitions.  
  _Labels:_ `test`, `integration`

## 📜 Documentation & Vision

- **Write or update `VISION.md` / `PHILOSOPHY.md`**  
  Combine the original vision with the current project status and roadmap to orient new contributors.  
  _Labels:_ `documentation`

- **Document trunk implementations and capabilities**  
  Create documentation for each trunk (file, blob, memory) describing supported features like history and sync.  
  _Labels:_ `documentation`

- **Add usage samples to `README`**  
  Include code snippets demonstrating local usage, remote synchronization, conflict resolution, and custom trunk integration.  
  _Labels:_ `documentation`, `example`

