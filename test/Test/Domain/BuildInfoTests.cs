using AwtrixSharpWeb.Domain;

namespace Test.Domain
{
    /// <summary>
    /// BuildInfo is the single source for the version/commit shown in the startup log and the startup notification.
    /// </summary>
    public class BuildInfoTests
    {
        [Fact]
        public void Describe_WithCommit_IsAwtrixSharpVersionAndHash()
        {
            Assert.Equal($"Awtrix-Sharp {BuildInfo.Version} abc1234", BuildInfo.Describe("abc1234"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("  ")]
        public void Describe_WithoutCommit_OmitsTheHash(string? commit)
        {
            Assert.Equal($"Awtrix-Sharp {BuildInfo.Version}", BuildInfo.Describe(commit));
        }

        [Fact]
        public void Version_IsMajorMinorBuild()
        {
            Assert.Matches(@"^\d+\.\d+\.\d+$", BuildInfo.Version);
        }

        [Fact]
        public void Description_UsesTheAssemblyCommit()
        {
            Assert.Equal(BuildInfo.Describe(BuildInfo.CommitShort), BuildInfo.Description);
        }
    }
}
