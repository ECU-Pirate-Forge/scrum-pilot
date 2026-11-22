

using Microsoft.Extensions.Configuration;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);
ScrumPilot.AppHost.InfrastructureGraph.Configure(builder);
builder.Build().Run();

public partial class Program;
