using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;

namespace Orleans.Docs.Snippets.Aspire.Silo;

// 此类包含以 aspire 配置 Orleans silo 的示例代码
public static class SiloProgram
{
    // <silo_basic_config>
    public static void BasicSiloConfiguration(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        // 添加服务默认值(OpenTelemetry、健康检查等)
        builder.AddServiceDefaults();

        // 添加指定对象 Redis 客户端的客户 Orleans
        builder.AddKeyedRedisClient("orleans-redis");

        // 配置 Orleans - 自动将所有配置都输入
        builder.UseOrleans();

        builder.Build().Run();
    }
    // </silo_basic_config>

    // <silo_explicit_connection>
    public static void ExplicitConnectionConfiguration(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        builder.AddServiceDefaults();
        builder.AddKeyedRedisClient("orleans-redis");

        builder.UseOrleans(siloBuilder =>
        {
            var redisConnectionString = builder.Configuration.GetConnectionString("orleans-redis");

            siloBuilder.UseRedisClustering(options =>
            {
                options.ConfigurationOptions =
                    ConfigurationOptions.Parse(redisConnectionString!);
            });

            siloBuilder.AddRedisGrainStorageAsDefault(options =>
            {
                options.ConfigurationOptions =
                    ConfigurationOptions.Parse(redisConnectionString!);
            });
        });

        builder.Build().Run();
    }
    // </silo_explicit_connection>

    // <silo_azure_config>
    public static void AzureStorageConfiguration(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        builder.AddServiceDefaults();
        builder.AddKeyedAzureTableServiceClient("orleans-tables");
        builder.UseOrleans();

        builder.Build().Run();
    }
    // </silo_azure_config>

    // <health_checks>
    public static void ConfigureHealthChecks(IHostApplicationBuilder builder)
    {
        builder.Services.AddHealthChecks()
            .AddCheck<GrainHealthCheck>("orleans-grains")
            .AddCheck<SiloHealthCheck>("orleans-silo");
    }
    // </health_checks>

    // <reminders_redis_silo>
    public static void RemindersRedisSilo(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        builder.AddServiceDefaults();
        builder.AddKeyedRedisClient("redis");
        builder.UseOrleans();

        builder.Build().Run();
    }
    // </reminders_redis_silo>

    // <reminders_azure_table_silo>
    public static void RemindersAzureTableSilo(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        builder.AddServiceDefaults();
        builder.AddKeyedAzureTableServiceClient("reminders");
        builder.UseOrleans();

        builder.Build().Run();
    }
    // </reminders_azure_table_silo>

    // <reminders_inmemory_silo>
    public static void RemindersInMemorySilo(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        builder.AddServiceDefaults();
        builder.UseOrleans();

        builder.Build().Run();
    }
    // </reminders_inmemory_silo>

    // <adonet_silo>
    public static void AdoNetSilo(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        builder.AddServiceDefaults();

        // 配置 Orleans 手动操作, 因为 Aspire 不能自动电线 ADO.NET
        // 供应商——提供者类型类型的推论产生 "SqlServerDatabase" 而不是
        // "AdoNet" 供应商名称 Orleans 预期。
        builder.UseOrleans(siloBuilder =>
        {
            var connectionString = builder.Configuration.GetConnectionString("orleans-db")!;

            siloBuilder.UseAdoNetClustering(options =>
            {
                options.Invariant = "Microsoft.Data.SqlClient";
                options.ConnectionString = connectionString;
            });

            siloBuilder.AddAdoNetGrainStorageAsDefault(options =>
            {
                options.Invariant = "Microsoft.Data.SqlClient";
                options.ConnectionString = connectionString;
            });

            siloBuilder.UseAdoNetReminderService(options =>
            {
                options.Invariant = "Microsoft.Data.SqlClient";
                options.ConnectionString = connectionString;
            });
        });

        builder.Build().Run();
    }
    // </adonet_silo>

    // <grain_directory_silo>
    public static void GrainDirectorySilo(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        builder.AddServiceDefaults();
        builder.AddKeyedRedisClient("orleans-redis");
        builder.UseOrleans();

        builder.Build().Run();
    }
    // </grain_directory_silo>
}

// 文献证明实例的斯塔布健康检查班
// 实际应用中,您将执行实际健康检查逻辑

internal class GrainHealthCheck : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
        => Task.FromResult(HealthCheckResult.Healthy());
}

internal class SiloHealthCheck : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
        => Task.FromResult(HealthCheckResult.Healthy());
}
