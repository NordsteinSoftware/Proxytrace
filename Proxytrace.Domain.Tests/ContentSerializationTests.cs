using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.Common.Serialization;
using Proxytrace.Domain.Message;

namespace Proxytrace.Domain.Tests;

[TestClass]
public sealed class ContentSerializationTests : DomainTest<Module>
{
    [TestMethod]
    public void Serialize_TextContent_RoundTrips()
    {
        IServiceProvider services = GetServices();
        var serializer = services.GetRequiredService<ISerializer>();
        var content = Content.FromText("hello");

        var restored = serializer.DeserializeRequired<Content>(serializer.Serialize(content));

        restored.Should().Be(content);
    }

    [TestMethod]
    public void Serialize_ImageContent_RoundTripsMediaType()
    {
        // Regression: the converter used to drop BinaryData.MediaType on write, so a stored image
        // content came back without a media type and failed Content validation on reload.
        IServiceProvider services = GetServices();
        var serializer = services.GetRequiredService<ISerializer>();
        var content = Content.FromImage(BinaryData.FromBytes([1, 2, 3], "image/png"));

        var restored = serializer.DeserializeRequired<Content>(serializer.Serialize(content));

        restored.Should().Be(content);
        restored.Kind.Should().Be(ContentKind.Image);
        restored.Data.Should().NotBeNull();
        restored.Data.MediaType.Should().Be("image/png");
    }

    [TestMethod]
    public void Equals_SameBytesDifferentMediaType_AreNotEqual()
    {
        var png = Content.FromImage(BinaryData.FromBytes([1, 2, 3], "image/png"));
        var jpeg = Content.FromImage(BinaryData.FromBytes([1, 2, 3], "image/jpeg"));

        png.Should().NotBe(jpeg);
    }
}
