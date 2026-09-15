using Xunit;

namespace Sati.Api.Tests;

/// <summary>
/// Shares the primary API fixture across the main integration-test collection.
///
/// Each SatiApiFactory has its own named shared-memory SQLite database, so
/// explicitly separate collections can retain private synthetic data without
/// changing this collection's count-sensitive seed fixtures. Contexts within a
/// factory share the same database; different factory instances do not.
///
/// A collection fixture also means the seed runs once rather than once per class.
/// Tests that create their own rows are responsible for removing them.
/// </summary>
[CollectionDefinition(Name)]
public sealed class SatiApiCollection : ICollectionFixture<SatiApiFactory>
{
    public const string Name = "Sati API integration";
}
