using Fusi.DbManager.PgSql;
using Fusi.Tools.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NpgsqlTypes;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TaxoStore.Core;

namespace TaxoStore.PgSql;

/// <summary>
/// PostgreSQL implementation of <see cref="ITaxoStore"/>.
/// </summary>
public sealed class PgSqlTaxoStore : ITaxoStore, IDisposable
{
    // columns read by ReadNode, in this order, from a node aliased as n
    private const string NODE_COLUMNS =
        "n.id, n.parent_id, n.tree_id, n.key, n.label, n.label_ix, " +
        "n.flags, n.note";

    // max number of upsert commands sent to the server in a single batch
    private const int WRITE_BATCH_SIZE = 500;

    // the sibling position (1-based, siblings ordered by key) of a node
    // aliased as t having columns id, parent_id, tree_id, key; the two
    // branches allow using the (tree_id, parent_id, key) and (parent_id, key)
    // indexes respectively
    private const string SIBLING_POSITION_SQL =
        "CASE WHEN t.parent_id IS NULL THEN " +
        "(SELECT COUNT(*) FROM node s WHERE s.tree_id = t.tree_id " +
        "AND s.parent_id IS NULL AND s.key <= t.key) " +
        "ELSE (SELECT COUNT(*) FROM node s WHERE s.parent_id = t.parent_id " +
        "AND s.key <= t.key) END";

    // idempotent script bringing a database created by a previous version
    // up to date with the current schema (see Assets/Schema.pgsql)
    private const string UPGRADE_SQL =
        "ALTER TABLE tree ADD COLUMN IF NOT EXISTS " +
        "flat BOOLEAN NOT NULL DEFAULT FALSE; " +
        "CREATE TABLE IF NOT EXISTS node_mapping (" +
        "node_id INT NOT NULL REFERENCES node(id) ON DELETE CASCADE, " +
        "scope VARCHAR(100) NOT NULL, " +
        "target VARCHAR(500) NOT NULL, " +
        "label VARCHAR(500), " +
        "CONSTRAINT pk_node_mapping PRIMARY KEY (node_id, scope, target)); " +
        "CREATE INDEX IF NOT EXISTS idx_node_mapping_scope_target " +
        "ON node_mapping(scope, target); " +
        "ALTER TABLE node_mapping ADD COLUMN IF NOT EXISTS " +
        "label_ix VARCHAR(500); " +
        "UPDATE node_mapping SET label_ix = label " +
        "WHERE label_ix IS NULL AND label IS NOT NULL; " +
        "CREATE INDEX IF NOT EXISTS idx_node_mapping_label_ix_trgm " +
        "ON node_mapping USING gin(label_ix gin_trgm_ops);";

    private readonly TaxoStoreOptions _options;
    private readonly ILogger _logger;
    private readonly PgSqlDbManager _dbManager;
    private readonly string _connectionString;
    private readonly NpgsqlDataSource _dataSource;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    // flags the async call flow that is currently running the
    // initialization/seeding logic inside EnsureDatabaseReady, so that
    // calls made back into this same flow (e.g. the CSV importer calling
    // AddTreeAsync/AddNodesAsync while seeding) can skip re-acquiring
    // _initLock instead of deadlocking on it (SemaphoreSlim is not
    // reentrant). This does not flow to unrelated concurrent callers, who
    // must still wait on _initLock until seeding has fully completed.
    private readonly AsyncLocal<bool> _initializing = new();
    private bool _disposed;
    private volatile bool _databaseReady;

    /// <summary>
    /// Initializes a new instance of the PgSqlTaxoStore class using the
    /// specified options.
    /// </summary>
    /// <param name="options">The options used to configure the tree store,
    /// including the PostgreSQL connection string.</param>
    /// <param name="logger">An optional logger used to report database
    /// initialization and seeding progress and diagnostics. When not
    /// provided, logging is disabled.</param>
    /// <exception cref="ArgumentNullException">Thrown if the options parameter
    /// is null.</exception>
    public PgSqlTaxoStore(TaxoStoreOptions options,
        ILogger<PgSqlTaxoStore>? logger = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? NullLogger<PgSqlTaxoStore>.Instance;

        // replace DB name with placeholder
        NpgsqlConnectionStringBuilder builder = new(options.Source)
        {
            Database = "{0}"
        };
        string csTemplate = builder.ConnectionString;

        _dbManager = new PgSqlDbManager(csTemplate);
        _connectionString = options.Source;
        _dataSource = NpgsqlDataSource.Create(_connectionString);
        _databaseReady = false;
    }

    /// <summary>
    /// Builds a connection description safe for logging, i.e. without
    /// including the password.
    /// </summary>
    private string GetSafeConnectionDescription()
    {
        try
        {
            NpgsqlConnectionStringBuilder builder = new(_connectionString);
            return $"Host={builder.Host};Port={builder.Port};" +
                $"Database={builder.Database}";
        }
        catch (Exception)
        {
            return "(unavailable)";
        }
    }

    private void Dispose(bool disposing)
    {
        if (_disposed) return;
        if (disposing)
        {
            _initLock.Dispose();
            _dataSource.Dispose();
        }
        _disposed = true;
    }

    /// <summary>
    /// Releases unmanaged and - optionally - managed resources.
    /// </summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    private static string? ExtractDbName(string connectionString)
    {
        return new NpgsqlConnectionStringBuilder(connectionString).Database;
    }

    private static string LoadResourceText(string resourceName)
    {
        using Stream stream = typeof(PgSqlTaxoStore).Assembly
            .GetManifestResourceStream(resourceName) ??
            throw new InvalidOperationException(
                $"Cannot load embedded resource '{resourceName}'");
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }

    private async Task EnsureDatabaseReady()
    {
        // fast path: already initialized, or this call is part of the same
        // async flow that is currently performing initialization/seeding
        // (e.g. the CSV importer calling back into AddTreeAsync/
        // AddNodesAsync below) - re-entering the lock in that case would
        // deadlock, since SemaphoreSlim is not reentrant
        if (_databaseReady || _initializing.Value) return;

        // guard against concurrent callers (e.g. an incoming API request
        // racing with the hosted initialization service, or several
        // requests arriving before the first one completes initialization)
        // trying to initialize the database at the same time
        await _initLock.WaitAsync().ConfigureAwait(false);
        try
        {
            // re-check now that we hold the lock: another caller may have
            // already completed initialization while we were waiting
            if (_databaseReady) return;

            // mark this async flow as the one performing initialization,
            // so that nested calls it makes (seeding) skip the lock above
            // instead of deadlocking on it
            _initializing.Value = true;

            string connectionInfo = GetSafeConnectionDescription();

            // check if database exists
            string dbName = ExtractDbName(_connectionString) ??
                throw new InvalidOperationException(
                    "Database name missing from connection string");

            _logger.LogInformation(
                "Checking TaxoStore database existence ({ConnectionInfo})",
                connectionInfo);

            bool existing = _dbManager.Exists(dbName);

            if (existing)
            {
                _logger.LogInformation(
                    "TaxoStore database {Database} already exists; " +
                    "skipping creation and seeding", dbName);

                // the database might have been created by a previous
                // version: ensure that its schema is up to date
                await using NpgsqlConnection connection = await _dataSource
                    .OpenConnectionAsync().ConfigureAwait(false);
                await using NpgsqlCommand command =
                    new(UPGRADE_SQL, connection);
                await command.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
            else
            {
                _logger.LogInformation(
                    "TaxoStore database {Database} not found; creating it " +
                    "({ConnectionInfo})", dbName, connectionInfo);

                // create database and seed its schema from DDL SQL in assets
                string sql =
                    LoadResourceText("TaxoStore.PgSql.Assets.Schema.pgsql");
                _dbManager.CreateDatabase(dbName, sql, null);

                _logger.LogInformation(
                    "TaxoStore database {Database} created", dbName);

                // seed if seed sources were provided
                bool hasTreeSource =
                    !string.IsNullOrEmpty(_options.SeedTreeSource);
                bool hasNodeSource =
                    !string.IsNullOrEmpty(_options.SeedNodeSource);

                if (hasTreeSource && hasNodeSource)
                {
                    _logger.LogInformation(
                        "Seeding TaxoStore database {Database} from " +
                        "configured sources", dbName);

                    TaxoTreeImporter importer = new(this, _logger);

                    // mappings are optional
                    bool hasMappingSource =
                        !string.IsNullOrEmpty(_options.SeedMappingSource);

                    if (_options.SeedSourceAsText)
                    {
                        // create readers from direct CSV text
                        using StringReader treeReader =
                            new(_options.SeedTreeSource!);
                        using StringReader nodeReader =
                            new(_options.SeedNodeSource!);
                        using StringReader? mappingReader = hasMappingSource
                            ? new(_options.SeedMappingSource!) : null;
                        await importer.ImportAsync(treeReader, nodeReader,
                            mappingReader).ConfigureAwait(false);
                    }
                    else
                    {
                        // create readers from file paths
                        using StreamReader treeReader =
                            new(_options.SeedTreeSource!, Encoding.UTF8);
                        using StreamReader nodeReader =
                            new(_options.SeedNodeSource!, Encoding.UTF8);
                        using StreamReader? mappingReader = hasMappingSource
                            ? new(_options.SeedMappingSource!, Encoding.UTF8)
                            : null;
                        await importer.ImportAsync(treeReader, nodeReader,
                            mappingReader).ConfigureAwait(false);
                    }
                }
                else
                {
                    _logger.LogWarning(
                        "TaxoStore database {Database} was created but not " +
                        "seeded: {Reason}", dbName,
                        hasTreeSource || hasNodeSource
                            ? "only one of SeedTreeSource/SeedNodeSource " +
                              "was provided; both are required"
                            : "no SeedTreeSource/SeedNodeSource was " +
                              "configured, and no default CSV files were " +
                              "found");
                }
            }

            _databaseReady = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error ensuring TaxoStore database is ready ({ConnectionInfo})",
                GetSafeConnectionDescription());
            throw;
        }
        finally
        {
            _initializing.Value = false;
            _initLock.Release();
        }
    }

    /// <summary>
    /// Ensure that the database is ready, and open a new connection to it.
    /// </summary>
    /// <returns>The open connection.</returns>
    private async Task<NpgsqlConnection> OpenConnectionAsync()
    {
        await EnsureDatabaseReady().ConfigureAwait(false);
        return await _dataSource.OpenConnectionAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Ensure that the database is initialized.
    /// </summary>
    public async Task InitializeAsync()
    {
        await EnsureDatabaseReady();
    }

    #region Helpers
    /// <summary>
    /// Builds a pattern for a case-insensitive "contains" match via
    /// <c>ILIKE</c>, escaping LIKE wildcards in the specified text so that
    /// they are matched literally.
    /// </summary>
    /// <param name="text">The text to find.</param>
    /// <returns>Pattern.</returns>
    private static string BuildContainsPattern(string text)
    {
        // backslash is the default escape character for LIKE in PostgreSQL
        return "%" + text.Replace("\\", "\\\\")
            .Replace("%", "\\%")
            .Replace("_", "\\_") + "%";
    }

    /// <summary>
    /// Gets the limit and offset for the specified paging options, or null
    /// when paging is disabled (page size is 0 or less).
    /// </summary>
    private static (int Limit, long Offset)? GetPaging(IPagingOptions options)
    {
        if (options.PageSize <= 0) return null;
        int pageNumber = Math.Max(1, options.PageNumber);
        return (options.PageSize, (long)(pageNumber - 1) * options.PageSize);
    }

    /// <summary>
    /// Reads a page of items.
    /// </summary>
    /// <typeparam name="T">The type of item.</typeparam>
    /// <param name="connection">The open connection.</param>
    /// <param name="selectSql">The SQL query for items, including ORDER BY
    /// but excluding paging. Its last column must be <c>COUNT(*) OVER()</c>,
    /// i.e. the total count of the matching items.</param>
    /// <param name="countSql">The SQL query counting all the matching items.
    /// This is used only when the requested page is empty and beyond the first
    /// one, so the total count cannot be got from the items query.</param>
    /// <param name="parameters">The parameters for both queries.</param>
    /// <param name="options">The paging options. When page size is 0, all
    /// the items are returned.</param>
    /// <param name="read">The function reading an item.</param>
    /// <returns>The page.</returns>
    private static async Task<DataPage<T>> ReadPageAsync<T>(
        NpgsqlConnection connection,
        string selectSql,
        string countSql,
        IList<NpgsqlParameter> parameters,
        IPagingOptions options,
        Func<NpgsqlDataReader, T> read)
    {
        (int Limit, long Offset)? paging = GetPaging(options);
        List<T> items = [];
        long total = 0;

        await using (NpgsqlCommand command = connection.CreateCommand())
        {
            command.CommandText = paging == null
                ? selectSql
                : selectSql + " LIMIT @limit OFFSET @offset";
            foreach (NpgsqlParameter p in parameters)
                command.Parameters.Add(p.Clone());
            if (paging != null)
            {
                command.Parameters.AddWithValue("limit", paging.Value.Limit);
                command.Parameters.AddWithValue("offset", paging.Value.Offset);
            }

            await using NpgsqlDataReader reader =
                await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                items.Add(read(reader));
                total = reader.GetInt64(reader.FieldCount - 1);
            }
        }

        if (items.Count == 0 && paging?.Offset > 0)
        {
            await using NpgsqlCommand command = connection.CreateCommand();
            command.CommandText = countSql;
            foreach (NpgsqlParameter p in parameters)
                command.Parameters.Add(p.Clone());
            total = Convert.ToInt64(await command.ExecuteScalarAsync());
        }

        return paging == null
            ? new DataPage<T>(1, (int)total, (int)total, items)
            : new DataPage<T>(Math.Max(1, options.PageNumber),
                options.PageSize, (int)total, items);
    }

    /// <summary>
    /// Translates a PostgreSQL exception raised by a write operation into
    /// the corresponding store exception, if any.
    /// </summary>
    private static Exception? TranslateWriteException(PostgresException ex)
    {
        return ex.SqlState switch
        {
            PostgresErrorCodes.UniqueViolation =>
                new TaxoStoreConflictException(
                    "Duplicate key: " + (ex.Detail ?? ex.MessageText), ex),
            PostgresErrorCodes.ForeignKeyViolation =>
                new ArgumentException(
                    "Reference to a missing tree or node: " +
                    (ex.Detail ?? ex.MessageText), ex),
            PostgresErrorCodes.CheckViolation or
            PostgresErrorCodes.NotNullViolation or
            PostgresErrorCodes.StringDataRightTruncation =>
                new ArgumentException("Invalid data: " + ex.MessageText, ex),
            _ => null
        };
    }
    #endregion

    #region Trees
    /// <summary>
    /// Gets the tree with the specified ID.
    /// </summary>
    /// <param name="id">The tree's ID (key).</param>
    /// <returns>Tree or null if not found.</returns>
    public async Task<TaxoTree?> GetTreeAsync(string id)
    {
        const string sql =
            "SELECT id, name, note, flat FROM tree WHERE id = @id";

        await using NpgsqlConnection connection = await OpenConnectionAsync();
        await using NpgsqlCommand command = new(sql, connection);
        command.Parameters.AddWithValue("id", id);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? ReadTree(reader) : null;
    }

    private static TaxoTree ReadTree(NpgsqlDataReader reader)
    {
        return new TaxoTree
        {
            Id = reader.GetString(0),
            Name = reader.GetString(1),
            Note = reader.IsDBNull(2) ? null : reader.GetString(2),
            Flat = reader.GetBoolean(3)
        };
    }

    /// <summary>
    /// Adds or updates the specified tree.
    /// </summary>
    /// <param name="tree">The tree to add or update.</param>
    /// <returns>The ID (key) of the tree which was added.</returns>
    /// <exception cref="ArgumentException">Missing tree ID or name, invalid
    /// data, or a tree set as flat while it has non-root nodes.</exception>
    public async Task<string> AddTreeAsync(TaxoTree tree)
    {
        ArgumentNullException.ThrowIfNull(tree);
        if (string.IsNullOrWhiteSpace(tree.Id))
            throw new ArgumentException("Tree ID is required", nameof(tree));
        if (string.IsNullOrWhiteSpace(tree.Name))
            throw new ArgumentException("Tree name is required", nameof(tree));

        // insert or update tree with specified ID
        const string sql =
            "INSERT INTO tree (id, name, note, flat) " +
            "VALUES (@id, @name, @note, @flat) " +
            "ON CONFLICT (id) DO UPDATE " +
            "SET name = EXCLUDED.name, note = EXCLUDED.note, " +
            "flat = EXCLUDED.flat " +
            "RETURNING id";

        // a flat tree cannot have non-root nodes
        const string flatSql =
            "SELECT EXISTS(SELECT 1 FROM node " +
            "WHERE tree_id = @id AND parent_id IS NOT NULL)";

        await using NpgsqlConnection connection = await OpenConnectionAsync();
        await using NpgsqlTransaction transaction =
            await connection.BeginTransactionAsync();
        await using NpgsqlCommand command = new(sql, connection, transaction);
        command.Parameters.AddWithValue("id", tree.Id);
        command.Parameters.AddWithValue("name", tree.Name);
        command.Parameters.AddWithValue("note",
            (object?)tree.Note ?? DBNull.Value);
        command.Parameters.AddWithValue("flat", tree.Flat);

        try
        {
            object? result = await command.ExecuteScalarAsync();

            if (tree.Flat)
            {
                await using NpgsqlCommand flatCommand =
                    new(flatSql, connection, transaction);
                flatCommand.Parameters.AddWithValue("id", tree.Id);
                if (await flatCommand.ExecuteScalarAsync() is true)
                {
                    throw new ArgumentException(
                        $"Tree {tree.Id} cannot be flat because it has " +
                        "non-root nodes", nameof(tree));
                }
            }

            await transaction.CommitAsync();
            return result?.ToString() ?? tree.Id;
        }
        catch (PostgresException ex) when (TranslateWriteException(ex)
            is Exception translated)
        {
            throw translated;
        }
    }

    /// <summary>
    /// Deletes the tree with the specified ID, with all its nodes.
    /// </summary>
    /// <param name="id">The ID (key) of the tree to delete.</param>
    /// <returns>The ID (key) of the tree which was deleted, or null if it was
    /// not found.</returns>
    public async Task<string?> DeleteTreeAsync(string id)
    {
        const string sql = "DELETE FROM tree WHERE id = @id";

        await using NpgsqlConnection connection = await OpenConnectionAsync();
        await using NpgsqlCommand command = new(sql, connection);
        command.Parameters.AddWithValue("id", id);

        int affected = await command.ExecuteNonQueryAsync();
        return affected > 0 ? id : null;
    }

    /// <summary>
    /// Retrieves a paged list of trees that match the specified filter
    /// criteria, sorted by name.
    /// </summary>
    /// <param name="filter">The filter criteria. When page size is 0,
    /// all the matching trees are returned.</param>
    /// <returns>A page of trees.</returns>
    public async Task<DataPage<TaxoTree>> GetTreesAsync(TaxoTreeFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);

        string where = "";
        List<NpgsqlParameter> parameters = [];
        if (!string.IsNullOrEmpty(filter.Name))
        {
            where = " WHERE name ILIKE @name";
            parameters.Add(new NpgsqlParameter("name",
                BuildContainsPattern(filter.Name)));
        }

        await using NpgsqlConnection connection = await OpenConnectionAsync();
        return await ReadPageAsync(connection,
            $"SELECT id, name, note, flat, COUNT(*) OVER() FROM tree{where} " +
            "ORDER BY name, id",
            $"SELECT COUNT(*) FROM tree{where}",
            parameters, filter, ReadTree);
    }
    #endregion

    #region Node Reads
    private static TaxoNode ReadNode(NpgsqlDataReader reader)
    {
        return new TaxoNode
        {
            Id = reader.GetInt32(0),
            ParentId = reader.IsDBNull(1) ? null : reader.GetInt32(1),
            TreeId = reader.GetString(2),
            Key = reader.GetString(3),
            Label = reader.GetString(4),
            FilteredLabel = reader.GetString(5),
            Flags = reader.IsDBNull(6) ? null : reader.GetString(6),
            Note = reader.IsDBNull(7) ? null : reader.GetString(7)
        };
    }

    /// <summary>
    /// Loads the mappings of the specified nodes with a single query,
    /// setting the <see cref="TaxoNode.Mappings"/> of each node (to an empty
    /// list when it has no mappings). Mappings are sorted by scope and target.
    /// </summary>
    private static async Task LoadMappingsAsync(NpgsqlConnection connection,
        IEnumerable<TaxoNode> nodes)
    {
        Dictionary<int, IList<TaxoNodeMapping>> mappings = [];
        foreach (TaxoNode node in nodes)
        {
            // the same node might occur more than once
            if (!mappings.TryGetValue(node.Id,
                out IList<TaxoNodeMapping>? list))
            {
                mappings[node.Id] = list = [];
            }
            node.Mappings = list;
        }
        if (mappings.Count == 0) return;

        await using NpgsqlCommand command = new(
            "SELECT node_id, scope, target, label, label_ix " +
            "FROM node_mapping WHERE node_id = ANY(@ids) ORDER BY node_id, scope, target",
            connection);
        command.Parameters.AddWithValue("ids", mappings.Keys.ToArray());

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            mappings[reader.GetInt32(0)].Add(new TaxoNodeMapping
            {
                Scope = reader.GetString(1),
                Target = reader.GetString(2),
                Label = reader.IsDBNull(3) ? null : reader.GetString(3),
                FilteredLabel = reader.IsDBNull(4) ? null : reader.GetString(4)
            });
        }
    }

    private static async Task<List<TaxoNode>> ReadNodesAsync(
        NpgsqlCommand command, bool includeMappings)
    {
        List<TaxoNode> nodes = [];
        await using (NpgsqlDataReader reader =
            await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync()) nodes.Add(ReadNode(reader));
        }

        if (includeMappings)
            await LoadMappingsAsync(command.Connection!, nodes);
        return nodes;
    }

    /// <summary>
    /// Retrieves the root node(s) of the specified tree, sorted by key.
    /// </summary>
    /// <param name="treeId">The ID (key) of the tree.</param>
    /// <param name="options">Paging options. If <see cref="PagingOptions.PageSize"/>
    /// is 0, all root nodes are returned without paging.</param>
    /// <param name="includeMappings">True to also fetch the mappings of the
    /// retrieved nodes.</param>
    /// <returns>A page of root nodes. When page size is 0, the page contains
    /// all root nodes.</returns>
    public Task<DataPage<TaxoNode>> GetRootNodes(string treeId,
        PagingOptions options, bool includeMappings = false)
    {
        ArgumentNullException.ThrowIfNull(options);

        return GetNodesAsync(new TaxoNodeFilter
        {
            PageNumber = options.PageNumber,
            PageSize = options.PageSize,
            TreeId = treeId,
            IsRoot = true
        }, includeMappings);
    }

    /// <summary>
    /// Retrieves the node with the specified identifier.
    /// </summary>
    /// <param name="id">The ID of the node to retrieve.</param>
    /// <param name="includeMappings">True to also fetch the mappings of the
    /// retrieved node.</param>
    /// <returns>The node or null if not found.</returns>
    public async Task<TaxoNode?> GetNodeAsync(int id,
        bool includeMappings = false)
    {
        await using NpgsqlConnection connection = await OpenConnectionAsync();
        await using NpgsqlCommand command = new(
            $"SELECT {NODE_COLUMNS} FROM node n WHERE n.id = @id", connection);
        command.Parameters.AddWithValue("id", id);

        List<TaxoNode> nodes = await ReadNodesAsync(command, includeMappings);
        return nodes.Count > 0 ? nodes[0] : null;
    }

    /// <summary>
    /// Retrieves the node associated with the specified key from the given tree.
    /// </summary>
    /// <param name="treeId">The ID (key) of the tree to search.</param>
    /// <param name="key">The key of the node to retrieve.</param>
    /// <param name="includeMappings">True to also fetch the mappings of the
    /// retrieved node.</param>
    /// <returns>A task that represents the asynchronous operation. The task
    /// result contains the node associated with the specified key, or null
    /// if no such node exists.</returns>
    public async Task<TaxoNode?> GetNodeFromKeyAsync(string treeId, string key,
        bool includeMappings = false)
    {
        await using NpgsqlConnection connection = await OpenConnectionAsync();
        await using NpgsqlCommand command = new(
            $"SELECT {NODE_COLUMNS} FROM node n " +
            "WHERE n.tree_id = @treeId AND n.key = @key", connection);
        command.Parameters.AddWithValue("treeId", treeId);
        command.Parameters.AddWithValue("key", key);

        List<TaxoNode> nodes = await ReadNodesAsync(command, includeMappings);
        return nodes.Count > 0 ? nodes[0] : null;
    }

    /// <summary>
    /// Builds the SQL parts for querying nodes (aliased as <c>n</c>) matching
    /// the specified filter.
    /// </summary>
    /// <param name="filter">The filter.</param>
    /// <returns>The CTEs prefix (empty or starting with <c>WITH</c>), the
    /// FROM/WHERE body, and the parameters.</returns>
    private static (string Ctes, string Body, List<NpgsqlParameter> Parameters)
        BuildNodeQuery(TaxoNodeFilter filter)
    {
        List<string> ctes = [];
        StringBuilder from = new(" FROM node n");
        List<string> where = [];
        List<NpgsqlParameter> parameters = [];
        bool hasTree = !string.IsNullOrEmpty(filter.TreeId);

        // tree
        if (hasTree)
        {
            where.Add("n.tree_id = @treeId");
            parameters.Add(new NpgsqlParameter("treeId", filter.TreeId));
        }

        // root or parent
        if (filter.IsRoot)
        {
            where.Add("n.parent_id IS NULL");
        }
        else if (filter.ParentId.HasValue)
        {
            where.Add("n.parent_id = @parentId");
            parameters.Add(new NpgsqlParameter("parentId",
                filter.ParentId.Value));
        }

        // key (contains)
        if (!string.IsNullOrEmpty(filter.Key))
        {
            where.Add("n.key ILIKE @key");
            parameters.Add(new NpgsqlParameter("key",
                BuildContainsPattern(filter.Key)));
        }

        // parent key (contains)
        if (!string.IsNullOrEmpty(filter.ParentKey))
        {
            from.Append(" JOIN node p ON p.id = n.parent_id");
            where.Add("p.key ILIKE @parentKey");
            parameters.Add(new NpgsqlParameter("parentKey",
                BuildContainsPattern(filter.ParentKey)));
        }

        // ancestor key (exact): descendants of the node(s) with that key;
        // UNION (rather than UNION ALL) also guarantees termination
        if (!string.IsNullOrEmpty(filter.AncestorKey))
        {
            ctes.Add("ad(id) AS (" +
                "SELECT c.id FROM node a JOIN node c ON c.parent_id = a.id " +
                "WHERE a.key = @ancestorKey" +
                (hasTree ? " AND a.tree_id = @treeId" : "") +
                " UNION " +
                "SELECT c.id FROM ad JOIN node c ON c.parent_id = ad.id)");
            where.Add("n.id IN (SELECT id FROM ad)");
            parameters.Add(new NpgsqlParameter("ancestorKey",
                filter.AncestorKey));
        }

        // filtered label of node and/or of its mappings (contains, combined
        // with OR), optionally matching descendants
        bool hasLabel = !string.IsNullOrEmpty(filter.FilteredLabel);
        bool hasMappingLabel =
            !string.IsNullOrEmpty(filter.FilteredMappingLabel);
        if (hasLabel || hasMappingLabel)
        {
            // the matching nodes as (id, parent_id); each branch can use
            // the trigram index on its label_ix, and UNION merges them
            List<string> matches = [];
            if (hasLabel)
            {
                parameters.Add(new NpgsqlParameter("filteredLabel",
                    BuildContainsPattern(filter.FilteredLabel!)));
                matches.Add("SELECT id, parent_id FROM node " +
                    "WHERE label_ix ILIKE @filteredLabel" +
                    (hasTree ? " AND tree_id = @treeId" : ""));
            }
            if (hasMappingLabel)
            {
                parameters.Add(new NpgsqlParameter("filteredMappingLabel",
                    BuildContainsPattern(filter.FilteredMappingLabel!)));
                matches.Add("SELECT x.id, x.parent_id FROM node_mapping m " +
                    "JOIN node x ON x.id = m.node_id " +
                    "WHERE m.label_ix ILIKE @filteredMappingLabel" +
                    (hasTree ? " AND x.tree_id = @treeId" : ""));
            }
            string matchSql = string.Join(" UNION ", matches);

            if (filter.MatchDescendants)
            {
                // walk up from matching nodes to collect them and all their
                // ancestors
                ctes.Add($"lx(id, parent_id) AS ({matchSql})");
                ctes.Add("lm(id, parent_id) AS (" +
                    "SELECT id, parent_id FROM lx" +
                    " UNION " +
                    "SELECT x.id, x.parent_id FROM lm " +
                    "JOIN node x ON x.id = lm.parent_id)");
                where.Add("n.id IN (SELECT id FROM lm)");
            }
            else if (!hasMappingLabel)
            {
                where.Add("n.label_ix ILIKE @filteredLabel");
            }
            else
            {
                where.Add($"n.id IN (SELECT id FROM ({matchSql}) lx)");
            }
        }

        // flags: each character is a flag
        if (!string.IsNullOrEmpty(filter.Flags))
        {
            string[] flags = [.. filter.Flags.Distinct()
                .Select(c => c.ToString())];
            parameters.Add(new NpgsqlParameter("flags",
                NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = flags });

            // string_to_array with NULL delimiter splits into characters
            const string nodeFlags = "string_to_array(n.flags, NULL)";
            where.Add(filter.FlagMatchMode switch
            {
                NodeFlagMatchMode.All => $"{nodeFlags} @> @flags",
                NodeFlagMatchMode.None => $"NOT ({nodeFlags} && @flags)",
                _ => $"{nodeFlags} && @flags"
            });
        }

        // leaf
        if (filter.IsLeaf.HasValue)
        {
            where.Add((filter.IsLeaf.Value ? "NOT " : "") +
                "EXISTS (SELECT 1 FROM node c WHERE c.parent_id = n.id)");
        }

        // mapping scope and/or target (exact), matched by the same mapping
        bool hasScope = !string.IsNullOrEmpty(filter.MappingScope);
        bool hasTarget = !string.IsNullOrEmpty(filter.MappingTarget);
        if (hasScope || hasTarget)
        {
            where.Add("EXISTS (SELECT 1 FROM node_mapping m " +
                "WHERE m.node_id = n.id" +
                (hasScope ? " AND m.scope = @mappingScope" : "") +
                (hasTarget ? " AND m.target = @mappingTarget" : "") + ")");
            if (hasScope)
            {
                parameters.Add(new NpgsqlParameter("mappingScope",
                    filter.MappingScope));
            }
            if (hasTarget)
            {
                parameters.Add(new NpgsqlParameter("mappingTarget",
                    filter.MappingTarget));
            }
        }

        if (where.Count > 0)
            from.Append(" WHERE ").Append(string.Join(" AND ", where));

        string ctesSql = ctes.Count > 0
            ? "WITH RECURSIVE " + string.Join(", ", ctes) + " "
            : "";

        return (ctesSql, from.ToString(), parameters);
    }

    /// <summary>
    /// Retrieves a paged list of nodes that match the specified filter
    /// criteria, sorted by key.
    /// </summary>
    /// <param name="filter">The filter criteria. When page size is 0,
    /// all the matching nodes are returned.</param>
    /// <param name="includeMappings">True to also fetch the mappings of the
    /// retrieved nodes.</param>
    /// <returns>A page of nodes.</returns>
    public async Task<DataPage<TaxoNode>> GetNodesAsync(TaxoNodeFilter filter,
        bool includeMappings = false)
    {
        ArgumentNullException.ThrowIfNull(filter);

        (string ctes, string body, List<NpgsqlParameter> parameters) =
            BuildNodeQuery(filter);

        await using NpgsqlConnection connection = await OpenConnectionAsync();
        DataPage<TaxoNode> page = await ReadPageAsync(connection,
            $"{ctes}SELECT {NODE_COLUMNS}, COUNT(*) OVER(){body} " +
            "ORDER BY n.key, n.id",
            $"{ctes}SELECT COUNT(*){body}",
            parameters, filter, ReadNode);

        if (includeMappings) await LoadMappingsAsync(connection, page.Items);
        return page;
    }

    /// <summary>
    /// Determines whether the node with the specified identifier has any
    /// child nodes.
    /// </summary>
    /// <param name="id">The ID of the node to check for child nodes.</param>
    /// <returns>True if the node has children; otherwise, false.</returns>
    public async Task<bool> NodeHasChildrenAsync(int id)
    {
        const string sql =
            "SELECT EXISTS(SELECT 1 FROM node WHERE parent_id = @id)";

        await using NpgsqlConnection connection = await OpenConnectionAsync();
        await using NpgsqlCommand command = new(sql, connection);
        command.Parameters.AddWithValue("id", id);

        object? result = await command.ExecuteScalarAsync();
        return result is true;
    }

    /// <summary>
    /// Retrieves the collection of child nodes for the specified parent node.
    /// </summary>
    /// <param name="parentId">The identifier of the parent node.</param>
    /// <param name="includeMappings">True to also fetch the mappings of the
    /// retrieved nodes.</param>
    /// <returns>A list of child nodes, sorted by their key.</returns>
    public async Task<IList<TaxoNode>> GetChildNodesAsync(int parentId,
        bool includeMappings = false)
    {
        await using NpgsqlConnection connection = await OpenConnectionAsync();
        await using NpgsqlCommand command = new(
            $"SELECT {NODE_COLUMNS} FROM node n " +
            "WHERE n.parent_id = @parentId ORDER BY n.key, n.id", connection);
        command.Parameters.AddWithValue("parentId", parentId);

        return await ReadNodesAsync(command, includeMappings);
    }

    /// <summary>
    /// Retrieves all descendant nodes of the specified parent node using
    /// a recursive query.
    /// </summary>
    /// <param name="parentId">The identifier of the parent node.</param>
    /// <param name="includeMappings">True to also fetch the mappings of the
    /// retrieved nodes.</param>
    /// <returns>A list of descendant nodes in depth-first (pre-order)
    /// traversal order, with siblings sorted by key.</returns>
    public async Task<IList<TaxoNode>> GetDescendantNodesAsync(int parentId,
        bool includeMappings = false)
    {
        // the sort path is the array of keys from the first descendant level
        // down to each node: sorting by it yields a pre-order traversal
        const string sql =
            "WITH RECURSIVE d AS (" +
            "SELECT id, parent_id, tree_id, key, label, label_ix, flags, " +
            "note, ARRAY[key::text] AS sort_path " +
            "FROM node WHERE parent_id = @parentId " +
            "UNION ALL " +
            "SELECT c.id, c.parent_id, c.tree_id, c.key, c.label, " +
            "c.label_ix, c.flags, c.note, d.sort_path || c.key::text " +
            "FROM node c JOIN d ON c.parent_id = d.id" +
            ") CYCLE id SET is_cycle USING cycle_path " +
            $"SELECT {NODE_COLUMNS} FROM d n WHERE NOT n.is_cycle " +
            "ORDER BY n.sort_path";

        await using NpgsqlConnection connection = await OpenConnectionAsync();
        await using NpgsqlCommand command = new(sql, connection);
        command.Parameters.AddWithValue("parentId", parentId);

        return await ReadNodesAsync(command, includeMappings);
    }

    /// <summary>
    /// Retrieves all ancestor nodes of the specified node, ordered from the
    /// immediate parent up to the root.
    /// </summary>
    /// <param name="nodeId">The identifier of the node.</param>
    /// <param name="includeMappings">True to also fetch the mappings of the
    /// retrieved nodes.</param>
    /// <returns>A list of ancestor nodes.</returns>
    public async Task<IList<TaxoNode>> GetAncestorNodesAsync(int nodeId,
        bool includeMappings = false)
    {
        const string sql =
            "WITH RECURSIVE a AS (" +
            "SELECT id, parent_id, tree_id, key, label, label_ix, flags, " +
            "note, 0 AS level FROM node WHERE id = @nodeId " +
            "UNION ALL " +
            "SELECT p.id, p.parent_id, p.tree_id, p.key, p.label, " +
            "p.label_ix, p.flags, p.note, a.level + 1 " +
            "FROM node p JOIN a ON p.id = a.parent_id" +
            ") CYCLE id SET is_cycle USING cycle_path " +
            $"SELECT {NODE_COLUMNS} FROM a n " +
            "WHERE n.level > 0 AND NOT n.is_cycle ORDER BY n.level";

        await using NpgsqlConnection connection = await OpenConnectionAsync();
        await using NpgsqlCommand command = new(sql, connection);
        command.Parameters.AddWithValue("nodeId", nodeId);

        return await ReadNodesAsync(command, includeMappings);
    }

    /// <summary>
    /// Retrieves the path from the root node to the specified target node,
    /// including the page number for each step.
    /// </summary>
    /// <param name="nodeId">The identifier of the target node.</param>
    /// <param name="pageSize">The page size used to calculate page numbers.</param>
    /// <returns>A list of path steps from root to target.</returns>
    public async Task<IList<TaxoNodePathStep>> GetNodePathAsync(int nodeId,
        int pageSize)
    {
        if (pageSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(pageSize),
                "Page size must be greater than 0");

        // get the path from target to root, and the 1-based position of
        // each node among its siblings, ordering from root to target
        const string sql =
            "WITH RECURSIVE t AS (" +
            "SELECT id, parent_id, tree_id, key, 0 AS level " +
            "FROM node WHERE id = @nodeId " +
            "UNION ALL " +
            "SELECT p.id, p.parent_id, p.tree_id, p.key, t.level + 1 " +
            "FROM node p JOIN t ON p.id = t.parent_id" +
            ") CYCLE id SET is_cycle USING cycle_path " +
            $"SELECT t.id, {SIBLING_POSITION_SQL} FROM t " +
            "WHERE NOT t.is_cycle ORDER BY t.level DESC";

        await using NpgsqlConnection connection = await OpenConnectionAsync();
        await using NpgsqlCommand command = new(sql, connection);
        command.Parameters.AddWithValue("nodeId", nodeId);

        List<TaxoNodePathStep> steps = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            int id = reader.GetInt32(0);
            long position = reader.GetInt64(1);
            int page = (int)((position - 1) / pageSize) + 1;
            steps.Add(new TaxoNodePathStep(id, page));
        }

        return steps;
    }

    /// <summary>
    /// Gets the position of each of the specified nodes in its tree, i.e.
    /// its depth (Y), its sibling position (X, with siblings ordered by key),
    /// and whether it has children. All the positions are computed with a
    /// single query.
    /// </summary>
    /// <param name="nodeIds">The IDs of the nodes.</param>
    /// <returns>A dictionary where each key is a node ID and each value
    /// is its position. Nodes not found are not included.</returns>
    public async Task<IDictionary<int, TaxoNodePosition>> GetNodePositionsAsync(
        IEnumerable<int> nodeIds)
    {
        ArgumentNullException.ThrowIfNull(nodeIds);

        int[] ids = [.. nodeIds.Distinct()];
        Dictionary<int, TaxoNodePosition> positions = [];
        if (ids.Length == 0) return positions;

        // the depth is the number of rows got by walking up from each node
        const string sql =
            "WITH RECURSIVE t AS (" +
            "SELECT id, parent_id, tree_id, key FROM node WHERE id = ANY(@ids)" +
            "), up(node_id, parent_id) AS (" +
            "SELECT id, parent_id FROM t " +
            "UNION ALL " +
            "SELECT up.node_id, p.parent_id FROM up " +
            "JOIN node p ON p.id = up.parent_id" +
            ") CYCLE node_id, parent_id SET is_cycle USING cycle_path, " +
            "depth AS (SELECT node_id, COUNT(*) AS y FROM up " +
            "WHERE NOT is_cycle GROUP BY node_id) " +
            $"SELECT t.id, depth.y, {SIBLING_POSITION_SQL}, " +
            "EXISTS (SELECT 1 FROM node c WHERE c.parent_id = t.id) " +
            "FROM t JOIN depth ON depth.node_id = t.id";

        await using NpgsqlConnection connection = await OpenConnectionAsync();
        await using NpgsqlCommand command = new(sql, connection);
        command.Parameters.AddWithValue("ids", ids);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            int id = reader.GetInt32(0);
            positions[id] = new TaxoNodePosition(id,
                (int)reader.GetInt64(1),
                (int)reader.GetInt64(2),
                reader.GetBoolean(3));
        }
        return positions;
    }
    #endregion

    #region Node Writes
    private static void ValidateNode(TaxoNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (node.Id < 0)
            throw new ArgumentException($"Invalid node ID: {node.Id}");
        if (string.IsNullOrWhiteSpace(node.TreeId))
            throw new ArgumentException($"Node {node} has no tree ID");
        if (string.IsNullOrWhiteSpace(node.Key))
            throw new ArgumentException($"Node {node} has no key");
        if (node.Label == null)
            throw new ArgumentException($"Node {node} has no label");
        if (node.ParentId.HasValue && node.ParentId == node.Id)
            throw new ArgumentException($"Node {node} cannot be its own parent");

        if (node.Mappings == null) return;

        HashSet<(string, string)> mappingKeys = [];
        foreach (TaxoNodeMapping mapping in node.Mappings)
        {
            if (mapping == null)
                throw new ArgumentException($"Node {node} has a null mapping");
            if (string.IsNullOrWhiteSpace(mapping.Scope))
            {
                throw new ArgumentException(
                    $"Node {node} has a mapping without scope");
            }
            if (string.IsNullOrWhiteSpace(mapping.Target))
            {
                throw new ArgumentException(
                    $"Node {node} has a mapping without target");
            }
            if (!mappingKeys.Add((mapping.Scope, mapping.Target)))
            {
                throw new ArgumentException(
                    $"Node {node} has a duplicate mapping {mapping.Scope}:" +
                    mapping.Target);
            }
        }
    }

    /// <summary>
    /// Replaces the mappings of each of the specified (just written) nodes
    /// having non-null mappings with those mappings. The mappings of nodes
    /// having null mappings are left untouched.
    /// </summary>
    /// <param name="connection">The connection.</param>
    /// <param name="transaction">The transaction.</param>
    /// <param name="nodes">The nodes.</param>
    /// <param name="ids">The ID of each node in <paramref name="nodes"/>.
    /// </param>
    private static async Task SaveMappingsAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction,
        List<TaxoNode> nodes, List<int> ids)
    {
        // when the same node occurs more than once, the last one wins
        Dictionary<int, IList<TaxoNodeMapping>> mappings = [];
        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i].Mappings != null) mappings[ids[i]] = nodes[i].Mappings!;
        }
        if (mappings.Count == 0) return;

        await using (NpgsqlCommand command = new(
            "DELETE FROM node_mapping WHERE node_id = ANY(@ids)",
            connection, transaction))
        {
            command.Parameters.AddWithValue("ids", mappings.Keys.ToArray());
            await command.ExecuteNonQueryAsync();
        }

        List<int> nodeIds = [];
        List<string> scopes = [];
        List<string> targets = [];
        List<string?> labels = [];
        List<string?> filteredLabels = [];
        foreach (KeyValuePair<int, IList<TaxoNodeMapping>> p in mappings)
        {
            foreach (TaxoNodeMapping mapping in p.Value)
            {
                nodeIds.Add(p.Key);
                scopes.Add(mapping.Scope);
                targets.Add(mapping.Target);
                string? label = string.IsNullOrEmpty(mapping.Label)
                    ? null : mapping.Label;
                labels.Add(label);
                // as for nodes, fall back to label when not filtered
                filteredLabels.Add(string.IsNullOrEmpty(mapping.FilteredLabel)
                    ? label : mapping.FilteredLabel);
            }
        }
        if (nodeIds.Count == 0) return;

        // insert all the mappings with a single command
        await using NpgsqlCommand insert = new(
            "INSERT INTO node_mapping " +
            "(node_id, scope, target, label, label_ix) " +
            "SELECT * FROM unnest(@nodeIds, @scopes, @targets, @labels, " +
            "@filteredLabels)",
            connection, transaction);
        insert.Parameters.AddWithValue("nodeIds", nodeIds.ToArray());
        insert.Parameters.Add(new NpgsqlParameter("scopes",
            NpgsqlDbType.Array | NpgsqlDbType.Varchar)
            { Value = scopes.ToArray() });
        insert.Parameters.Add(new NpgsqlParameter("targets",
            NpgsqlDbType.Array | NpgsqlDbType.Varchar)
            { Value = targets.ToArray() });
        insert.Parameters.Add(new NpgsqlParameter("labels",
            NpgsqlDbType.Array | NpgsqlDbType.Varchar)
            { Value = labels.ToArray() });
        insert.Parameters.Add(new NpgsqlParameter("filteredLabels",
            NpgsqlDbType.Array | NpgsqlDbType.Varchar)
            { Value = filteredLabels.ToArray() });
        await insert.ExecuteNonQueryAsync();
    }

    private static NpgsqlBatchCommand CreateUpsertCommand(TaxoNode node)
    {
        NpgsqlBatchCommand command = new(node.Id == 0
            // insert new node
            ? "INSERT INTO node " +
              "(parent_id, tree_id, key, label, label_ix, flags, note) " +
              "VALUES (@parentId, @treeId, @key, @label, " +
              "@labelIx, @flags, @note) RETURNING id"
            // update or insert with specific ID
            : "INSERT INTO node " +
              "(id, parent_id, tree_id, key, label, label_ix, flags, note) " +
              "VALUES (@id, @parentId, @treeId, @key, @label, " +
              "@labelIx, @flags, @note) " +
              "ON CONFLICT (id) DO UPDATE " +
              "SET parent_id = EXCLUDED.parent_id, " +
              "tree_id = EXCLUDED.tree_id, key = EXCLUDED.key, " +
              "label = EXCLUDED.label, label_ix = EXCLUDED.label_ix, " +
              "flags = EXCLUDED.flags, note = EXCLUDED.note " +
              "RETURNING id");

        if (node.Id != 0) command.Parameters.AddWithValue("id", node.Id);
        command.Parameters.AddWithValue("parentId",
            (object?)node.ParentId ?? DBNull.Value);
        command.Parameters.AddWithValue("treeId", node.TreeId);
        command.Parameters.AddWithValue("key", node.Key);
        command.Parameters.AddWithValue("label", node.Label);
        command.Parameters.AddWithValue("labelIx",
            string.IsNullOrEmpty(node.FilteredLabel)
                ? node.Label : node.FilteredLabel);
        command.Parameters.AddWithValue("flags", node.Flags ?? "");
        command.Parameters.AddWithValue("note",
            (object?)node.Note ?? DBNull.Value);
        return command;
    }

    /// <summary>
    /// Ensures that the node ID sequence is beyond any existing node ID.
    /// This is required after inserting nodes with an explicit ID, which
    /// does not advance the sequence: otherwise, the next insertion of a
    /// new node could get an already used ID. The sequence never moves
    /// backwards, so this is safe with concurrent insertions.
    /// </summary>
    private static async Task SyncNodeSequenceAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        const string sql =
            "SELECT setval('node_id_seq', GREATEST(" +
            "(SELECT COALESCE(MAX(id), 1) FROM node), " +
            "(SELECT last_value FROM node_id_seq)))";

        await using NpgsqlCommand command = new(sql, connection, transaction);
        await command.ExecuteScalarAsync();
    }

    /// <summary>
    /// Validates that none of the specified nodes, when updating an existing
    /// node, changes its key. Once a node exists, its key is immutable,
    /// because external data (e.g. Cadmus parts) refer to nodes by tree ID
    /// and key.
    /// </summary>
    /// <exception cref="ArgumentException">Key changed.</exception>
    private static async Task ValidateKeysUnchangedAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction,
        IList<TaxoNode> nodes)
    {
        List<TaxoNode> updated = [.. nodes.Where(n => n.Id > 0)];
        if (updated.Count == 0) return;

        const string sql =
            "SELECT n.id, n.key, w.key FROM node n " +
            "JOIN unnest(@ids, @keys) AS w(id, key) ON w.id = n.id " +
            "WHERE n.key <> w.key LIMIT 1";

        await using NpgsqlCommand command = new(sql, connection, transaction);
        command.Parameters.AddWithValue("ids",
            updated.Select(n => n.Id).ToArray());
        command.Parameters.AddWithValue("keys",
            updated.Select(n => n.Key).ToArray());

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            throw new ArgumentException(
                $"The key of node {reader.GetInt32(0)} cannot be changed " +
                $"(from \"{reader.GetString(1)}\" to \"{reader.GetString(2)}\")");
        }
    }

    /// <summary>
    /// Validates the hierarchy of the specified (just written) nodes: each
    /// node must belong to the same tree of its parent and children, must
    /// not be its own ancestor, and must have no parent in a flat tree.
    /// </summary>
    /// <exception cref="ArgumentException">Invalid hierarchy.</exception>
    private static async Task ValidateHierarchyAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction,
        IList<int> ids)
    {
        const string treeSql =
            "SELECT n.id FROM node n WHERE n.id = ANY(@ids) AND (" +
            "EXISTS (SELECT 1 FROM node p " +
            "WHERE p.id = n.parent_id AND p.tree_id <> n.tree_id) " +
            "OR EXISTS (SELECT 1 FROM node c " +
            "WHERE c.parent_id = n.id AND c.tree_id <> n.tree_id)) LIMIT 1";

        await using (NpgsqlCommand command =
            new(treeSql, connection, transaction))
        {
            command.Parameters.AddWithValue("ids", ids.ToArray());
            if (await command.ExecuteScalarAsync() is int id)
            {
                throw new ArgumentException(
                    $"Node {id} belongs to a tree different from its " +
                    "parent or children");
            }
        }

        const string flatSql =
            "SELECT n.id FROM node n JOIN tree t ON t.id = n.tree_id " +
            "WHERE n.id = ANY(@ids) AND n.parent_id IS NOT NULL AND t.flat " +
            "LIMIT 1";

        await using (NpgsqlCommand command =
            new(flatSql, connection, transaction))
        {
            command.Parameters.AddWithValue("ids", ids.ToArray());
            if (await command.ExecuteScalarAsync() is int id)
            {
                throw new ArgumentException(
                    $"Node {id} cannot have a parent because its tree is flat");
            }
        }

        const string cycleSql =
            "WITH RECURSIVE up(start_id, id, parent_id) AS (" +
            "SELECT id, id, parent_id FROM node " +
            "WHERE id = ANY(@ids) AND parent_id IS NOT NULL " +
            "UNION ALL " +
            "SELECT up.start_id, p.id, p.parent_id FROM up " +
            "JOIN node p ON p.id = up.parent_id" +
            ") CYCLE id SET is_cycle USING cycle_path " +
            "SELECT start_id FROM up WHERE is_cycle LIMIT 1";

        await using (NpgsqlCommand command =
            new(cycleSql, connection, transaction))
        {
            command.Parameters.AddWithValue("ids", ids.ToArray());
            if (await command.ExecuteScalarAsync() is int id)
            {
                throw new ArgumentException(
                    $"Node {id} cannot be a descendant of itself");
            }
        }
    }

    /// <summary>
    /// Adds a new node (when its ID is 0) or updates an existing one (when
    /// its ID is greater than 0; if not found, it is added with that ID).
    /// The key of an existing node cannot be changed.
    /// When the node's mappings are not null, they replace all the mappings
    /// of that node; when null, its mappings are left untouched.
    /// </summary>
    /// <param name="node">The node to add.</param>
    /// <returns>The ID of the added or updated node.</returns>
    /// <exception cref="ArgumentException">Invalid node.</exception>
    /// <exception cref="TaxoStoreConflictException">Duplicate key.</exception>
    public async Task<int> AddNodeAsync(TaxoNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        IList<int> ids = await AddNodesAsync([node]);
        return ids[0];
    }

    /// <summary>
    /// Adds or updates the specified collection of nodes in a single
    /// transaction.
    /// </summary>
    /// <param name="nodes">The collection of nodes to add.</param>
    /// <returns>A list of identifiers assigned to the nodes.</returns>
    /// <exception cref="ArgumentException">Invalid node.</exception>
    /// <exception cref="TaxoStoreConflictException">Duplicate key.</exception>
    public async Task<IList<int>> AddNodesAsync(IEnumerable<TaxoNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);

        List<TaxoNode> list = [.. nodes];
        foreach (TaxoNode node in list) ValidateNode(node);
        if (list.Count == 0) return [];

        await using NpgsqlConnection connection = await OpenConnectionAsync();
        await using NpgsqlTransaction transaction =
            await connection.BeginTransactionAsync();

        try
        {
            await ValidateKeysUnchangedAsync(connection, transaction, list);

            List<int> ids = new(list.Count);

            // send upserts in batches to minimize round trips
            for (int i = 0; i < list.Count; i += WRITE_BATCH_SIZE)
            {
                await using NpgsqlBatch batch = new(connection, transaction);
                foreach (TaxoNode node in list.Skip(i).Take(WRITE_BATCH_SIZE))
                    batch.BatchCommands.Add(CreateUpsertCommand(node));

                await using NpgsqlDataReader reader =
                    await batch.ExecuteReaderAsync();
                do
                {
                    while (await reader.ReadAsync()) ids.Add(reader.GetInt32(0));
                } while (await reader.NextResultAsync());
            }

            if (list.Any(n => n.Id > 0))
                await SyncNodeSequenceAsync(connection, transaction);

            await ValidateHierarchyAsync(connection, transaction, ids);

            await SaveMappingsAsync(connection, transaction, list, ids);

            await transaction.CommitAsync();
            return ids;
        }
        catch (PostgresException ex) when (TranslateWriteException(ex)
            is Exception translated)
        {
            throw translated;
        }
    }

    /// <summary>
    /// Deletes the node with the specified identifier, with all its
    /// descendants.
    /// </summary>
    /// <param name="id">The unique identifier of the node to delete.</param>
    /// <returns>The ID of the deleted node, or 0 if the node was not found.
    /// </returns>
    public async Task<int> DeleteNodeAsync(int id)
    {
        const string sql = "DELETE FROM node WHERE id = @id";

        await using NpgsqlConnection connection = await OpenConnectionAsync();
        await using NpgsqlCommand command = new(sql, connection);
        command.Parameters.AddWithValue("id", id);

        int affected = await command.ExecuteNonQueryAsync();
        return affected > 0 ? id : 0;
    }

    /// <summary>
    /// Asynchronously removes all data from the store and resets sequences.
    /// </summary>
    /// <returns>A task that represents the asynchronous clear operation.
    /// </returns>
    public async Task ClearAsync()
    {
        await using NpgsqlConnection connection = await OpenConnectionAsync();
        await using NpgsqlCommand command = new(
            "TRUNCATE TABLE node_mapping, node, tree RESTART IDENTITY;",
            connection);
        await command.ExecuteNonQueryAsync();
    }
    #endregion
}
