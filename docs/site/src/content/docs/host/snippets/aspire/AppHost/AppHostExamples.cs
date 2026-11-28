using Aspire.Hosting;
using Aspire.Hosting.Azure;

namespace Orleans.Docs.Snippets.Aspire;

// 此文件包含 Orleans 与 Aspire 集成的文档示例。
// 每个示例都包装在区域标记和方法中，以便编译。

public static class AppHostExamples
{
    // <basic_orleans_cluster>
    public static void BasicOrleansCluster(string[] args)
    {
        var builder = DistributedApplication.CreateBuilder(args);

        // 添加用于 Orleans 集群成员管理的 Redis。
        var redis = builder.AddRedis("orleans-redis");

        // 定义使用 Redis 集群成员管理的 Orleans 资源。
        var orleans = builder.AddOrleans("cluster")
            .WithClustering(redis);

        // 添加 Orleans Silo 项目。
        builder.AddProject<Projects.Silo>("silo")
            .WithReference(orleans)
            .WaitFor(redis)
            .WithReplicas(3);

        builder.Build().Run();
    }
    // </basic_orleans_cluster>

    // <orleans_with_storage_reminders>
    public static void OrleansWithStorageAndReminders(string[] args)
    {
        var builder = DistributedApplication.CreateBuilder(args);

        var redis = builder.AddRedis("orleans-redis");

        var orleans = builder.AddOrleans("cluster")
            .WithClustering(redis)
            .WithGrainStorage("Default", redis)
            .WithGrainStorage("PubSubStore", redis)
            .WithReminders(redis);

        builder.AddProject<Projects.Silo>("silo")
            .WithReference(orleans)
            .WaitFor(redis)
            .WithReplicas(3);

        builder.Build().Run();
    }
    // </orleans_with_storage_reminders>

    // <separate_silo_and_client>
    public static void SeparateSiloAndClient(string[] args)
    {
        var builder = DistributedApplication.CreateBuilder(args);

        var redis = builder.AddRedis("orleans-redis");

        var orleans = builder.AddOrleans("cluster")
            .WithClustering(redis)
            .WithGrainStorage("Default", redis);

        // 后端 Orleans Silo 集群。
        var silo = builder.AddProject<Projects.Silo>("backend")
            .WithReference(orleans)
            .WaitFor(redis)
            .WithReplicas(5);

        // 作为 Orleans 客户端的前端 Web 项目。
        builder.AddProject<Projects.Client>("frontend")
            .WithReference(orleans.AsClient())  // 仅客户端引用。
            .WaitFor(silo);

        builder.Build().Run();
    }
    // </separate_silo_and_client>

    // <azure_storage_aspire>
    public static void AzureStorageWithAspire(string[] args)
    {
        var builder = DistributedApplication.CreateBuilder(args);

        // 为 Orleans 添加 Azure Storage。
        var storage = builder.AddAzureStorage("orleans-storage")
            .RunAsEmulator();  // 在本地开发中使用 Azurite 模拟器。

        var tables = storage.AddTables("orleans-tables");

        var orleans = builder.AddOrleans("cluster")
            .WithClustering(tables)
            .WithGrainStorage("Default", tables)
            .WithReminders(tables);

        builder.AddProject<Projects.Silo>("silo")
            .WithReference(orleans)
            .WaitFor(storage)
            .WithReplicas(3);

        builder.Build().Run();
    }
    // </azure_storage_aspire>

    // <local_development>
    public static void LocalDevelopment(string[] args)
    {
        var builder = DistributedApplication.CreateBuilder(args);

        var redis = builder.AddRedis("orleans-redis");
        // 开发期间自动运行 Redis 容器。

        builder.AddOrleans("cluster")
            .WithClustering(redis);

        // ...
    }
    // </local_development>

    // <production_config>
    public static void ProductionConfig(string[] args)
    {
        var builder = DistributedApplication.CreateBuilder(args);

        // 使用现有的 Azure Cache for Redis。
        var redis = builder.AddConnectionString("orleans-redis");

        builder.AddOrleans("cluster")
            .WithClustering(redis);

        // ...
    }
    // </production_config>

    // <reminders_redis_apphost>
    public static void RemindersRedisAppHost(string[] args)
    {
        var builder = DistributedApplication.CreateBuilder(args);

        var redis = builder.AddRedis("redis");

        var orleans = builder.AddOrleans("cluster")
            .WithClustering(redis)
            .WithReminders(redis);

        builder.AddProject<Projects.Silo>("silo")
            .WithReference(orleans)
            .WaitFor(redis);

        builder.Build().Run();
    }
    // </reminders_redis_apphost>

    // <reminders_azure_table_apphost>
    public static void RemindersAzureTableAppHost(string[] args)
    {
        var builder = DistributedApplication.CreateBuilder(args);

        var storage = builder.AddAzureStorage("storage")
            .RunAsEmulator();

        var reminders = storage.AddTables("reminders");

        var orleans = builder.AddOrleans("cluster")
            .WithClustering(reminders)
            .WithReminders(reminders);

        builder.AddProject<Projects.Silo>("silo")
            .WithReference(orleans)
            .WaitFor(storage);

        builder.Build().Run();
    }
    // </reminders_azure_table_apphost>

    // <reminders_inmemory_apphost>
    public static void RemindersInMemoryAppHost(string[] args)
    {
        var builder = DistributedApplication.CreateBuilder(args);

        var redis = builder.AddRedis("redis");

        var orleans = builder.AddOrleans("cluster")
            .WithClustering(redis)
            .WithMemoryReminders();

        builder.AddProject<Projects.Silo>("silo")
            .WithReference(orleans)
            .WaitFor(redis);

        builder.Build().Run();
    }
    // </reminders_inmemory_apphost>

    // <adonet_apphost>
    public static void AdoNetAppHost(string[] args)
    {
        var builder = DistributedApplication.CreateBuilder(args);

        // 添加 SQL Server 实例及数据库。
        // 注意：Aspire 根据资源类名推断 Orleans 提供程序类型
        // （SqlServerDatabaseResource → "SqlServerDatabase"），它与
        // Orleans 提供程序名称 "AdoNet" 不匹配。
        //
        // 当前版本的 Aspire.Hosting.Orleans 没有用于覆盖此推断的公开 API。
        // 作为变通方法，可手动在 Silo 中配置 Orleans 提供程序，
        // 使用 UseOrleans(siloBuilder => {...}) 并从
        // IConfiguration 读取连接字符串。
        var sql = builder.AddSqlServer("sql");
        var db = sql.AddDatabase("orleans-db");

        // 传入数据库资源，使 Aspire 注入 ConnectionStrings__orleans-db。
        // 然后在 Silo 中手动配置 Orleans（参见 Silo 示例）。
        builder.AddProject<Projects.Silo>("silo")
            .WithReference(db)
            .WaitFor(sql);

        builder.Build().Run();
    }
    // </adonet_apphost>

    // <grain_directory_apphost>
    public static void GrainDirectoryAppHost(string[] args)
    {
        var builder = DistributedApplication.CreateBuilder(args);

        var redis = builder.AddRedis("orleans-redis");

        var orleans = builder.AddOrleans("cluster")
            .WithClustering(redis)
            .WithGrainDirectory("MyDirectory", redis);

        builder.AddProject<Projects.Silo>("silo")
            .WithReference(orleans)
            .WaitFor(redis);

        builder.Build().Run();
    }
    // </grain_directory_apphost>

    // <explicit_cluster_ids>
    public static void ExplicitClusterIds(string[] args)
    {
        var builder = DistributedApplication.CreateBuilder(args);

        var redis = builder.AddRedis("orleans-redis");

        var orleans = builder.AddOrleans("cluster")
            // 设置稳定的 ID，以支持滚动部署及跨重启兼容性。
            // 如果省略，每次运行都会生成随机 ID。开发时没有问题，
            // 但在生产环境中会造成问题，因为不同运行中的 Silo
            // 将无法识别彼此。
            .WithClusterId("my-cluster")
            .WithServiceId("my-service")
            .WithClustering(redis);

        builder.AddProject<Projects.Silo>("silo")
            .WithReference(orleans)
            .WaitFor(redis)
            .WithReplicas(3);

        builder.Build().Run();
    }
    // </explicit_cluster_ids>
}
