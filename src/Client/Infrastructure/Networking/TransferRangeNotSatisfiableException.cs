namespace LANPartyHub.Client.Infrastructure.Networking;

/// <summary>
/// The server answered that the requested range cannot be satisfied (<c>416</c>).
///
/// <para>
/// It is the failure of the transfer and not of the whole client: the caller decides what
/// it means for the operation that asked for the range. The story 5.2 turns it into the
/// terminal state of the operation.
/// </para>
/// </summary>
public sealed class TransferRangeNotSatisfiableException : InvalidOperationException
{
    /// <summary>Creates the failure with the range that the server rejected.</summary>
    /// <param name="startInclusive">First byte of the rejected range, included.</param>
    /// <param name="endInclusive">Last byte of the rejected range, included.</param>
    public TransferRangeNotSatisfiableException(long startInclusive, long endInclusive)
        : base($"El servidor no puede satisfacer el rango {startInclusive}-{endInclusive}.")
    {
        StartInclusive = startInclusive;
        EndInclusive = endInclusive;
    }

    /// <summary>First byte of the rejected range, included.</summary>
    public long StartInclusive { get; }

    /// <summary>Last byte of the rejected range, included.</summary>
    public long EndInclusive { get; }
}
