using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;

namespace Orleans.Docs.Snippets.Aspire.Silo;

// 此类包含结合 Aspire 配置 Orleans Silo 的示例代码。
public static class SiloProgram
{
    // <silo_basic_config>
    public static void BasicSiloConfiguration(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        // 添加 Aspire 服务默认配置（OpenTelemetry、健康检查等）。
        builder.AddServiceDefaults();

        // 为 Orleans 添加 Aspire Redis 客户端。
        builder.AddKeyedRedisClient("orleans-redis");

        // 配置 Orleans：Aspire 自动注入所有配置。
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

        // 手动配置 Orleans，因为 Aspire 无法自动配置 ADO.NET
        // 提供程序：类型推断得到 "SqlServerDatabase"，而不是
        // Orleans 预期的提供程序名称 "AdoNet"。
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

// 文档示例所用的健康检查桩类。
// 在真实应用中，应实现实际的健康检查逻辑。

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
