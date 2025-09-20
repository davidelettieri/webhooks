var builder = DistributedApplication.CreateBuilder(args);

var receiver = builder
    .AddProject<Projects.MinimalApis_Receivers>("minimalapis-receivers");

builder
    .AddProject<Projects.WorkerService_Publisher>("workerservice-publisher")
    .WaitFor(receiver);

builder.Build().Run();
