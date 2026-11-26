using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

// 常见添加 .NET 服务:服务发现、复原力、健康检查和 OpenTelemetry.
// 解决时,每个服务项目都应参考该项目。
// 要了解更多关于使用此项目的知识,请见 https://aka.ms/dotnet/aspire/service-defaults
public static class Extensions
{
    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();

        builder.AddDefaultHealthChecks();

        builder.Services.AddServiceDiscovery();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            // 默认打开抗力
            http.AddStandardResilienceHandler();

            // 默认打开服务发现
            http.AddServiceDiscovery();
        });

        // 不同意以下限制服务发现许可计划的意见。
        // builder.Services.Configure<ServiceDiscoveryOptions>(选项 ____________________________________________________________________________________
        // {
        //     options.AllowedSchemes = ["https"];
        // });

        return builder;
    }

    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                .AddMeter("Microsoft.Orleans");
            })
            .WithTracing(tracing =>
            {
                tracing.AddSource("Microsoft.Orleans.Runtime");
                tracing.AddSource("Microsoft.Orleans.Application");

                tracing.AddSource(builder.Environment.ApplicationName)
                        .AddAspNetCoreInstrumentation()
                        // 取消下列行的修改,以启用 gRPC 仪表(需要 OpenTelemetry.Instrumentation.GrpcNetClient 包)
                        //.AddGrpcClientInstrumentation()
                        .AddHttpClientInstrumentation();
            });

        builder.AddOpenTelemetryExporters();

        return builder;
    }

    private static TBuilder AddOpenTelemetryExporters<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        var useOtlpExporter = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        if (useOtlpExporter)
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        // 将下列行 Azure 监测出口者(要求 Azure.Monitor.OpenTelemetry.AspNetCore 包)
        //是否(!)string.IsNullOrEmpty(builder.Configuration) (《关于推进的动态- 控制- 战略》)
        //{
        //    builder.Services.AddOpenTelemetry()
        //       .UseAzureMonitor();
        //}

        return builder;
    }

    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            // 添加默认活性检查以确保应用程序响应
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        // 在非发展环境中,在申请中增加健康检查终点对安全有影响。
        // 在非发展环境中实现这些终点之前,详见 https://aka.ms/dotnet/aspire/healthchecks 。
        if (app.Environment.IsDevelopment())
        {
            // 所有健康检查必须经过所有健康检查,才能认为申请在开始后可以接受交通
            app.MapHealthChecks("/health");

            // 只有贴有“活”标签的健康检查才能通过应用程序才被视为活
            app.MapHealthChecks("/alive", new HealthCheckOptions
            {
                Predicate = r => r.Tags.Contains("live")
            });
        }

        return app;
    }
}
