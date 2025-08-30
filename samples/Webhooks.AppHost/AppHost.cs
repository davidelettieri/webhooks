var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.MinimalApis_Receivers>("minimalapis-receivers");

builder.AddProject<Projects.WorkerService_Publisher>("workerservice-publisher");

builder.Build().Run();
