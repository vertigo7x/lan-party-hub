using Xunit;

namespace LANPartyHub.IntegrationTests;

/// <summary>
/// Collection of the tests that read the log of the product (AD-15).
///
/// <para>
/// Reading the log means replacing the standard output of the process while the test
/// runs, so the tests of this collection cannot run in parallel with any other test: the
/// output of a test that runs at the same time would land in the capture of another one.
/// </para>
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class LogCaptureCollection
{
    /// <summary>Name of the collection.</summary>
    public const string Name = "Captura del log";
}
