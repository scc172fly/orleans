using Orleans.Configuration;
using Orleans.Dashboard;
using Orleans.Runtime.MembershipService.SiloMetadata;
using System.Net;
using TestGrains;

//
// 在此样本中,我们将Dashboard最小API纳入客户应用程序。
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

// 创建用于主机仪表板的 WebApplication
var dashboardBuilder = WebApplication.CreateBuilder(args);

// 配置 Orleans 客户端
dashboardBuilder.UseOrleansClient(clientBuilder =>
{
    clientBuilder.UseStaticClustering(options => options.Gateways.Add(new IPEndPoint(IPAddress.Loopback, 30000).ToGatewayUri()));

    // 添加仪表仪表仪表仪仪仪仪仪仪仪服务
    clientBuilder.AddDashboard();
});

var dashboardApp = dashboardBuilder.Build();

// 地图仪表板终端点
dashboardApp.MapOrleansDashboard();

await dashboardApp.RunAsync();

await siloHost.StopAsync();
