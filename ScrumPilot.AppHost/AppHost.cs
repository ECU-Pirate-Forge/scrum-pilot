

var builder = DistributedApplication.CreateBuilder(args);



var webapi = builder.AddProject<Projects.ScrumPilot_API>("webapi");


var web = builder.AddProject<Projects.ScrumPilot_Web>("web")
    .WithExternalHttpEndpoints()
    .WithReference(webapi)
    .WaitFor(webapi);



builder.Build().Run();
