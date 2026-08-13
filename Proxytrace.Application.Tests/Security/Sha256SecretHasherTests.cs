using AwesomeAssertions;
using Nordstein.Core.Common.Security;
using Proxytrace.Infrastructure.Security.Internal;

namespace Proxytrace.Application.Tests.Security;

[TestClass]
public sealed class Sha256SecretHasherTests
{
    private static ISecretHasher Hasher => new Sha256SecretHasher();

    [TestMethod]
    public void Hash_OfSameInput_IsDeterministic64CharHex()
    {
        var first = Hasher.Hash("proxytrace-abc");
        var second = Hasher.Hash("proxytrace-abc");

        first.Should().Be(second);
        first.Should().HaveLength(64).And.MatchRegex("^[0-9A-F]+$");
    }

    [TestMethod]
    public void Hash_OfDifferentInputs_Differs()
        => Hasher.Hash("a").Should().NotBe(Hasher.Hash("b"));
}
