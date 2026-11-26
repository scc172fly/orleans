using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Hosting;
using Orleans.Runtime;
using Orleans.Dashboard.Implementation;
using Orleans.Dashboard.Implementation.Details;
using Orleans.Dashboard.Metrics;
using Orleans.Dashboard.Metrics.Details;
using Orleans.Dashboard.Model;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using Orleans.Dashboard.Core;
using Microsoft.AspNetCore.Mvc;
using Orleans.Configuration.Internal;

// ReSharper disable CheckNamespace
namespace Orleans.Dashboard;

/// <summary>
/// 提供配置和整合 Orleans 板的扩展方法。
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 添加 Orleans 向 silo 建筑商
    /// </summary>
    /// <param name="siloBuilder">下 silo 建筑商</param>
    /// <param name="configureOptions">用于 <see cref="DashboardOptions"/>.</param>
    /// <returns>下 silo 方法链的建筑工。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="siloBuilder"/> 无效。</exception>
    public static ISiloBuilder AddDashboard(this ISiloBuilder siloBuilder, Action<DashboardOptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(siloBuilder);
        siloBuilder.Services.AddOrleansDashboardForSiloCore(configureOptions);
        return siloBuilder;
    }

    internal static IServiceCollection AddOrleansDashboardForSiloCore(
        this IServiceCollection services,
        Action<DashboardOptions>? configureOptions = null)
    {
        services.AddGrainService<SiloGrainService>();
        if (!services.Any(descriptor => descriptor.ServiceType == typeof(DashboardHost)))
        {
            services.AddSingleton<DashboardHost>();
            services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<DashboardHost>());
            services.AddFromExisting<ILifecycleParticipant<ISiloLifecycle>, DashboardHost>();
        }
        services.Configure(configureOptions ?? (x => { }));
        services.AddSingleton<DashboardTelemetryExporter>();
        services.AddOptions<GrainProfilerOptions>();

        services.AddSingleton<EmbeddedAssetProvider>();
        services.AddSingleton<SiloStatusOracleSiloDetailsProvider>();
        services.AddSingleton<MembershipTableSiloDetailsProvider>();
        services.AddSingleton<IDashboardClient, DashboardClient>();
        services.AddSingleton<DashboardLogger>();
        services.AddFromExisting<ILoggerProvider, DashboardLogger>();
        services.AddSingleton<IGrainProfiler, GrainProfiler>();
        services.AddSingleton(c => (ILifecycleParticipant<ISiloLifecycle>)c.GetRequiredService<IGrainProfiler>());
        services.AddSingleton<IIncomingGrainCallFilter, GrainProfilerFilter>();
        services.AddSingleton<ISiloGrainClient, SiloGrainClient>();

        services.AddSingleton<ISiloDetailsProvider>(c => c.GetService<IMembershipTable>() switch
        {
            not null => c.GetRequiredService<MembershipTableSiloDetailsProvider>(),
            null => c.GetRequiredService<SiloStatusOracleSiloDetailsProvider>(),
        });

        services.TryAddSingleton(GrainProfilerFilter.DefaultGrainMethodFormatter);

        return services;
    }

    /// <summary>
    /// 地图图地图 Orleans 使用板头端点 ASP.NET 核心最低年度宣传单。
    /// 返回的路线组支持认证、授权和其他端点配置。
    /// </summary>
    /// <param name="endpoints">应用程序的端点路由建设器。</param>
    /// <param name="routePrefix">仪表板端点的可选基线。</param>
    /// <returns>仪表板路由组。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="endpoints"/> 无效。</exception>
    /// <example>
    /// <code>
    /// // Basic usage
    /// app.MapOrleansDashboard();
    ///
    /// // With authentication
    /// app.MapOrleansDashboard().RequireAuthorization();
    ///
    /// // 使用自定义基路径
    /// app.MapOrleansDashboard(routePrefix: "/dashboard");
    /// </code>
    /// </example>
    public static RouteGroupBuilder MapOrleansDashboard(
        this IEndpointRouteBuilder endpoints,
        [StringSyntax("Route")] string? routePrefix = null)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        // 创建静态资产提供商
        var assets = endpoints.ServiceProvider.GetService<EmbeddedAssetProvider>()
            ?? throw new InvalidOperationException("Orleans Dashboard services have not been registered. " +
                "Please call AddDashboard on ISiloBuilder or IClientBuilder.");

        // 为所有仪表板端点创建一个路径组
        var group = endpoints.MapGroup(routePrefix ?? "");

        // 静态资产 - 这些符合在已建 CSS/HTML 中引用的路径
        // 当指定了 routePrefix 时,将请求重定向而不留下尾巴以包括它。
        // 这确保了相对资产路径(如 index.min.js)的解决正确。
        group.MapGet("/", (HttpContext ctx) =>
        {
            if (!string.IsNullOrEmpty(routePrefix) && ctx.Request.Path.Value?.EndsWith('/') == false)
            {
                // 使用尾行斜线重定向到相同的路径, 保存查询字符串
                var redirectUrl = $"{ctx.Request.PathBase}{ctx.Request.Path}/{ctx.Request.QueryString}";
                return Results.Redirect(redirectUrl, permanent: true);
            }
            return assets.ServeAsset("index.html", ctx);
        });
        group.MapGet("/index.html", (HttpContext ctx) => assets.ServeAsset("index.html", ctx));
        group.MapGet("/favicon.ico", (HttpContext ctx) => assets.ServeAsset("favicon.ico", ctx));
        group.MapGet("/index.min.js", (HttpContext ctx) => assets.ServeAsset("index.min.js", ctx));
        group.MapGet("/index.css", (HttpContext ctx) => assets.ServeAsset("index.css", ctx));

        // 字体文件 - /fonts/ 目录的全抓图路径
        group.MapGet("/fonts/{**path}", (string path, HttpContext ctx) => assets.ServeAsset($"fonts.{path}", ctx));

        // 图像文件 - /img/ 目录的全抓图路径
        group.MapGet("/img/{**path}", (string path, HttpContext ctx) => assets.ServeAsset($"img.{path}", ctx));

        // API 终点
        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            IncludeFields = true,
            Converters = { new TimeSpanConverter() }
        };

        group.MapGet("/version", () => Results.Json(
            new { version = typeof(EmbeddedAssetProvider).Assembly.GetName().Version?.ToString() },
            jsonOptions));

        group.MapGet("/DashboardCounters", async (
            string[] exclude,
            [FromServices] IDashboardClient client,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await client.DashboardCounters(SanitizeExclusionFilters(exclude), cancellationToken);
                return Results.Json(result.Value, jsonOptions);
            }
            catch (SiloUnavailableException)
            {
                return CreateUnavailableResult(true);
            }
        });

        group.MapGet("/ClusterStats", async (
            [FromServices] IDashboardClient client,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await client.ClusterStats(cancellationToken);
                return Results.Json(result.Value, jsonOptions);
            }
            catch (SiloUnavailableException)
            {
                return CreateUnavailableResult(true);
            }
        });

        group.MapGet("/Reminders", async (
            [FromServices] IDashboardClient client,
            CancellationToken cancellationToken) =>
            await GetRemindersPage(1, client, jsonOptions, cancellationToken));
        group.MapGet("/Reminders/{page:int}", async (
            int page,
            [FromServices] IDashboardClient client,
            CancellationToken cancellationToken) =>
            await GetRemindersPage(page, client, jsonOptions, cancellationToken));

        group.MapGet("/HistoricalStats/{*path}", async (
            string path,
            [FromServices] IDashboardClient client,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await client.HistoricalStats(path, cancellationToken);
                return Results.Json(result.Value, jsonOptions);
            }
            catch (SiloUnavailableException)
            {
                return CreateUnavailableResult(true);
            }
        });

        group.MapGet("/SiloProperties/{*address}", async (
            string address,
            [FromServices] IDashboardClient client,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await client.SiloProperties(address, cancellationToken);
                return Results.Json(result.Value, jsonOptions);
            }
            catch (SiloUnavailableException)
            {
                return CreateUnavailableResult(true);
            }
        });

        group.MapGet("/SiloMetadata/{*address}", async (
            string address,
            [FromServices] IDashboardClient client,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await client.SiloMetadata(address, cancellationToken);
                return Results.Json(result.Value, jsonOptions);
            }
            catch (SiloUnavailableException)
            {
                return CreateUnavailableResult(true);
            }
        });

        group.MapGet("/SiloStats/{*address}", async (
            string address,
            [FromServices] IDashboardClient client,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await client.SiloStats(address, cancellationToken);
                return Results.Json(result.Value, jsonOptions);
            }
            catch (SiloUnavailableException)
            {
                return CreateUnavailableResult(true);
            }
        });

        group.MapGet("/SiloCounters/{*address}", async (
            string address,
            [FromServices] IDashboardClient client,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await client.GetCounters(address, cancellationToken);
                return Results.Json(result.Value, jsonOptions);
            }
            catch (SiloUnavailableException)
            {
                return CreateUnavailableResult(true);
            }
        });

        group.MapGet("/GrainStats/{*grainName}", async (
            string grainName,
            [FromServices] IDashboardClient client,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await client.GrainStats(grainName, cancellationToken);
                return Results.Json(result.Value, jsonOptions);
            }
            catch (SiloUnavailableException)
            {
                return CreateUnavailableResult(true);
            }
        });

        group.MapGet("/TopGrainMethods", async (
            string[] exclude,
            [FromServices] IDashboardClient client,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await client.TopGrainMethods(
                    take: 5,
                    exclusions: SanitizeExclusionFilters(exclude),
                    cancellationToken: cancellationToken);
                return Results.Json(result.Value, jsonOptions);
            }
            catch (SiloUnavailableException)
            {
                return CreateUnavailableResult(true);
            }
        });

        group.MapGet("/GrainState", async (HttpContext context, [FromServices] IDashboardClient client) =>
        {
            try
            {
                context.Request.Query.TryGetValue("grainId", out var grainId);
                context.Request.Query.TryGetValue("grainType", out var grainType);
                var result = await client.GetGrainState(grainId, grainType, context.RequestAborted);
                return Results.Json(result.Value, jsonOptions);
            }
            catch (SiloUnavailableException)
            {
                return CreateUnavailableResult(true);
            }
        });

        group.MapGet("/LifecycleStages", async (
            [FromServices] IDashboardClient client,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await client.GetLifecycleStages(cancellationToken);
                return Results.Json(result.Value, jsonOptions);
            }
            catch (SiloUnavailableException)
            {
                return CreateUnavailableResult(true);
            }
        });

        group.MapGet("/GrainTypes", async (
            string[] exclude,
            [FromServices] IDashboardClient client,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await client.GetGrainTypes(SanitizeExclusionFilters(exclude), cancellationToken);
                return Results.Json(result.Value, jsonOptions);
            }
            catch (SiloUnavailableException)
            {
                return CreateUnavailableResult(true);
            }
        });

        group.MapGet("/Trace", async (HttpContext context, [FromServices] IOptions<DashboardOptions> opts, [FromServices] DashboardLogger logger) =>
        {
            if (opts.Value.HideTrace)
            {
                return Results.Problem("The trace endpoint is disabled in the dashboard options.",
                    title: "Trace Endpoint Disabled", statusCode: StatusCodes.Status403Forbidden);
            }

            await StreamTraceAsync(context, logger);
            return Results.Empty;
        });

        return group;
    }

    private static async Task<IResult> GetRemindersPage(
        int page,
        IDashboardClient client,
        JsonSerializerOptions jsonOptions,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await client.GetReminders(page, 50, cancellationToken);
            return Results.Json(result.Value, jsonOptions);
        }
        catch (SiloUnavailableException)
        {
            return CreateUnavailableResult(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // 如果提醒未配置, 返回空响应
            return Results.Json(new ReminderResponse { Reminders = [], Count = 0 }, jsonOptions);
        }
    }

    private static async Task StreamTraceAsync(HttpContext context, DashboardLogger logger)
    {
        var token = context.RequestAborted;

        try
        {
            await using var writer = new TraceWriter(logger, context);
            await writer.WriteAsync("""
                   ____       _                        _____            _     _                         _
                  / __ \     | |                      |  __ \          | |   | |                       | |
                 | |  | |_ __| | ___  __ _ _ __  ___  | |  | | __ _ ___| |__ | |__   ___   __ _ _ __ __| |
                 | |  | | '__| |/ _ \/ _` | '_ \/ __| | |  | |/ _` / __| '_ \| '_ \ / _ \ / _` | '__/ _` |
                 | |__| | |  | |  __/ (_| | | | \__ \ | |__| | (_| \__ \ | | | |_) | (_) | (_| | | | (_| |
                  \____/|_|  |_|\___|\__,_|_| |_|___/ |_____/ \__,_|___/_| |_|_.__/ \___/ \__,_|_|  \__,_|

                You are connected to the Orleans Dashboard log streaming service
                """);

            await Task.Delay(TimeSpan.FromMinutes(60), token);
            await writer.WriteAsync("Disconnecting after 60 minutes\r\n");
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static IResult CreateUnavailableResult(bool lostConnectivity)
    {
        var message = lostConnectivity
            ? "The dashboard has lost connectivity with the Orleans cluster"
            : "The dashboard is still trying to connect to the Orleans cluster";

        return Results.Text(message, "text/plain", statusCode: 503);
    }

    private static string[] SanitizeExclusionFilters(string[] filters)
    {
        return (filters == null || filters.Length == 0) ? [] : [.. filters
            .Select(f => f.Trim())
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(f => string.Concat(f, '.'))];
    }

    /// <summary>
    /// 添加 Orleans 向 Orleans 客户端构建器
    /// 允许您主机 Orleans 键 Orleans 客户端,只要 silos 还将仪表板加进。
    /// </summary>
    /// <param name="clientBuilder">客户端构建器。</param>
    /// <param name="configureOptions">用于 <see cref="DashboardOptions"/>.</param>
    /// <returns>用于方法链的客户端构建器。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="clientBuilder"/> 无效。</exception>
    public static IClientBuilder AddDashboard(this IClientBuilder clientBuilder, Action<DashboardOptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(clientBuilder);
        clientBuilder.Services.Configure(configureOptions ?? (x => { }));
        clientBuilder.Services.AddSingleton<DashboardLogger>();
        clientBuilder.Services.AddFromExisting<ILoggerProvider, DashboardLogger>();
        clientBuilder.Services.AddSingleton<IDashboardClient, DashboardClient>();
        clientBuilder.Services.AddSingleton<EmbeddedAssetProvider>();

        return clientBuilder;
    }
}
