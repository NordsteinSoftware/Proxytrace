using System.Resources;
using Autofac;
using Microsoft.Testing.Platform.Services;
using Proxytrace.Domain.Prompt;
using Nordstein.Core.Testing;

namespace Proxytrace.Domain.Tests;

[TestClass]
public class ResourcesPromptRepositoryTests : BaseTest<Module>
{
    [TestMethod]
    public async Task GetAsync_TemplateExists_ReturnsTemplate()
    {
        // Arrange
        var services = GetServices();
        var repository = services.GetRequiredService<IPromptTemplateRepository>();
        var templateName = "Test";

        // Act
        var template = await repository.FindAsync(templateName);

        // Assert
        Assert.IsNotNull(template);
        Assert.AreEqual(templateName, template.Name);
        Assert.AreEqual("foobar", template.Template);
    }
    
    [TestMethod]
    public async Task GetAsync_TemplateDoesNotExist_ReturnsNull()
    {
        // Arrange
        var services = GetServices(ct =>
        {
            ct.RegisterInstance(Prompts.ResourceManager).As<ResourceManager>();
        });
        var repository = services.GetRequiredService<IPromptTemplateRepository>();
        var templateName = "DoesNotExist";

        // Act
        var template = await repository.FindAsync(templateName);

        // Assert
        Assert.IsNull(template);
    }
}