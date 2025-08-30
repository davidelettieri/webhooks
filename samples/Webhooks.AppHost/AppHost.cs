var builder = DistributedApplication.CreateBuilder(args);

#pragma warning disable ASPIRECOSMOSDB001
var cosmos = builder.AddAzureCosmosDB("cosmos-db").RunAsPreviewEmulator(emulator => { emulator.WithDataExplorer(); });
#pragma warning restore ASPIRECOSMOSDB001

var db = cosmos.AddCosmosDatabase("webhooks");
db.AddContainer("payloads", "/id");

var receiver = builder
    .AddProject<Projects.MinimalApis_Receivers>("minimalapis-receivers")
    .WithReference(cosmos)
    .WaitFor(cosmos);

builder
    .AddProject<Projects.WorkerService_Publisher>("workerservice-publisher")
    .WaitFor(receiver);

builder.Build().Run();