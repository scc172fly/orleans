using Orleans.Configuration;
using Orleans.Dashboard;
using Orleans.Runtime.MembershipService.SiloMetadata;
using System.Net;
using TestGrains;

//
// 此示例将 Dashboard 的 Minimal API 集成到客户端应用中。
//
var siloHostBuilder = Host.CreateApplicationBuilder(args);
siloHostBuilder.UseOrleans(builder =>
{
    builder.UseDevelopmentClustering(options => options.PrimarySiloEndpoint = new IPEndPoint(IPAddress.Loopback, 11111));
    builder.UseInMemoryReminderService();
    builder.AddMemoryGrainStorageAsDefault();
    builder.ConfigureEndpoints(IPAddress.Loopback, 11111, 30000);
    builder.AddDashboard();
});
siloHostBuilder.Services.AddSingleton<IHostedService, TestGrainsHostedService>();
using var siloHost = siloHostBuilder.Build();

await siloHost.StartAsync();

await Task.Delay(1000);

// 创建用于托管 Dashboard 的 WebApplication。
var dashboardBuilder = WebApplication.CreateBuilder(args);

// 配置 Orleans 客户端。
dashboardBuilder.UseOrleansClient(clientBuilder =>
{
    clientBuilder.UseStaticClustering(options => options.Gateways.Add(new IPEndPoint(IPAddress.Loopback, 30000).ToGatewayUri()));

    // 添加 Dashboard 服务。
    clientBuilder.AddDashboard();
});

var dashboardApp = dashboardBuilder.Build();

// 映射 Dashboard 端点。
dashboardApp.MapOrleansDashboard();

await dashboardApp.RunAsync();

await siloHost.StopAsync();
