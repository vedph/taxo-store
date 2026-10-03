using System;

namespace TaxoStore.Core;

/// <summary>
/// Exception thrown by <see cref="ITaxoStore"/> implementations when a write
/// operation conflicts with existing data, e.g. when adding a node whose key
/// is already used by another node in the same tree.
/// </summary>
public class TaxoStoreConflictException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TaxoStoreConflictException"/>
    /// class.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The optional inner exception.</param>
    public TaxoStoreConflictException(string message,
        Exception? innerException = null) : base(message, innerException)
    {
    }
}
