using CrawlOnline.Protocol;
using Xunit;

namespace CrawlOnline.Protocol.Tests;

public sealed class GameBuildFingerprintTests
{
    private const string WindowsSha256 = "e93e8fb49fd3c3ebe622d0f9f9557c1e4dd475c2a277be19e2c05cbb1f05f61e";

    [Fact]
    public void RoundTripsCanonicalSha256()
    {
        GameBuildFingerprint fingerprint = GameBuildFingerprint.Parse(WindowsSha256.ToUpperInvariant());

        Assert.False(fingerprint.IsEmpty);
        Assert.Equal(WindowsSha256, fingerprint.ToString());
        Assert.True(GameBuildFingerprint.TryParse(fingerprint.ToString(), out GameBuildFingerprint decoded));
        Assert.Equal(fingerprint, decoded);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("e93e")]
    [InlineData("z93e8fb49fd3c3ebe622d0f9f9557c1e4dd475c2a277be19e2c05cbb1f05f61e")]
    [InlineData("0000000000000000000000000000000000000000000000000000000000000000")]
    public void RejectsMissingMalformedOrEmptyFingerprint(string value)
    {
        Assert.False(GameBuildFingerprint.TryParse(value!, out GameBuildFingerprint fingerprint));
        Assert.True(fingerprint.IsEmpty);
    }

    [Fact]
    public void LobbyMetadataRequiresExactParsedFingerprint()
    {
        GameBuildFingerprint expected = GameBuildFingerprint.Parse(WindowsSha256);

        Assert.True(GameBuildFingerprint.MatchesMetadata(expected, WindowsSha256.ToUpperInvariant()));
        Assert.False(GameBuildFingerprint.MatchesMetadata(expected,
            "d6f169535cf2123568359550d75fe1a9924948e04d8d0beb2eed7eb187542f84"));
        Assert.False(GameBuildFingerprint.MatchesMetadata(expected, string.Empty));
        Assert.False(GameBuildFingerprint.MatchesMetadata(expected, "forged"));
        Assert.False(GameBuildFingerprint.MatchesMetadata(new GameBuildFingerprint(), WindowsSha256));
    }
}
