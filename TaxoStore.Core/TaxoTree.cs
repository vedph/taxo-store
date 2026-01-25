namespace TaxoStore.Core;

/// <summary>
/// A tree in the store.
/// </summary>
public class TaxoTree
{
    /// <summary>
    /// The tree's unique identifier. This is the external identifier
    /// used by consumers to reference the tree.
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// The tree's human-friendly display name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// An optional note about the tree.
    /// </summary>
    public string? Note {  get; set; }

    /// <summary>
    /// Returns a string representing this object.
    /// </summary>
    /// <returns>String.</returns>
    public override string ToString()
    {
        return $"{Id}: {Name}";
    }
}
