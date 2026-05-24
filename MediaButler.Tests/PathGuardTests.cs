using MediaButler.Pipeline;
using NUnit.Framework;

namespace MediaButler.Tests;

/// <summary>
/// The path-overlap guard is the single most important safety net: pointing
/// the source at <c>M:\TV</c> would cause every show folder to be re-parsed
/// as a multi-season parent and hoisted into oblivion.
/// </summary>
[TestFixture]
public class PathGuardTests
{
    [TestCase(@"C:\Media\Torrents", @"C:\Media\TV",       false)]
    [TestCase(@"C:\Media\TV",       @"C:\Media\TV",       true)]   // identical
    [TestCase(@"C:\Media\TV\",      @"C:\Media\TV",       true)]   // trailing slash
    [TestCase(@"C:\Media\TV",       @"C:\Media",          true)]   // source inside other
    [TestCase(@"C:\Media",          @"C:\Media\TV",       true)]   // other inside source
    [TestCase(@"C:\Media\TV2",      @"C:\Media\TV",       false)]  // sibling prefix-match must not trigger
    [TestCase(@"",                  @"C:\Media\TV",       false)]
    [TestCase(@"C:\Media\TV",       @"",                  false)]
    public void PathOverlaps_detects_identical_and_nested_paths(string source, string other, bool expected) =>
        Assert.That(PathGuard.PathOverlaps(source, other), Is.EqualTo(expected));
}
