# History

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
