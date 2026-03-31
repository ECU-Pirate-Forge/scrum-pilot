using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using NSubstitute;

namespace ScrumPilot.UnitTests.Frontend
{
    public abstract class FrontendTestBase : BunitContext
    {
        protected readonly HttpClient MockHttpClient;

        protected FrontendTestBase()
        {
            Services.AddMudServices();
            MockHttpClient = Substitute.For<HttpClient>();
            Services.AddSingleton(MockHttpClient);

            // Use loose mode to ignore JSInterop issues - focus on component logic instead
            JSInterop.Mode = JSRuntimeMode.Loose;
        }
    }
}

