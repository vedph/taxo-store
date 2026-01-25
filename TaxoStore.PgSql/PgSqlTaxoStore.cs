using Fusi.DbManager.PgSql;
using Fusi.Tools.Data;
using Npgsql;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using TaxoStore.Core;

namespace TaxoStore.PgSql;

/// <summary>
/// PostgreSQL implementation of <see cref="ITaxoStore"/>.
/// </summary>
public sealed class PgSqlTaxoStore : ITaxoStore, IDisposable
{
    private readonly TaxoStoreOptions _options;
    private readonly PgSqlDbManager _dbManager;
    private readonly string _connectionString;
    private bool _disposed;
    private bool _databaseReady;

    /// <summary>
    /// Initializes a new instance of the PgSqlTaxoStore class using the
    /// specified options.
    /// </summary>
    /// <param name="options">The options used to configure the tree store,
    /// including the PostgreSQL connection string.</param>
    /// <exception cref="ArgumentNullException">Thrown if the options parameter
    /// is null.</exception>
    public PgSqlTaxoStore(TaxoStoreOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));

        // replace DB name with placeholder
        NpgsqlConnectionStringBuilder builder = new(options.Source)
        {
            Database = "{0}"
        };
        string csTemplate = builder.ConnectionString;

        _dbManager = new PgSqlDbManager(csTemplate);
        _connectionString = options.Source;
        _databaseReady = false;
    }

    private void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            _disposed = true;
        }
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
        if (_databaseReady) return;

        // check if database exists
        string dbName = ExtractDbName(_connectionString) ??
            throw new InvalidOperationException(
                "Database name missing from connection string");
        bool existing = _dbManager.Exists(dbName);
        if (!existing)
        {
            // create database and seed its schema from DDL SQL in assets
            string sql = LoadResourceText("TaxoStore.PgSql.Assets.Schema.pgsql");
            _dbManager.CreateDatabase(dbName, sql, null);
        }

        // seed if not existing and seed sources were provided
        if (!existing &&
            !string.IsNullOrEmpty(_options.SeedTreeSource) &&
            !string.IsNullOrEmpty(_options.SeedNodeSource))
        {
            TaxoTreeImporter importer = new(this);

            if (_options.SeedSourceAsText)
            {
                // create readers from direct CSV text
                using StringReader treeReader =
                    new(_options.SeedTreeSource);
                using StringReader nodeReader =
                    new(_options.SeedNodeSource);
                await importer.ImportAsync(treeReader, nodeReader);
            }
            else
            {
                // create readers from file paths
                using StreamReader treeReader =
                    new(_options.SeedTreeSource, Encoding.UTF8);
                using StreamReader nodeReader =
                    new(_options.SeedNodeSource, Encoding.UTF8);
                await importer.ImportAsync(treeReader, nodeReader);
            }
        }

        _databaseReady = true;
    }

    /// <summary>
    /// Ensure that the database is initialized.
    /// </summary>
    public async Task InitializeAsync()
    {
        await EnsureDatabaseReady();
    }

    /// <summary>
    /// Gets the tree with the specified ID.
    /// </summary>
    /// <param name="id">The tree's ID (key).</param>
    /// <returns>Tree or null if not found.</returns>
    public async Task<TaxoTree?> GetTreeAsync(string id)
    {
        await EnsureDatabaseReady();

        const string sql = "SELECT id, name, note FROM tree WHERE id = @id";

        using NpgsqlConnection connection = new(_connectionString);
        await connection.OpenAsync();

        using NpgsqlCommand command = new(sql, connection);
        command.Parameters.AddWithValue("@id", id);

        using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return ReadTree(reader);
        }

        return null;
    }

    private static TaxoTree ReadTree(NpgsqlDataReader reader)
    {
        return new TaxoTree
        {
            Id = reader.GetString(0),
            Name = reader.GetString(1),
            Note = reader.IsDBNull(2) ? null : reader.GetString(2)
        };
    }

    /// <summary>
    /// Adds or updates the specified tree.
    /// </summary>
    /// <param name="tree">The tree to add or update.</param>
    /// <returns>The ID (key) of the tree which was added.</returns>
    public async Task<string> AddTreeAsync(TaxoTree tree)
    {
        ArgumentNullException.ThrowIfNull(tree);

        await EnsureDatabaseReady();

        using NpgsqlConnection connection = new(_connectionString);
        await connection.OpenAsync();

        // Insert or update tree with specified ID
        const string sql =
            "INSERT INTO tree (id, name, note) " +
            "VALUES (@id, @name, @note) " +
            "ON CONFLICT (id) DO UPDATE " +
            "SET name = EXCLUDED.name, note = EXCLUDED.note " +
            "RETURNING id";

        using NpgsqlCommand command = new(sql, connection);
        command.Parameters.AddWithValue("@id", tree.Id);
        command.Parameters.AddWithValue("@name", tree.Name);
        command.Parameters.AddWithValue("@note",
            (object?)tree.Note ?? DBNull.Value);

        object? result = await command.ExecuteScalarAsync();
        return result?.ToString() ?? tree.Id;
    }

    /// <summary>
    /// Deletes the tree with the specified ID.
    /// </summary>
    /// <param name="id">The ID (key) of the tree to delete.</param>
    /// <returns>The ID (key) of the tree which was deleted, or null if it was
    /// not found.</returns>
    public async Task<string?> DeleteTreeAsync(string id)
    {
        await EnsureDatabaseReady();

        const string sql = "DELETE FROM tree WHERE id = @id";

        using NpgsqlConnection connection = new(_connectionString);
        await connection.OpenAsync();

        using NpgsqlCommand command = new(sql, connection);
        command.Parameters.AddWithValue("@id", id);

        int affected = await command.ExecuteNonQueryAsync();
        return affected > 0 ? id : null;
    }

    /// <summary>
    /// Retrieves a paged list of trees that match the specified filter
    /// criteria.
    /// </summary>
    /// <param name="filter">The filter criteria.</param>
    /// <returns>A page of trees.</returns>
    public async Task<DataPage<TaxoTree>> GetTreesAsync(TaxoTreeFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);

        await EnsureDatabaseReady();

        StringBuilder sql = new("SELECT id, name, note FROM tree");

        // Build WHERE clause
        if (!string.IsNullOrEmpty(filter.Name))
        {
            sql.Append(" WHERE name ILIKE @name");
        }

        // Add ORDER BY
        sql.Append(" ORDER BY name");

        // Get total count
        int total = await GetTreesCountAsync(filter);

        // Add pagination
        sql.Append(" LIMIT @limit OFFSET @offset");

        using NpgsqlConnection connection = new(_connectionString);
        await connection.OpenAsync();

        using NpgsqlCommand command = new(sql.ToString(), connection);

        if (!string.IsNullOrEmpty(filter.Name))
        {
            command.Parameters.AddWithValue("@name",
                $"%{filter.Name}%");
        }

        command.Parameters.AddWithValue("@limit", filter.PageSize);
        command.Parameters.AddWithValue("@offset",
            (filter.PageNumber - 1) * filter.PageSize);

        List<TaxoTree> trees = [];
        using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            trees.Add(ReadTree(reader));
        }

        return new DataPage<TaxoTree>(
            filter.PageNumber,
            filter.PageSize,
            total,
            trees);
    }

    private async Task<int> GetTreesCountAsync(TaxoTreeFilter filter)
    {
        StringBuilder sql = new("SELECT COUNT(*) FROM tree");

        if (!string.IsNullOrEmpty(filter.Name))
        {
            sql.Append(" WHERE name ILIKE @name");
        }

        using NpgsqlConnection connection = new(_connectionString);
        await connection.OpenAsync();

        using NpgsqlCommand command = new(sql.ToString(), connection);

        if (!string.IsNullOrEmpty(filter.Name))
        {
            command.Parameters.AddWithValue("@name",
                $"%{filter.Name}%");
        }

        object? result = await command.ExecuteScalarAsync();
        return result != null ? Convert.ToInt32(result) : 0;
    }

    /// <summary>
    /// Retrieves the root node(s) of the specified tree.
    /// </summary>
    /// <param name="treeId">The ID (key) of the tree.</param>
    /// <param name="options">Paging options. If <see cref="PagingOptions.PageSize"/>
    /// is 0, all root nodes are returned without paging.</param>
    /// <returns>A page of root nodes. When page size is 0, the page contains
    /// all root nodes.</returns>
    public async Task<DataPage<TaxoNode>> GetRootNodes(
        string treeId,
        PagingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        await EnsureDatabaseReady();

        // When pageSize is 0, return all root nodes without paging
        bool noPaging = options.PageSize == 0;

        string sql = noPaging
            ? "SELECT id, parent_id, tree_id, key, label, label_ix, flags, " +
              "note FROM node " +
              "WHERE tree_id = @treeId AND parent_id IS NULL " +
              "ORDER BY key"
            : "SELECT id, parent_id, tree_id, key, label, label_ix, flags, " +
              "note FROM node " +
              "WHERE tree_id = @treeId AND parent_id IS NULL " +
              "ORDER BY key " +
              "LIMIT @limit OFFSET @offset";

        const string countSql =
            "SELECT COUNT(*) FROM node " +
            "WHERE tree_id = @treeId AND parent_id IS NULL";

        using NpgsqlConnection connection = new(_connectionString);
        await connection.OpenAsync();

        // Get total count
        int total;
        using (NpgsqlCommand countCommand = new(countSql, connection))
        {
            countCommand.Parameters.AddWithValue("@treeId", treeId);
            object? result = await countCommand.ExecuteScalarAsync();
            total = result != null ? Convert.ToInt32(result) : 0;
        }

        // Get nodes
        List<TaxoNode> nodes = [];
        using (NpgsqlCommand command = new(sql, connection))
        {
            command.Parameters.AddWithValue("@treeId", treeId);
            if (!noPaging)
            {
                command.Parameters.AddWithValue("@limit", options.PageSize);
                command.Parameters.AddWithValue("@offset",
                    (options.PageNumber - 1) * options.PageSize);
            }

            using NpgsqlDataReader reader =
                await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                nodes.Add(ReadNode(reader));
            }
        }

        return new DataPage<TaxoNode>(
            options.PageNumber,
            noPaging ? total : options.PageSize,
            total,
            nodes);
    }

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
    /// Retrieves the node with the specified identifier.
    /// </summary>
    /// <param name="id">The ID of the node to retrieve.</param>
    /// <returns>The node or null if not found.</returns>
    public async Task<TaxoNode?> GetNodeAsync(int id)
    {
        await EnsureDatabaseReady();

        const string sql =
            "SELECT id, parent_id, tree_id, key, label, label_ix, flags, " +
            "note FROM node WHERE id = @id";

        using NpgsqlConnection connection = new(_connectionString);
        await connection.OpenAsync();

        using NpgsqlCommand command = new(sql, connection);
        command.Parameters.AddWithValue("@id", id);

        using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return ReadNode(reader);
        }

        return null;
    }

    /// <summary>
    /// Retrieves the node associated with the specified key from the given tree.
    /// </summary>
    /// <param name="treeId">The ID (key) of the tree to search.</param>
    /// <param name="key">The key of the node to retrieve.</param>
    /// <returns>A task that represents the asynchronous operation. The task
    /// result contains the node associated with the specified key, or null
    /// if no such node exists.</returns>
    public async Task<TaxoNode?> GetNodeFromKeyAsync(string treeId, string key)
    {
        await EnsureDatabaseReady();

        const string sql =
            "SELECT id, parent_id, tree_id, key, label, label_ix, " +
            "flags, note " +
            "FROM node " +
            "WHERE tree_id = @treeId AND key = @key";

        using NpgsqlConnection connection = new(_connectionString);
        await connection.OpenAsync();

        using NpgsqlCommand command = new(sql, connection);
        command.Parameters.AddWithValue("@treeId", treeId);
        command.Parameters.AddWithValue("@key", key);

        using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return ReadNode(reader);
        }

        return null;
    }

    /// <summary>
    /// Retrieves a paged list of nodes that match the specified filter
    /// criteria.
    /// </summary>
    /// <param name="filter">The filter criteria.</param>
    /// <returns>A page of nodes.</returns>
    public async Task<DataPage<TaxoNode>> GetNodesAsync(TaxoNodeFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);

        await EnsureDatabaseReady();

        StringBuilder sql = new(
            "SELECT n.id, n.parent_id, n.tree_id, n.key, n.label, " +
            "n.label_ix, n.flags, n.note FROM node n");

        List<string> whereClauses = [];
        List<NpgsqlParameter> parameters = [];

        // TreeId filter
        if (!string.IsNullOrEmpty(filter.TreeId))
        {
            whereClauses.Add("n.tree_id = @treeId");
            parameters.Add(
                new NpgsqlParameter("@treeId", filter.TreeId));
        }

        // ParentId filter
        if (filter.ParentId.HasValue)
        {
            whereClauses.Add("n.parent_id = @parentId");
            parameters.Add(
                new NpgsqlParameter("@parentId", filter.ParentId.Value));
        }

        // Key filter
        if (!string.IsNullOrEmpty(filter.Key))
        {
            whereClauses.Add("n.key ILIKE @key");
            parameters.Add(
                new NpgsqlParameter("@key", $"%{filter.Key}%"));
        }

        // ParentKey filter
        if (!string.IsNullOrEmpty(filter.ParentKey))
        {
            sql.Append(" JOIN node p ON n.parent_id = p.id");
            whereClauses.Add("p.key ILIKE @parentKey");
            parameters.Add(
                new NpgsqlParameter("@parentKey", $"%{filter.ParentKey}%"));
        }

        // AncestorKey filter (requires recursive CTE)
        if (!string.IsNullOrEmpty(filter.AncestorKey))
        {
            sql.Insert(0,
                "WITH RECURSIVE ancestors AS (" +
                "  SELECT id, parent_id, tree_id, key, label, " +
                "label_ix, flags, note " +
                "  FROM node " +
                "  WHERE key ILIKE @ancestorKey " +
                "  UNION ALL " +
                "  SELECT n.id, n.parent_id, n.tree_id, n.key, n.label, " +
                "n.label_ix, n.flags, n.note " +
                "  FROM node n " +
                "  INNER JOIN ancestors a ON n.parent_id = a.id" +
                ") ");
            sql.Replace("FROM node n", "FROM ancestors n");
            parameters.Add(
                new NpgsqlParameter("@ancestorKey",
                    $"%{filter.AncestorKey}%"));
        }

        // FilteredLabel filter
        if (!string.IsNullOrEmpty(filter.FilteredLabel))
        {
            whereClauses.Add("n.label_ix ILIKE @filteredLabel");
            parameters.Add(
                new NpgsqlParameter("@filteredLabel",
                    $"%{filter.FilteredLabel}%"));
        }

        // Flags filter
        if (!string.IsNullOrEmpty(filter.Flags))
        {
            switch (filter.FlagMatchMode)
            {
                case NodeFlagMatchMode.All:
                    // All flags must be present
                    foreach (char flag in filter.Flags)
                    {
                        string paramName = $"@flag{flag}";
                        whereClauses.Add($"n.flags LIKE {paramName}");
                        parameters.Add(
                            new NpgsqlParameter(paramName, $"%{flag}%"));
                    }
                    break;

                case NodeFlagMatchMode.Any:
                    // At least one flag must be present
                    List<string> flagConditions = [];
                    foreach (char flag in filter.Flags)
                    {
                        string paramName = $"@flag{flag}";
                        flagConditions.Add($"n.flags LIKE {paramName}");
                        parameters.Add(
                            new NpgsqlParameter(paramName, $"%{flag}%"));
                    }
                    if (flagConditions.Count > 0)
                    {
                        whereClauses.Add(
                            $"({string.Join(" OR ", flagConditions)})");
                    }
                    break;
            }
        }

        // IsLeaf filter
        if (filter.IsLeaf.HasValue)
        {
            if (filter.IsLeaf.Value)
            {
                whereClauses.Add(
                    "NOT EXISTS (SELECT 1 FROM node c " +
                    "WHERE c.parent_id = n.id)");
            }
            else
            {
                whereClauses.Add(
                    "EXISTS (SELECT 1 FROM node c " +
                    "WHERE c.parent_id = n.id)");
            }
        }

        // Build WHERE clause
        if (whereClauses.Count > 0)
        {
            sql.Append(" WHERE ").Append(string.Join(" AND ", whereClauses));
        }

        // Order by key
        sql.Append(" ORDER BY n.key");

        // Get total count
        int total = await GetNodesCountAsync(filter);

        // Add pagination
        sql.Append(" LIMIT @limit OFFSET @offset");
        parameters.Add(new NpgsqlParameter("@limit", filter.PageSize));
        parameters.Add(new NpgsqlParameter("@offset",
            (filter.PageNumber - 1) * filter.PageSize));

        using NpgsqlConnection connection = new(_connectionString);
        await connection.OpenAsync();

        using NpgsqlCommand command = new(sql.ToString(), connection);
        command.Parameters.AddRange(parameters.ToArray());

        List<TaxoNode> nodes = [];
        using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            nodes.Add(ReadNode(reader));
        }

        return new DataPage<TaxoNode>(
            filter.PageNumber,
            filter.PageSize,
            total,
            nodes);
    }

    private async Task<int> GetNodesCountAsync(TaxoNodeFilter filter)
    {
        StringBuilder sql = new("SELECT COUNT(*) FROM node n");

        List<string> whereClauses = [];
        List<NpgsqlParameter> parameters = [];

        // Apply same filters as GetNodesAsync
        if (!string.IsNullOrEmpty(filter.TreeId))
        {
            whereClauses.Add("n.tree_id = @treeId");
            parameters.Add(
                new NpgsqlParameter("@treeId", filter.TreeId));
        }

        if (filter.ParentId.HasValue)
        {
            whereClauses.Add("n.parent_id = @parentId");
            parameters.Add(
                new NpgsqlParameter("@parentId", filter.ParentId.Value));
        }

        if (!string.IsNullOrEmpty(filter.Key))
        {
            whereClauses.Add("n.key ILIKE @key");
            parameters.Add(
                new NpgsqlParameter("@key", $"%{filter.Key}%"));
        }

        if (!string.IsNullOrEmpty(filter.ParentKey))
        {
            sql.Append(" JOIN node p ON n.parent_id = p.id");
            whereClauses.Add("p.key ILIKE @parentKey");
            parameters.Add(
                new NpgsqlParameter("@parentKey", $"%{filter.ParentKey}%"));
        }

        if (!string.IsNullOrEmpty(filter.AncestorKey))
        {
            sql.Insert(0,
                "WITH RECURSIVE ancestors AS (" +
                "  SELECT id FROM node WHERE key ILIKE @ancestorKey " +
                "  UNION ALL " +
                "  SELECT n.id FROM node n " +
                "  INNER JOIN ancestors a ON n.parent_id = a.id" +
                ") SELECT COUNT(*) FROM ancestors n WHERE 1=1 ");
            parameters.Add(
                new NpgsqlParameter("@ancestorKey",
                    $"%{filter.AncestorKey}%"));
            whereClauses.Clear();
        }

        if (!string.IsNullOrEmpty(filter.FilteredLabel))
        {
            whereClauses.Add("n.label_ix ILIKE @filteredLabel");
            parameters.Add(
                new NpgsqlParameter("@filteredLabel",
                    $"%{filter.FilteredLabel}%"));
        }

        if (!string.IsNullOrEmpty(filter.Flags))
        {
            switch (filter.FlagMatchMode)
            {
                case NodeFlagMatchMode.All:
                    foreach (char flag in filter.Flags)
                    {
                        string paramName = $"@flag{flag}";
                        whereClauses.Add($"n.flags LIKE {paramName}");
                        parameters.Add(
                            new NpgsqlParameter(paramName, $"%{flag}%"));
                    }
                    break;

                case NodeFlagMatchMode.Any:
                    List<string> flagConditions = [];
                    foreach (char flag in filter.Flags)
                    {
                        string paramName = $"@flag{flag}";
                        flagConditions.Add($"n.flags LIKE {paramName}");
                        parameters.Add(
                            new NpgsqlParameter(paramName, $"%{flag}%"));
                    }
                    if (flagConditions.Count > 0)
                    {
                        whereClauses.Add(
                            $"({string.Join(" OR ", flagConditions)})");
                    }
                    break;
            }
        }

        if (filter.IsLeaf.HasValue)
        {
            if (filter.IsLeaf.Value)
            {
                whereClauses.Add(
                    "NOT EXISTS (SELECT 1 FROM node c " +
                    "WHERE c.parent_id = n.id)");
            }
            else
            {
                whereClauses.Add(
                    "EXISTS (SELECT 1 FROM node c " +
                    "WHERE c.parent_id = n.id)");
            }
        }

        if (whereClauses.Count > 0)
        {
            sql.Append(" WHERE ").Append(string.Join(" AND ", whereClauses));
        }

        using NpgsqlConnection connection = new(_connectionString);
        await connection.OpenAsync();

        using NpgsqlCommand command = new(sql.ToString(), connection);
        command.Parameters.AddRange(parameters.ToArray());

        object? result = await command.ExecuteScalarAsync();
        return result != null ? Convert.ToInt32(result) : 0;
    }

    /// <summary>
    /// Adds a new node to the collection.
    /// </summary>
    /// <param name="node">The node to add.</param>
    /// <returns>The unique identifier assigned to the newly added node.
    /// </returns>
    public async Task<int> AddNodeAsync(TaxoNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        await EnsureDatabaseReady();

        using NpgsqlConnection connection = new(_connectionString);
        await connection.OpenAsync();

        if (node.Id == 0)
        {
            // Insert new node
            const string sql =
                "INSERT INTO node " +
                "(parent_id, tree_id, key, label, label_ix, flags, note) " +
                "VALUES (@parentId, @treeId, @key, @label, " +
                "@labelIx, @flags, @note) RETURNING id";

            using NpgsqlCommand command = new(sql, connection);
            command.Parameters.AddWithValue("@parentId",
                (object?)node.ParentId ?? DBNull.Value);
            command.Parameters.AddWithValue("@treeId", node.TreeId);
            command.Parameters.AddWithValue("@key", node.Key);
            command.Parameters.AddWithValue("@label", node.Label);
            command.Parameters.AddWithValue("@labelIx",
                node.FilteredLabel);
            command.Parameters.AddWithValue("@flags",
                node.Flags ?? "");
            command.Parameters.AddWithValue("@note",
                (object?)node.Note ?? DBNull.Value);

            object? result = await command.ExecuteScalarAsync();
            return result != null ? Convert.ToInt32(result) : 0;
        }
        else
        {
            // Update or insert with specific ID
            const string sql =
                "INSERT INTO node " +
                "(id, parent_id, tree_id, key, label, label_ix, flags, note) " +
                "VALUES (@id, @parentId, @treeId, @key, @label, " +
                "@labelIx, @flags, @note) " +
                "ON CONFLICT (id) DO UPDATE " +
                "SET parent_id = EXCLUDED.parent_id, " +
                "tree_id = EXCLUDED.tree_id, key = EXCLUDED.key, " +
                "label = EXCLUDED.label, label_ix = EXCLUDED.label_ix, " +
                "flags = EXCLUDED.flags, note = EXCLUDED.note " +
                "RETURNING id";

            using NpgsqlCommand command = new(sql, connection);
            command.Parameters.AddWithValue("@id", node.Id);
            command.Parameters.AddWithValue("@parentId",
                (object?)node.ParentId ?? DBNull.Value);
            command.Parameters.AddWithValue("@treeId", node.TreeId);
            command.Parameters.AddWithValue("@key", node.Key);
            command.Parameters.AddWithValue("@label", node.Label);
            command.Parameters.AddWithValue("@labelIx",
                node.FilteredLabel);
            command.Parameters.AddWithValue("@flags",
                node.Flags ?? "");
            command.Parameters.AddWithValue("@note",
                (object?)node.Note ?? DBNull.Value);

            object? result = await command.ExecuteScalarAsync();
            return result != null ? Convert.ToInt32(result) : node.Id;
        }
    }

    /// <summary>
    /// Deletes the node with the specified identifier.
    /// </summary>
    /// <param name="id">The unique identifier of the node to delete.</param>
    /// <returns>The ID of the deleted node, or 0 if the node was not found.
    /// </returns>
    public async Task<int> DeleteNodeAsync(int id)
    {
        await EnsureDatabaseReady();

        const string sql = "DELETE FROM node WHERE id = @id";

        using NpgsqlConnection connection = new(_connectionString);
        await connection.OpenAsync();

        using NpgsqlCommand command = new(sql, connection);
        command.Parameters.AddWithValue("@id", id);

        int affected = await command.ExecuteNonQueryAsync();
        return affected > 0 ? id : 0;
    }

    /// <summary>
    /// Adds the specified collection of nodes to the data store.
    /// </summary>
    /// <param name="nodes">The collection of nodes to add.</param>
    /// <returns>A list of identifiers assigned to the nodes.</returns>
    public async Task<IList<int>> AddNodesAsync(IEnumerable<TaxoNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);

        await EnsureDatabaseReady();

        List<int> ids = [];

        using NpgsqlConnection connection = new(_connectionString);
        await connection.OpenAsync();

        foreach (TaxoNode node in nodes)
        {
            int id = await AddNodeInternalAsync(connection, node);
            ids.Add(id);
        }

        return ids;
    }

    private static async Task<int> AddNodeInternalAsync(
        NpgsqlConnection connection,
        TaxoNode node)
    {
        if (node.Id == 0)
        {
            // Insert new node
            const string sql =
                "INSERT INTO node " +
                "(parent_id, tree_id, key, label, label_ix, flags, note) " +
                "VALUES (@parentId, @treeId, @key, @label, " +
                "@labelIx, @flags, @note) RETURNING id";

            using NpgsqlCommand command = new(sql, connection);
            command.Parameters.AddWithValue("@parentId",
                (object?)node.ParentId ?? DBNull.Value);
            command.Parameters.AddWithValue("@treeId", node.TreeId);
            command.Parameters.AddWithValue("@key", node.Key);
            command.Parameters.AddWithValue("@label", node.Label);
            command.Parameters.AddWithValue("@labelIx",
                node.FilteredLabel);
            command.Parameters.AddWithValue("@flags",
                node.Flags ?? "");
            command.Parameters.AddWithValue("@note",
                (object?)node.Note ?? DBNull.Value);

            object? result = await command.ExecuteScalarAsync();
            return result != null ? Convert.ToInt32(result) : 0;
        }
        else
        {
            // Update or insert with specific ID
            const string sql =
                "INSERT INTO node " +
                "(id, parent_id, tree_id, key, label, label_ix, flags, note) " +
                "VALUES (@id, @parentId, @treeId, @key, @label, " +
                "@labelIx, @flags, @note) " +
                "ON CONFLICT (id) DO UPDATE " +
                "SET parent_id = EXCLUDED.parent_id, " +
                "tree_id = EXCLUDED.tree_id, key = EXCLUDED.key, " +
                "label = EXCLUDED.label, label_ix = EXCLUDED.label_ix, " +
                "flags = EXCLUDED.flags, note = EXCLUDED.note " +
                "RETURNING id";

            using NpgsqlCommand command = new(sql, connection);
            command.Parameters.AddWithValue("@id", node.Id);
            command.Parameters.AddWithValue("@parentId",
                (object?)node.ParentId ?? DBNull.Value);
            command.Parameters.AddWithValue("@treeId", node.TreeId);
            command.Parameters.AddWithValue("@key", node.Key);
            command.Parameters.AddWithValue("@label", node.Label);
            command.Parameters.AddWithValue("@labelIx",
                node.FilteredLabel);
            command.Parameters.AddWithValue("@flags",
                node.Flags ?? "");
            command.Parameters.AddWithValue("@note",
                (object?)node.Note ?? DBNull.Value);

            object? result = await command.ExecuteScalarAsync();
            return result != null ? Convert.ToInt32(result) : node.Id;
        }
    }

    /// <summary>
    /// Determines whether the node with the specified identifier has any
    /// child nodes.
    /// </summary>
    /// <param name="id">The ID of the node to check for child nodes.</param>
    /// <returns>True if the node has children; otherwise, false.</returns>
    public async Task<bool> NodeHasChildrenAsync(int id)
    {
        await EnsureDatabaseReady();

        const string sql =
            "SELECT EXISTS(SELECT 1 FROM node WHERE parent_id = @id)";

        using NpgsqlConnection connection = new(_connectionString);
        await connection.OpenAsync();

        using NpgsqlCommand command = new(sql, connection);
        command.Parameters.AddWithValue("@id", id);

        object? result = await command.ExecuteScalarAsync();
        return result != null && (bool)result;
    }

    /// <summary>
    /// Retrieves the collection of child nodes for the specified parent node.
    /// </summary>
    /// <param name="parentId">The identifier of the parent node.</param>
    /// <returns>A list of child nodes, sorted by their key.</returns>
    public async Task<IList<TaxoNode>> GetChildNodesAsync(int parentId)
    {
        await EnsureDatabaseReady();

        const string sql =
            "SELECT id, parent_id, tree_id, key, label, label_ix, flags, " +
            "note FROM node WHERE parent_id = @parentId ORDER BY key";

        using NpgsqlConnection connection = new(_connectionString);
        await connection.OpenAsync();

        using NpgsqlCommand command = new(sql, connection);
        command.Parameters.AddWithValue("@parentId", parentId);

        List<TaxoNode> nodes = [];
        using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            nodes.Add(ReadNode(reader));
        }

        return nodes;
    }

    /// <summary>
    /// Retrieves all descendant nodes of the specified parent node using
    /// a recursive query.
    /// </summary>
    /// <param name="parentId">The identifier of the parent node.</param>
    /// <returns>A list of descendant nodes in traversal order.</returns>
    public async Task<IList<TaxoNode>> GetDescendantNodesAsync(int parentId)
    {
        await EnsureDatabaseReady();

        const string sql =
            "WITH RECURSIVE descendants AS (" +
            "  SELECT id, parent_id, tree_id, key, label, label_ix, " +
            "flags, note, 0 AS level " +
            "  FROM node WHERE parent_id = @parentId " +
            "  UNION ALL " +
            "  SELECT n.id, n.parent_id, n.tree_id, n.key, n.label, " +
            "n.label_ix, n.flags, n.note, d.level + 1 " +
            "  FROM node n " +
            "  INNER JOIN descendants d ON n.parent_id = d.id" +
            ") " +
            "SELECT id, parent_id, tree_id, key, label, label_ix, flags, " +
            "note FROM descendants ORDER BY level, key";

        using NpgsqlConnection connection = new(_connectionString);
        await connection.OpenAsync();

        using NpgsqlCommand command = new(sql, connection);
        command.Parameters.AddWithValue("@parentId", parentId);

        List<TaxoNode> nodes = [];
        using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            nodes.Add(ReadNode(reader));
        }

        return nodes;
    }

    /// <summary>
    /// Retrieves all ancestor nodes of the specified node, ordered from the
    /// immediate parent up to the root.
    /// </summary>
    /// <param name="nodeId">The identifier of the node.</param>
    /// <returns>A list of ancestor nodes.</returns>
    public async Task<IList<TaxoNode>> GetAncestorNodesAsync(int nodeId)
    {
        await EnsureDatabaseReady();

        const string sql =
            "WITH RECURSIVE ancestors AS (" +
            "  SELECT n.id, n.parent_id, n.tree_id, n.key, n.label, " +
            "n.label_ix, n.flags, n.note, 0 AS level " +
            "  FROM node n " +
            "  WHERE n.id = @nodeId " +
            "  UNION ALL " +
            "  SELECT p.id, p.parent_id, p.tree_id, p.key, p.label, " +
            "p.label_ix, p.flags, p.note, a.level + 1 " +
            "  FROM node p " +
            "  INNER JOIN ancestors a ON p.id = a.parent_id" +
            ") " +
            "SELECT id, parent_id, tree_id, key, label, label_ix, flags, " +
            "note FROM ancestors WHERE level > 0 ORDER BY level";

        using NpgsqlConnection connection = new(_connectionString);
        await connection.OpenAsync();

        using NpgsqlCommand command = new(sql, connection);
        command.Parameters.AddWithValue("@nodeId", nodeId);

        List<TaxoNode> nodes = [];
        using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            nodes.Add(ReadNode(reader));
        }

        return nodes;
    }

    /// <summary>
    /// Retrieves the path from the root node to the specified target node,
    /// including the page number for each step.
    /// </summary>
    /// <param name="nodeId">The identifier of the target node.</param>
    /// <param name="pageSize">The page size used to calculate page numbers.</param>
    /// <returns>A list of path steps from root to target.</returns>
    public async Task<IList<TaxoNodePathStep>> GetNodePathAsync(int nodeId, int pageSize)
    {
        if (pageSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(pageSize),
                "Page size must be greater than 0");

        await EnsureDatabaseReady();

        // This query:
        // 1. Uses a recursive CTE to get the path from target to root
        // 2. For each node, calculates its 1-based position among siblings
        //    (siblings are nodes with the same parent, or all roots if parent is null)
        // 3. Orders results from root to target (by descending level)
        const string sql =
            "WITH RECURSIVE path AS (" +
            "  SELECT n.id, n.parent_id, n.tree_id, n.key, 0 AS level " +
            "  FROM node n " +
            "  WHERE n.id = @nodeId " +
            "  UNION ALL " +
            "  SELECT p.id, p.parent_id, p.tree_id, p.key, path.level + 1 " +
            "  FROM node p " +
            "  INNER JOIN path ON p.id = path.parent_id" +
            ") " +
            "SELECT " +
            "  p.id, " +
            "  (" +
            "    SELECT COUNT(*) FROM node s " +
            "    WHERE s.tree_id = p.tree_id " +
            "    AND (" +
            "      (p.parent_id IS NULL AND s.parent_id IS NULL) " +
            "      OR s.parent_id = p.parent_id" +
            "    ) " +
            "    AND s.key <= p.key" +
            "  ) AS sibling_position " +
            "FROM path p " +
            "ORDER BY p.level DESC";

        using NpgsqlConnection connection = new(_connectionString);
        await connection.OpenAsync();

        using NpgsqlCommand command = new(sql, connection);
        command.Parameters.AddWithValue("@nodeId", nodeId);

        List<TaxoNodePathStep> steps = [];
        using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            int id = reader.GetInt32(0);
            int position = reader.GetInt32(1);
            // Calculate page number: 1-based, ceiling division
            int page = (position - 1) / pageSize + 1;
            steps.Add(new TaxoNodePathStep(id, page));
        }

        return steps;
    }

    /// <summary>
    /// Asynchronously removes all data from the store and resets sequences.
    /// </summary>
    /// <returns>A task that represents the asynchronous clear operation.
    /// </returns>
    public async Task ClearAsync()
    {
        await EnsureDatabaseReady();

        using NpgsqlConnection connection = new(_connectionString);
        await connection.OpenAsync();

        // Delete all data
        using (NpgsqlCommand deleteCommand = new(
            "DELETE FROM node; DELETE FROM tree;", connection))
        {
            await deleteCommand.ExecuteNonQueryAsync();
        }

        // Reset node sequence to start from 1
        // Note: tree table uses VARCHAR id, not SERIAL, so no sequence exists
        using NpgsqlCommand resetCommand = new(
            "ALTER SEQUENCE node_id_seq RESTART WITH 1;",
            connection);
        await resetCommand.ExecuteNonQueryAsync();
    }
}
