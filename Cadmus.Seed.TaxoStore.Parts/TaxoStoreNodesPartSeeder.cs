using Bogus;
using Cadmus.Core;
using Cadmus.TaxoStore.Parts;
using Fusi.Tools;
using Fusi.Tools.Configuration;
using System;
using System.Collections.Generic;

namespace Cadmus.Seed.TaxoStore.Parts;


/// <summary>
/// Seeder for <see cref="TaxoStoreNodesPart"/>.
/// Tag: <c>seed.it.vedph.taxo-store-nodes</c>.
/// </summary>
/// <seealso cref="PartSeederBase" />
[Tag("seed.it.vedph.taxo-store-nodes")]
public sealed class TaxoStoreNodesPartSeeder : PartSeederBase,
    IConfigurable<TaxoStoreNodesPartSeederOptions>
{
    private TaxoStoreNodesPartSeederOptions? _options;

    /// <summary>
    /// Configures the object with the specified options.
    /// </summary>
    /// <param name="options">The options.</param>
    public void Configure(TaxoStoreNodesPartSeederOptions options)
    {
        _options = options;
    }

    /// <summary>
    /// Creates and seeds a new part.
    /// </summary>
    /// <param name="item">The item this part should belong to.</param>
    /// <param name="roleId">The optional part role ID.</param>
    /// <param name="factory">The part seeder factory. This is used
    /// for layer parts, which need to seed a set of fragments.</param>
    /// <returns>A new part or null.</returns>
    /// <exception cref="ArgumentNullException">item or factory</exception>
    public override IPart? GetPart(IItem item, string? roleId,
        PartSeederFactory? factory)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (_options == null) return null;

        TaxoStoreNodesPart part = new()
        {
            // pick a random tree from options
            TreeId = _options.TreeIds[
                Randomizer.Seed.Next(0, _options.TreeIds.Count)]
        };

        // get the nodes belongong to the tree
        List<StringPair> nodesInTree = _options.Nodes.FindAll(n =>
            n.Value?.StartsWith(part.TreeId + '/') == true);

        part.NodeIds = [];

        // pick random nodes from those available
        if (nodesInTree.Count > 0)
        {
            int maxNodes = Math.Min(5, nodesInTree.Count);
            int nodeCount = Randomizer.Seed.Next(1, maxNodes + 1);

            // create a shuffled copy to avoid duplicates
            List<StringPair> shuffled = [.. nodesInTree];
            for (int i = shuffled.Count - 1; i > 0; i--)
            {
                int j = Randomizer.Seed.Next(0, i + 1);
                (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
            }

            // take the first nodeCount items
            for (int n = 0; n < nodeCount; n++)
                part.NodeIds.Add(shuffled[n]);
        }

        SetPartMetadata(part, roleId, item);

        return part;
    }
}

/// <summary>
/// Options for <see cref="TaxoStoreNodesPartSeeder"/>.
/// </summary>
public class TaxoStoreNodesPartSeederOptions
{
    /// <summary>
    /// The tree IDs to choose from.
    /// </summary>
    public List<string> TreeIds { get; set; } = [];

    /// <summary>
    /// The node keys and labels to choose from. Each value is prefixed by
    /// its tree ID followed by a slash ('/').
    /// </summary>
    public List<StringPair> Nodes { get; set; } = [];
}