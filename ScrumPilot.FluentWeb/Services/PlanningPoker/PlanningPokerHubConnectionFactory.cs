using Microsoft.AspNetCore.SignalR.Client;

namespace ScrumPilot.FluentWeb.Services.PlanningPoker;

public interface IPlanningPokerHubConnectionFactory
{
    HubConnection Create(string hubUrl, Func<Task<string?>> accessTokenProvider);
}

public sealed class PlanningPokerHubConnectionFactory : IPlanningPokerHubConnectionFactory
{
    public HubConnection Create(string hubUrl, Func<Task<string?>> accessTokenProvider)
    {
        return new HubConnectionBuilder()
            .WithUrl(hubUrl, options => options.AccessTokenProvider = accessTokenProvider)
            .WithAutomaticReconnect()
            .Build();
    }
}
