var builder = DistributedApplication.CreateBuilder(args);

// 添加用于 Orleans 集群成员资格管理及
// Grain 状态存储的资源。
var sessionStorage = builder.AddAzureStorage("sessionStorage")
    .RunAsEmulator();
var persistentStorage = builder.AddAzureStorage("persistentStorage")
    .RunAsEmulator(config => config.WithLifetime(ContainerLifetime.Persistent));

var clusteringTable = sessionStorage.AddTables("clustering");
var grainStorage = persistentStorage.AddBlobs("grain-state");

// 将 Orleans 资源添加到 Aspire DistributedApplication
// 构建器，然后配置 Azure Table Storage 用于集群成员资格管理，
// Azure Blob Storage 用于 Grain 状态存储。
var orleans = builder.AddOrleans("default")
                     .WithClustering(clusteringTable)
                     .WithGrainStorage("Default", grainStorage);

builder.AddProject<Projects.JournaledTodoList_WebApp>("webapp")
       .WithReference(orleans)
       .WithReplicas(3)
       .WithExternalHttpEndpoints()
       .WaitFor(clusteringTable)
       .WaitFor(grainStorage);

builder.Build().Run();
