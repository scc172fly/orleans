using Aspire.Hosting;
using Aspire.Hosting.Azure;

namespace Orleans.Docs.Snippets.Aspire;

// 此文件包含 Orleans 请求整合文档示例。
// 每个实例都以区域标记和汇编方法包扎。

public static class AppHostExamples
{
    // <basic_orleans_cluster>
    public static void BasicOrleansCluster(string[] args)
    {
        var builder = DistributedApplication.CreateBuilder(args);

        // 添加添加添加 Redis 用于 Orleans 组合
        var redis = builder.AddRedis("orleans-redis");

        // 定义 Orleans 资源,并使用 Redis 组合
        var orleans = builder.AddOrleans("cluster")
            .WithClustering(redis);

        // 添加 Orleans silo 项目
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

        // 后端 Orleans silo 群集
        var silo = builder.AddProject<Projects.Silo>("backend")
            .WithReference(orleans)
            .WaitFor(redis)
            .WithReplicas(5);

        // 前端网络项目 Orleans 客户端
        builder.AddProject<Projects.Client>("frontend")
            .WithReference(orleans.AsClient())  // 仅客户端引用
            .WaitFor(silo);

        builder.Build().Run();
    }
    // </separate_silo_and_client>

    // <azure_storage_aspire>
    public static void AzureStorageWithAspire(string[] args)
    {
        var builder = DistributedApplication.CreateBuilder(args);

        // 添加 Azure 储存 Orleans
        var storage = builder.AddAzureStorage("orleans-storage")
            .RunAsEmulator();  // 使用 Azurite 模拟器促进本地发展

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
        // Redis 集装箱在开发期间自动运行

        builder.AddOrleans("cluster")
            .WithClustering(redis);

        // ...
    }
    // </local_development>

    // <production_config>
    public static void ProductionConfig(string[] args)
    {
        var builder = DistributedApplication.CreateBuilder(args);

        // 使用现有 Azure 缓存缓存的缓存 Redis
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

        // 添加 SQL 服务器实例和数据库。
        // 注: 远征从资源类别名称中引用 Orleans 提供者类型
        // (SqlServerDatabaseResource → "SqlServerDatabase“)),但与
        // Orleans 提供商名称 "AdoNet" 。
        //
        // 当前版本中没有公开的 API 推翻此推论
        // Aspire.Hosting.Orleans。作为一种变通办法,配置 Orleans 提供者
        // 中手动 silo 使用 UseOrleans(siloBuilder 并读取
        // Iconfigation 的连接字符串。
        var sql = builder.AddSqlServer("sql");
        var db = sql.AddDatabase("orleans-db");

        // 传递数据库资源, 从而快速输入 ConnectionStrings__orleans- db。
        // 然后配置 Orleans 中手动 silo (见 silo 实例)
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
            // 设定滚动部署和交叉重新启动兼容性的稳定标识。
            // 如果省略,则每次运行生成随机身份识别码—— 用于开发的罚款;
            // 生产有问题,因为 silos 来自不同的运行
            // 彼此不承认对方。
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
