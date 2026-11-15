using System.Net;
using System.Net.Http.Json;
using Bunit;
using ScrumPilot.Shared.Models;
using ScrumPilot.Web.Components.Organizations;

namespace ScrumPilot.UnitTests.Frontend.ComponentTests;

public sealed class OrganizationDetailsLifetimeTests : FrontendTestBase
{
    [Fact]
    public async Task DisposedOrganizationEditorCannotPublishALateRename()
    {
        var completion = new TaskCompletionSource<HttpResponseMessage>();
        HttpResponseFactoryAsync = (_, _) => completion.Task;
        var callbacks = 0;
        using var cut = Render<OrganizationDetails>(p => p
            .Add(x => x.Organization, new OrganizationSummaryDto(1, "First", OrganizationRole.Owner, false))
            .Add(x => x.OrganizationChanged, _ => callbacks++));
        var pending = cut.Find("[data-action='rename']").ClickAsync(new());
        await DisposeComponentsAsync();
        completion.SetResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new OrganizationSummaryDto(1, "Renamed", OrganizationRole.Owner, false))
        });
        await pending;
        Assert.Equal(0, callbacks);
    }
}
