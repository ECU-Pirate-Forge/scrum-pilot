using Bunit;
using System.Net;
using System.Net.Http.Json;
using ScrumPilot.FluentWeb.Components.Organizations;
using ScrumPilot.Shared.Models;
using Xunit;

namespace ScrumPilot.FluentWeb.Tests.Management;

public sealed class ConfirmationLifetimeTests : FluentFrontendTestBase
{
    [Fact]
    public async Task Disposed_organization_editor_cannot_publish_a_late_rename()
    {
        var completion = new TaskCompletionSource<HttpResponseMessage>();
        Http.OnAsync(HttpMethod.Put, "api/organizations/1", (_, _) => completion.Task);
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

    [Fact]
    public async Task Old_delete_confirmation_cannot_delete_a_replacement_organization()
    {
        var confirmation = new TaskCompletionSource<bool>();
        using var cut = Render<OrganizationDangerZone>(p => p
            .Add(x => x.Organization, new OrganizationSummaryDto(1, "First", OrganizationRole.Owner, false))
            .Add(x => x.ConfirmAsync, (_, _, _, _, _) => confirmation.Task));
        var pending = cut.Find("[data-action='delete']").ClickAsync(new());
        cut.Render(p => p.Add(x => x.Organization, new OrganizationSummaryDto(2, "Second", OrganizationRole.Owner, false)));
        confirmation.SetResult(true);
        await pending;
        Assert.DoesNotContain(Http.Requests, request => request.Method == HttpMethod.Delete);
    }

}
