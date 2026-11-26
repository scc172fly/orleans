var builder = DistributedApplication.CreateBuilder(args);

// 添加您为 Orleans 组合组合和
// grain 国家储存。
var sessionStorage = builder.AddAzureStorage("sessionStorage")
    .RunAsEmulator();
var persistentStorage = builder.AddAzureStorage("persistentStorage")
    .RunAsEmulator(config => config.WithLifetime(ContainerLifetime.Persistent));

var clusteringTable = sessionStorage.AddTables("clustering");
var grainStorage = persistentStorage.AddBlobs("grain-state");

// 将 Orleans 资源添加到寄件者 DistributedApplication
// 构建器, 然后将其配置为 Azure 表存储用于集束
// 和 Azure 布束存储 grain 储存。
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
