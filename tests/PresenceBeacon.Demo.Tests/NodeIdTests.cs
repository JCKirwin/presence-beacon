namespace PresenceBeacon.Demo.Tests;

using PresenceBeacon.Demo.Presence;

using Xunit;

/// <summary>
/// Invariant support: one beacon file path per node id.
/// </summary>
/// <remarks>
/// The id becomes the file name, so the one-file-per-node rule only holds if an id cannot
/// contain a separator, a dot, or anything else that would let it escape its directory or
/// impersonate a scratch file.
/// </remarks>
public sealed class NodeIdTests
{
    [Theory]
    [InlineData("ridge-01")]
    [InlineData("meadow_2")]
    [InlineData("A")]
    [InlineData("0123456789")]
    public void AcceptsPlainIdentifiers(string value)
    {
        var id = NodeId.Parse(value);

        Assert.Equal(value, id.Value);
        Assert.Equal(value, id.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("ridge 01")]
    [InlineData("ridge.01")]
    [InlineData("ridge/01")]
    [InlineData("ridge\\01")]
    [InlineData("..")]
    [InlineData("../../secrets")]
    [InlineData("ridge:01")]
    [InlineData("ridge*")]
    [InlineData("ridge-01.beacon.json")]
    public void RejectsAnythingThatCouldEscapeOrCollide(string value)
    {
        Assert.False(NodeId.TryParse(value, out _));
        Assert.Throws<ArgumentException>(() => NodeId.Parse(value));
    }

    [Fact]
    public void RejectsNull()
    {
        Assert.False(NodeId.TryParse(null, out _));
    }

    [Fact]
    public void RejectsIdsLongerThanTheLimit()
    {
        var atLimit = new string('a', 64);
        var overLimit = new string('a', 65);

        Assert.True(NodeId.TryParse(atLimit, out _));
        Assert.False(NodeId.TryParse(overLimit, out _));
    }

    [Fact]
    public void FailedTryParseYieldsTheDefaultId()
    {
        Assert.False(NodeId.TryParse("ridge/01", out var id));
        Assert.Equal(string.Empty, id.Value);
    }

    [Fact]
    public void DefaultIdHasEmptyValueRatherThanNull()
    {
        // The struct can always be default-constructed, so Value must stay safe to read.
        // The store and writer reject that state explicitly instead of dereferencing null.
        NodeId id = default;

        Assert.Equal(string.Empty, id.Value);
    }

    [Fact]
    public void SameTextIsTheSameNode()
    {
        Assert.Equal(NodeId.Parse("ridge-01"), NodeId.Parse("ridge-01"));
    }

    [Fact]
    public void DifferentTextIsADifferentNode()
    {
        Assert.NotEqual(NodeId.Parse("ridge-01"), NodeId.Parse("ridge-02"));
    }

    [Fact]
    public void IdsAreCaseSensitive()
    {
        // File systems disagree about case, so the id itself must not. Treating these as
        // one node on Windows and two on Linux would break the one-file-per-node rule.
        Assert.NotEqual(NodeId.Parse("ridge-01"), NodeId.Parse("RIDGE-01"));
    }
}
