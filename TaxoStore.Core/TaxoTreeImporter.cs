using CsvHelper;
using CsvHelper.Configuration;
using Fusi.Text.Unicode;
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
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="store"/>
    /// is null.</exception>
    public TaxoTreeImporter(ITaxoStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
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
    /// 'id', 'name' and 'note'.</param>
    /// <param name="nodeReader">A <see cref="TextReader"/> providing CSV data
    /// for nodes. The CSV must contain a header row and fields such as
    /// 'tree_n', 'parent_key', 'key', 'label', 'filtered_label', and 'flags'.
    /// </param>
    /// <returns>The total number of nodes successfully imported and added to
    /// the data store. Returns 0 if no data is available.</returns>
    /// <exception cref="InvalidOperationException">Thrown if an error occurs
    /// while reading the CSV header or processing the input data.</exception>
    public async Task<int> ImportAsync(TextReader treeReader,
        TextReader nodeReader)
    {
        CsvConfiguration config = new(CultureInfo.InvariantCulture)
        {
            Delimiter = Delimiter
        };

        int nodesImported = 0;
        try
        {
            // seed trees
            using CsvReader treeCsv = new(treeReader, config);
            List<TaxoTree> trees = [];

            // read header and exit if none
            if (!treeCsv.Read()) return 0;
            treeCsv.ReadHeader();

            // read trees from CSV having fields id, name, note
            while (treeCsv.Read())
            {
                TaxoTree tree = new()
                {
                    Id = treeCsv.GetField<string>("id")
                        ?? throw new Exception("Tree ID is required"),
                    Name = treeCsv.GetField<string>("name")
                        ?? throw new Exception("Tree name is required"),
                    Note = treeCsv.GetField<string?>("note")
                };
                await _store.AddTreeAsync(tree);
                trees.Add(tree);
            }

            // seed nodes using two-pass approach:
            // Pass 1: Insert all nodes without parent IDs
            // Pass 2: Update parent IDs based on parent_key references
            List<TaxoNode> nodes = [];

            // track parent_key for each node to resolve in second pass
            List<string?> parentKeys = [];

            using CsvReader nodeCsv = new(nodeReader, config);

            // read header and exit if none
            if (!nodeCsv.Read()) return 0;
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

            // insert all nodes and build key-to-ID mapping
            Dictionary<string, int> nodeKeyToIds = [];

            if (nodes.Count > 0)
            {
                IList<int> ids = await _store.AddNodesAsync(nodes);
                for (int i = 0; i < nodes.Count; i++)
                {
                    nodes[i].Id = ids[i];
                    nodeKeyToIds[$"{nodes[i].TreeId}#{nodes[i].Key}"] = ids[i];
                }
                nodesImported = nodes.Count;
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

            return nodesImported;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Error reading CSV header: " + ex.Message, ex);
        }
    }
}
