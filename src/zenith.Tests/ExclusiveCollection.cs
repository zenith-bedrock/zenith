using Xunit;

namespace Zenith.Tests;

/// <summary>
/// Tests that need the process to themselves join this collection: they change process-wide state (environment variables such as
/// ZENITH_DATA, or construct a <c>ZenithServer</c>, which reads them and shares the default data directory) or they assert on wall-clock
/// timing (tick pacing). xUnit runs it alone, never alongside another collection, so everything else in the suite runs in parallel
/// without seeing a half-changed environment or stealing CPU from a timing measurement.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ExclusiveCollection
{
    public const string Name = "Exclusive";
}
