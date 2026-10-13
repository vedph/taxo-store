using CsvHelper;
using CsvHelper.Configuration;
using Fusi.Text.Unicode;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace TaxoStore.Core;

/// <summary>
/// Tree store importer. This class provides functionality to import tree and node
/// data from CSV files into a tree store implementing <see cref="ITaxoStore"/>.
/// </summary>
public sealed class TaxoTreeImporter
{
    private readonly ITaxoStore _store;
    private readonly ILogger _logger;
    private readonly UniData _ud;
    private readonly HashSet<char> _whiteChars;

    /// <summary>
    /// The delimiter used in CSV files.
    /// </summary>
    public string Delimiter { get; set; } = ",";

    /// <summary>
    /// The number of nodes to cache before bulk inserting them.
    /// </summary>
    public int CacheSize { get; set; } = 1000;

    /// <summary>
    /// Initializes a new instance of the TreeImporter class using the specified
    /// tree store as a target.
    /// </summary>
    /// <param name="store">The tree store to be used for importing tree data.
    /// </param>
    /// <param name="logger">An optional logger used to report import progress
    /// and diagnostics. When not provided, logging is disabled.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="store"/>
    /// is null.</exception>
    public TaxoTreeImporter(ITaxoStore store, ILogger? logger = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _logger = logger ?? NullLogger.Instance;
        _ud = new UniData();
        _whiteChars =
        [
            ' ', '-', '_', '/', '.', ',', ':', ';'
        ];
    }

    /// <summary>
    /// Filter a label according to the following rules:
    /// - remove diacritics from letters.
    /// - convert to lowercase.
    /// - keep letters, spaces (normalized), dashes, underscores, slashes,
    /// dots, commas, colons, semicolons.
    /// </summary>
    /// <param name="label"></param>
    /// <returns></returns>
    public string FilterLabel(string? label)
    {
        if (string.IsNullOrEmpty(label)) return "";

        StringBuilder sb = new();
        foreach (char c in label)
        {
            if (char.IsWhiteSpace(c)) sb.Append(' ');
            else if (char.IsLetter(c))
            {
                sb.Append(char.ToLowerInvariant(_ud.GetSegment(c, true)));
            }
            else if (_whiteChars.Contains(c))
            {
                sb.Append(c);
            }
        }
        return sb.ToString().Trim();
    }

    private static bool ParseBoolean(string? text)
    {
        return text?.Trim().ToLowerInvariant() is "true" or "1" or "yes" or "y";
    }

    /// <summary>
    /// Reads node mappings from the specified CSV reader, attaching each of
    /// them to the node it refers to.
    /// </summary>
    /// <returns>The count of mappings read.</returns>
    private int ReadMappings(TextReader mappingReader, CsvConfiguration config,
        List<TaxoTree> trees, List<TaxoNode> nodes)
    {
        using CsvReader csv = new(mappingReader, config);

        // read header and exit if none
        if (!csv.Read())
        {
            _logger.LogWarning(
                "Mapping CSV source is empty; no mappings imported");
            return 0;
        }
        csv.ReadHeader();

        Dictionary<string, TaxoNode> keyToNodes = [];
        foreach (TaxoNode node in nodes)
            keyToNodes.TryAdd($"{node.TreeId}#{node.Key}", node);

        int count = 0;
        while (csv.Read())
        {
            int treeN = csv.GetField<int>("tree_n");
            if (treeN < 1 || treeN > trees.Count)
                throw new Exception($"Invalid tree_n value {treeN} for mapping");

            string nodeKey = csv.GetField<string>("node_key")
                ?? throw new Exception("Mapping node key is required");
            if (!keyToNodes.TryGetValue($"{trees[treeN - 1].Id}#{nodeKey}",
                out TaxoNode? node))
            {
                throw new Exception(
                    $"Node key '{nodeKey}' not found for mapping in tree " +
                    $"'{trees[treeN - 1].Id}'");
            }

            string? label = csv.GetField<string?>("label");
            node.Mappings ??= [];
            node.Mappings.Add(new TaxoNodeMapping
            {
                Scope = csv.GetField<string>("scope")
                    ?? throw new Exception("Mapping scope is required"),
                Target = csv.GetField<string>("target")
                    ?? throw new Exception("Mapping target is required"),
                Label = string.IsNullOrEmpty(label) ? null : label
            });
            count++;
        }
        return count;
    }

    /// <summary>
    /// Imports tree and node data from the specified CSV readers and adds them
    /// to the data store.
    /// </summary>
    /// <remarks>Tree and node data are read from the provided CSV streams.
    /// Each node is associated with a tree using the 'tree_n' field, and
    /// parent-child relationships are resolved using 'parent_key'. The method
    /// performs bulk inserts for nodes to optimize performance. The input
    /// readers must be positioned at the start of the CSV data, including the
    /// header row.</remarks>
    /// <param name="treeReader">A <see cref="TextReader"/> providing CSV data
    /// for trees. The CSV must contain a header row and fields such as
    /// 'id', 'name' and 'note', plus an optional 'flat' field (true for
    /// flat trees, whose nodes cannot have a parent).</param>
    /// <param name="nodeReader">A <see cref="TextReader"/> providing CSV data
    /// for nodes. The CSV must contain a header row and fields such as
    /// 'tree_n', 'parent_key', 'key', 'label', 'filtered_label', and 'flags'.
    /// </param>
    /// <param name="mappingReader">An optional <see cref="TextReader"/>
    /// providing CSV data for node mappings. The CSV must contain a header
    /// row and fields 'tree_n', 'node_key', 'scope', 'target', and 'label'.
    /// Each mapping must refer to a node found in the nodes CSV.</param>
    /// <returns>The total number of nodes successfully imported and added to
    /// the data store. Returns 0 if no data is available.</returns>
    /// <exception cref="InvalidOperationException">Thrown if an error occurs
    /// while reading the CSV header or processing the input data.</exception>
    public async Task<int> ImportAsync(TextReader treeReader,
        TextReader nodeReader, TextReader? mappingReader = null)
    {
        CsvConfiguration config = new(CultureInfo.InvariantCulture)
        {
            Delimiter = Delimiter
        };

        _logger.LogInformation("Starting TaxoStore CSV import");

        int nodesImported = 0;
        try
        {
            // seed trees
            using CsvReader treeCsv = new(treeReader, config);
            List<TaxoTree> trees = [];

            // read header and exit if none
            if (!treeCsv.Read())
            {
                _logger.LogWarning(
                    "Tree CSV source is empty; import aborted");
                return 0;
            }
            treeCsv.ReadHeader();

            // read trees from CSV having fields id, name, note and
            // optionally flat
            while (treeCsv.Read())
            {
                TaxoTree tree = new()
                {
                    Id = treeCsv.GetField<string>("id")
                        ?? throw new Exception("Tree ID is required"),
                    Name = treeCsv.GetField<string>("name")
                        ?? throw new Exception("Tree name is required"),
                    Note = treeCsv.GetField<string?>("note"),
                    Flat = treeCsv.TryGetField("flat", out string? flat)
                        && ParseBoolean(flat)
                };
                await _store.AddTreeAsync(tree);
                trees.Add(tree);
            }
            _logger.LogInformation("Imported {Count} tree(s)", trees.Count);

            // seed nodes using two-pass approach:
            // Pass 1: Insert all nodes without parent IDs
            // Pass 2: Update parent IDs based on parent_key references
            List<TaxoNode> nodes = [];

            // track parent_key for each node to resolve in second pass
            List<string?> parentKeys = [];

            using CsvReader nodeCsv = new(nodeReader, config);

            // read header and exit if none
            if (!nodeCsv.Read())
            {
                _logger.LogWarning(
                    "Node CSV source is empty; no nodes imported");
                return 0;
            }
            nodeCsv.ReadHeader();

            // PASS 1: Read and insert all nodes without parent IDs
            while (nodeCsv.Read())
            {
                TaxoNode node = new();
                int treeN = nodeCsv.GetField<int>("tree_n");
                if (treeN < 1 || treeN > trees.Count)
                    throw new Exception($"Invalid tree_n value {treeN} for node");
                node.TreeId = trees[treeN - 1].Id;

                // store parent_key for later resolution
                string? parentKey = nodeCsv.GetField<string?>("parent_key");
                parentKeys.Add(parentKey);

                // key
                node.Key = nodeCsv.GetField<string>("key")
                    ?? throw new Exception("Node key is required");

                // nodes in a flat tree cannot have a parent
                if (trees[treeN - 1].Flat && !string.IsNullOrEmpty(parentKey))
                {
                    throw new Exception(
                        $"Node '{node.Key}' has parent key '{parentKey}' " +
                        $"but its tree '{node.TreeId}' is flat");
                }

                // label
                node.Label = nodeCsv.GetField<string>("label")
                    ?? throw new Exception("Node label is required");

                // filtered label (compute if missing)
                string? filteredLabel = nodeCsv.GetField<string?>("filtered_label");
                if (string.IsNullOrEmpty(filteredLabel))
                {
                    node.FilteredLabel = FilterLabel(node.Label);
                }
                else
                {
                    node.FilteredLabel = filteredLabel;
                }

                // flags
                node.Flags = nodeCsv.GetField<string?>("flags");

                // parent ID will be set in pass 2
                node.ParentId = null;

                nodes.Add(node);
            }

            // attach mappings to nodes, so that they are inserted with them
            int mappingsImported = mappingReader != null
                ? ReadMappings(mappingReader, config, trees, nodes)
                : 0;

            // insert all nodes and build key-to-ID mapping
            Dictionary<string, int> nodeKeyToIds = [];

            if (nodes.Count > 0)
            {
                IList<int> ids = await _store.AddNodesAsync(nodes);
                for (int i = 0; i < nodes.Count; i++)
                {
                    nodes[i].Id = ids[i];
                    nodeKeyToIds[$"{nodes[i].TreeId}#{nodes[i].Key}"] = ids[i];

                    // mappings were saved: do not rewrite them in pass 2
                    nodes[i].Mappings = null;
                }
                nodesImported = nodes.Count;
                _logger.LogInformation(
                    "Inserted {Count} node(s) with {MappingCount} mapping(s) " +
                    "(pass 1: no parent links)",
                    nodesImported, mappingsImported);
            }

            // PASS 2: Update parent IDs
            List<TaxoNode> nodesToUpdate = [];
            for (int i = 0; i < nodes.Count; i++)
            {
                string? parentKey = parentKeys[i];
                if (!string.IsNullOrEmpty(parentKey))
                {
                    string parentCompositeKey =
                        $"{nodes[i].TreeId}#{parentKey}";
                    if (!nodeKeyToIds.TryGetValue(parentCompositeKey,
                        out int parentId))
                    {
                        throw new Exception(
                            $"Parent key '{parentKey}' not found for node " +
                            $"'{nodes[i].Key}'");
                    }
                    nodes[i].ParentId = parentId;
                    nodesToUpdate.Add(nodes[i]);
                }
            }

            // update nodes with parent IDs in batches
            for (int i = 0; i < nodesToUpdate.Count; i += CacheSize)
            {
                int batchSize = Math.Min(CacheSize,
                    nodesToUpdate.Count - i);
                List<TaxoNode> batch = nodesToUpdate.GetRange(i, batchSize);
                await _store.AddNodesAsync(batch);
            }
            if (nodesToUpdate.Count > 0)
            {
                _logger.LogInformation(
                    "Updated {Count} node(s) with parent references (pass 2)",
                    nodesToUpdate.Count);
            }

            _logger.LogInformation(
                "TaxoStore CSV import completed: {TreeCount} tree(s), " +
                "{NodeCount} node(s), {MappingCount} mapping(s)",
                trees.Count, nodesImported, mappingsImported);

            return nodesImported;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error importing TaxoStore CSV data");
            throw new InvalidOperationException(
                "Error reading CSV header: " + ex.Message, ex);
        }
    }
}
