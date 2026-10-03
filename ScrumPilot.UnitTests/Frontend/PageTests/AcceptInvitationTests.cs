using System.Net;
using Bunit;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using ScrumPilot.Shared.Models;
using ScrumPilot.Web.Pages;
using ScrumPilot.Web.Services;

namespace ScrumPilot.UnitTests.Frontend.PageTests;

public sealed class AcceptInvitationTests : FrontendTestBase
{
    [Fact]
    public void Anonymous_user_is_sent_to_login_with_local_token_return_url()
    {
        var navigation = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        navigation.NavigateTo("/accept-invitation?token=secret-value");

        Render<AcceptInvitation>();

        var query = QueryHelpers.ParseQuery(new Uri(navigation.Uri).Query);
        Assert.Equal("/login", new Uri(navigation.Uri).AbsolutePath);
        Assert.Equal("/accept-invitation?token=secret-value", query["returnUrl"]);
        Assert.DoesNotContain("secret-value", HttpRequests);
    }

    [Fact]
    public void Successful_acceptance_posts_token_refreshes_state_and_removes_token()
    {
        Authorization.SetAuthorized("member");
        HttpResponseFactory = request => request.Method == HttpMethod.Post
            ? new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"organizationId":7}""")
            }
            : new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    [
                      {"organizationId":4,"name":"Other","role":1,"isDeleted":false},
                      {"organizationId":7,"name":"Forge","role":1,"isDeleted":false}
                    ]
                    """)
            };
        var navigation = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        navigation.NavigateTo("/accept-invitation?token=one-time");

        var cut = Render<AcceptInvitation>();
        cut.WaitForState(() => new Uri(navigation.Uri).AbsolutePath == "/organization-management");

        Assert.Contains(HttpRequestLog, x => x.Method == HttpMethod.Post && x.Url == "api/organization-invitations/accept");
        Assert.DoesNotContain("token=", navigation.Uri);
        Assert.Equal(7, Services.GetRequiredService<OrganizationStateService>().SelectedOrganizationId);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "expired")]
    [InlineData(HttpStatusCode.Conflict, "already")]
    public void Failed_acceptance_shows_actionable_message(HttpStatusCode status, string expected)
    {
        Authorization.SetAuthorized("member");
        HttpResponseFactory = _ => new HttpResponseMessage(status);
        var navigation = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        navigation.NavigateTo("/accept-invitation?token=bad");

        var cut = Render<AcceptInvitation>();
        cut.WaitForState(() => cut.Markup.Contains(expected, StringComparison.OrdinalIgnoreCase));
    }
}
