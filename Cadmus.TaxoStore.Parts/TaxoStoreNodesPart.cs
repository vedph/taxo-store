using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Cadmus.Core;
using Fusi.Tools;
using Fusi.Tools.Configuration;

namespace Cadmus.TaxoStore.Parts;

/// <summary>
/// Taxonomy store nodes part. This contains the ID of a taxonomy tree (as a
/// string) and any number of nodes IDs and labels from that tree. Labels are
/// redundant, and might become stale, but they are stored here for easier reading
/// and third-party integration.
/// <para>Tag: <c>it.vedph.taxo-store-nodes</c>.</para>
/// </summary>
[Tag("it.vedph.taxo-store-nodes")]
public sealed class TaxoStoreNodesPart : PartBase
{
    /// <summary>
    /// The identifier of the taxonomy tree used by this part. All the nodes
    /// belong to this tree.
    /// </summary>
    public string TreeId { get; set; } = "";

    /// <summary>
    /// Gets or sets the collection of node identifiers included in this part.
    /// Each node identifier has a name, corresponding to the node label, and
    /// a value, corresponding to the node key.
    /// </summary>
    public List<StringPair> NodeIds { get; set; } = [];

    /// <summary>
    /// Get all the key=value pairs (pins) exposed by the implementor.
    /// </summary>
    /// <param name="item">The optional item. The item with its parts
    /// can optionally be passed to this method for those parts requiring
    /// to access further data.</param>
    /// <returns>The pins.</returns>
    public override IEnumerable<DataPin> GetDataPins(IItem? item = null)
    {
        DataPinBuilder builder = new();
        builder.AddValues("node-key", NodeIds.Select(p => $"{TreeId}/{p.Value}"));
        return builder.Build(this);
    }

    /// <summary>
    /// Gets the definitions of data pins used by the implementor.
    /// </summary>
    /// <returns>Data pins definitions.</returns>
    public override IList<DataPinDefinition> GetDataPinDefinitions()
    {
        return new List<DataPinDefinition>(
        [
             new DataPinDefinition(DataPinValueType.String,
                "node-key",
                "Tree ID + '/' + node's key.",
                "M")
        ]);
    }

    /// <summary>
    /// Converts to string.
    /// </summary>
    /// <returns>
    /// A <see cref="string" /> that represents this instance.
    /// </returns>
    public override string ToString()
    {
        StringBuilder sb = new();

        sb.Append("[TaxoStoreNodesPart] ").Append(TreeId);
        sb.AppendJoin(", ",
            NodeIds.Select(p => $"{p.Name}={p.Value}"));

        return sb.ToString();
    }
}
