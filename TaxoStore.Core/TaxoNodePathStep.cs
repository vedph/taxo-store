namespace TaxoStore.Core;

/// <summary>
/// Represents a step in the path from the root node to a target node.
/// </summary>
/// <param name="NodeId">The ID of the node at this step.</param>
/// <param name="PageNumber">The 1-based page number where this node appears
/// among its siblings (ordered by key) given the specified page size.</param>
public record TaxoNodePathStep(int NodeId, int PageNumber);
