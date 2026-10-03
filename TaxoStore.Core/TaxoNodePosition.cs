namespace TaxoStore.Core;

/// <summary>
/// The position of a node in its tree.
/// </summary>
/// <param name="NodeId">The ID of the node.</param>
/// <param name="Y">The depth level (1-based): root nodes have Y=1.</param>
/// <param name="X">The sibling position (1-based), with siblings ordered
/// by key.</param>
/// <param name="HasChildren">True if the node has any children.</param>
public record TaxoNodePosition(int NodeId, int Y, int X, bool HasChildren);
