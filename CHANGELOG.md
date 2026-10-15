# History

## Unreleased

- 2026-10-15: the key of an existing node is immutable: saving a node whose ID already exists with a different key throws `ArgumentException` (400 in the API), and nothing in that batch is saved. Keys are used by external data (e.g. `TaxoStoreNodesPart`) to refer to nodes.

## 1.1.0

- 2026-10-14: searchable mapping labels, to find nodes also via aliases stored as mappings.
  - ⚠️ breaking changes:
    - database schema: table `node_mapping` has a new `label_ix` column (filtered label), with a trigram index. Existing databases are automatically upgraded when the store is initialized, copying the label of existing mappings into it (see README).
  - `TaxoNodeMapping` has a new `FilteredLabel` property. When not set, the store falls back to `Label`, as for nodes.
  - `TaxoNodeFilter` has a new `FilteredMappingLabel` filter, matching any part of the filtered label of any mapping of the node. When `FilteredLabel` is also set, the two are combined with OR, so that a node matches via either its own label or any of its mapping labels. This is also honored by `MatchDescendants`.
  - importer: `mappings.csv` has a new optional `filtered_label` column; when empty, it is generated from `label` like for nodes.
  - API: `filteredLabel` in mapping models, `filteredMappingLabel` in nodes filter.

## 1.0.1

- 2026-10-14: import `note` column from `nodes.csv` into `TaxoNode.Note`.

## 1.0.0

- 2026-10-13: flat trees and node mappings.
  - ⚠️ breaking changes:
    - database schema: table `tree` has a new `flat` column, and there is a new `node_mapping` table. Existing databases are automatically upgraded when the store is initialized (see README).
    - `ITaxoStore`: all the methods returning nodes have a new optional `includeMappings` parameter. Consumer code is not affected, but other implementations of the interface must be updated.
  - flat trees: `TaxoTree.Flat` marks trees which are just lists of root nodes. The store rejects nodes with a parent in a flat tree, and setting as flat a tree having non-root nodes.
  - node mappings: `TaxoNode.Mappings` maps a node to any number of external identifiers (scope, target, optional label; unique per node). When saving a node, null mappings are left untouched, while non-null mappings (even when empty) replace all the mappings of that node. Mappings are fetched only when requested via `includeMappings`.
  - `TaxoNodeFilter` has new `MappingScope` and `MappingTarget` filters, to find nodes from their mappings.
  - importer: `trees.csv` has a new optional `flat` column, and there is a new optional `mappings.csv` source (`TaxoStoreOptions.SeedMappingSource`, defaulting to `wwwroot/taxo/mappings.csv` in the API; `-m` option in `taxo-tool import-store`).
  - API: `flat` in tree models, `mappings` in node models, `includeMappings` query parameter in all the endpoints returning nodes, `mappingScope` and `mappingTarget` in nodes filter.

## 0.0.9

- 2026-10-03: robustness and performance review of store and API.
  - ⚠️ breaking changes:
    - `ITaxoStore` has a new `GetNodePositionsAsync` method, which gets depth, sibling position and children presence for any number of nodes with a single query.
    - `NodeFlagMatchMode.None` now means that none of the specified flags must be present (it was a no-op, i.e. flags were ignored). In the API, the default flag match mode is now `Any`, as in `TaxoNodeFilter` (it was `None`, so flags were ignored when the mode was not specified).
    - `TaxoNodeFilter.AncestorKey` now is an exact key match, and matches only descendants of the ancestor.
    - `POST` for nodes and trees now return the added/updated ID in the response body.
  - fixes in `PgSqlTaxoStore`:
    - filtering nodes by `AncestorKey` always failed with an SQL syntax error, and when counting, it discarded all other filters. Node queries are now built by a single builder shared by items and total count, and `AncestorKey` can also be combined with `MatchDescendants` (which previously was silently ignored in this case).
    - flags filter generated invalid SQL for non-alphanumeric flags (e.g. `-`) and for repeated flags, and treated `%`/`_` flags as wildcards.
    - LIKE wildcards (`%`, `_`, `\`) in key, parent key, filtered label and tree name filters are now matched literally.
    - adding a node with an explicit ID did not advance the ID sequence, so a later insertion could fail with a duplicate key.
    - writes now validate the hierarchy: a node's parent must belong to the same tree, and a node cannot be its own ancestor (which would also make recursive queries loop forever). Recursive queries are also cycle-safe.
    - `AddNodesAsync` (also used by `AddNodeAsync` and by CSV import) is now transactional: if any node is invalid, no node is saved.
    - duplicate keys now throw `TaxoStoreConflictException`, and invalid data (missing tree/parent, invalid flags, etc.) `ArgumentException`, rather than raw database exceptions. The API maps them to 409 and 400 respectively, rather than 500.
    - `GetDescendantNodesAsync` now returns nodes in depth-first traversal order as documented (it was sorted by level and key, mixing nodes from different parents).
    - `GetNodesAsync` with page size 0 now returns all the matching nodes, as `GetRootNodes` does (it returned none); requesting a page number lower than 1 no longer fails.
    - `TaxoTreeFilter.ToString` always ignored the name.
  - performance:
    - using a single `NpgsqlDataSource`.
    - paged queries get items and total count with a single query (`COUNT(*) OVER()`).
    - positioned nodes in the API (X, Y, HasChildren) are got with a single set-based query, rather than 3 queries per node (plus loading all the siblings of each node).
    - sibling position queries can now use indexes.
    - batch node writes are sent in batches of commands, minimizing round trips.
    - clearing the store uses `TRUNCATE`.
  - added tests for store (filters, paging, hierarchy, writes) and a new test project for API controllers.

## 0.0.8

- 2026-09-14:
  - fixes to importer (semaphore).
  - added CLI tool for import.

## 0.0.7

- 2026-09-13:
  - added detailed logging (via DI-injected `ILogger`) to `PgSqlTaxoStore` and `TaxoTreeImporter` to diagnose database initialization and CSV seeding at startup: existence check, creation, seeding start/completion (with tree/node counts), and warnings when a newly created database is left unseeded because seed sources are missing or incomplete.
  - `PgSqlTaxoStore.EnsureDatabaseReady` is now safe against concurrent callers (e.g. an API request racing the hosted initialization service) via an async lock with double-checked locking.
  - fixed a misconfigured Docker Compose deployment where `TaxoStore`'s connection string accidentally pointed to another service's database, causing the existence check to find that (unrelated) database and silently skip creation and seeding of the actual TaxoStore database, with no error logged.

## 0.0.6

- 2026-09-13: updated packages.
- 2026-09-06: updated packages.
- 2026-07-13: updated packages.
- 2026-06-08: updated packages.
- 2026-05-29:
  - updated packages.
  - fixed deep search in trees.
  - improved PgSql indexes.

## 0.0.4

- 2026-05-13: updated packages.
