using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using ScrumPilot.API.Configuration;

namespace ScrumPilot.UnitTests.Backend.Configuration;

public sealed class BlazorCorsPolicyTests
{
    [Fact]
    public void Configure_AllowsTheProductionFrontendOrigin()
    {
        var options = new CorsOptions();
        BlazorCorsPolicy.Configure(options);
        var policy = options.GetPolicy(BlazorCorsPolicy.Name);
        var context = new DefaultHttpContext();
        context.Request.Headers.Origin = "https://scrumpilot-web.onrender.com";
        var corsService = new CorsService(Options.Create(options), NullLoggerFactory.Instance);

        var result = corsService.EvaluatePolicy(context, policy!);

        Assert.True(result.IsOriginAllowed);
        Assert.Equal("https://scrumpilot-web.onrender.com", result.AllowedOrigin);
        Assert.True(result.SupportsCredentials);
    }

    [Fact]
    public void Configure_RejectsUnlistedOrigins()
    {
        var options = new CorsOptions();
        BlazorCorsPolicy.Configure(options);
        var policy = options.GetPolicy(BlazorCorsPolicy.Name);
        var context = new DefaultHttpContext();
        context.Request.Headers.Origin = "https://untrusted.example";
        var corsService = new CorsService(Options.Create(options), NullLoggerFactory.Instance);

        var result = corsService.EvaluatePolicy(context, policy!);

        Assert.False(result.IsOriginAllowed);
    }
}
