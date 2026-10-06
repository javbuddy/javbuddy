using Bunit;
using Javbuddy.Components.Shared;

namespace Javbuddy.Tests.Components.Shared;

public class InertBackgroundTests : BunitContext
{
    private BunitJSModuleInterop SetUpModule()
    {
        var module = JSInterop.SetupModule("./Components/Shared/InertBackground.razor.js");
        module.Mode = JSRuntimeMode.Loose;
        return module;
    }

    [Fact]
    public void InitialisesTheModuleWithTheRoot_AndRendersNothing()
    {
        var module = SetUpModule();

        var cut = Render<InertBackground>(p => p
            .Add(x => x.Root, new Microsoft.AspNetCore.Components.ElementReference("backdrop-id")));

        var init = Assert.Single(module.Invocations, i => i.Identifier == "init");
        Assert.Equal("backdrop-id", Assert.IsType<Microsoft.AspNetCore.Components.ElementReference>(init.Arguments[1]).Id);
        Assert.Equal("", cut.Markup.Trim());
    }

    [Fact]
    public async Task Dispose_RestoresThePageUnderTheSameKey()
    {
        var module = SetUpModule();
        var cut = Render<InertBackground>(p => p
            .Add(x => x.Root, new Microsoft.AspNetCore.Components.ElementReference("backdrop-id")));

        await cut.Instance.DisposeAsync();

        var key = Assert.Single(module.Invocations, i => i.Identifier == "init").Arguments[0];
        Assert.Equal(key, Assert.Single(module.Invocations, i => i.Identifier == "dispose").Arguments[0]);
    }
}
