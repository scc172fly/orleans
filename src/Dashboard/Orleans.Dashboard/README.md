<a id="microsoft-orleans-dashboard"></a>

# Microsoft Orleans 机表

<a id="introduction"></a>

## 简介
Microsoft Orleans 机型仪表板是用于 Orleans 应用程序的网络监测和诊断工具,通过直观的网络界面提供实时可视化的启动 grain 、运行时统计、 silo 健康以及性能衡量标准。

<a id="features"></a>

## 功能
- **实时 Grain 统计**:监测活动 grain 计数、呼号率和例外率
- **Silo 健康监测**: 查看 silo 状况、CPU使用、内存消耗和网络活动
- **Silo 对数台**:从网关读到连接客户,从 silo仪表板遥测收集中累积电文字节阅读,每秒刷新。字节总数包括自收集开始以来记录的流量。
- **响应导航**:使用窄屏幕上的页眉按钮打开导航菜单;以“外观”、“背景”或“导航链接”将其删除
- **性能度量**:跟踪方法调用延时、吞吐和误差率
- **Grain 方法分析**:分析个人 grain 方法性能
- **催问跟踪**:在整个组群中看到预定的催问
- **机首板自定义**:配置认证、端口和其他选项

<a id="getting-started"></a>

## 入门
要使用此软件包, 请通过 NuGet安装 :

```shell
dotnet add package Microsoft.Orleans.Dashboard
```

<a id="example---adding-dashboard-to-a-silo"></a>

## 示例 - 将布板添加到 Silo

```csharp
using Orleans.Dashboard;

var builder = WebApplication.CreateBuilder(args);

// Configure Orleans
builder.UseOrleans(siloBuilder =>
{
    siloBuilder.UseLocalhostClustering();
    siloBuilder.UseInMemoryReminderService();
    siloBuilder.AddMemoryGrainStorageAsDefault();

    // Add the dashboard
    siloBuilder.AddDashboard();
});

var app = builder.Build();

// Map dashboard endpoints
app.MapOrleansDashboard();

app.Run();
```

仪表板可在应用程序的基本 URL (e.g., `http://localhost:5000/`)上查阅。

<a id="example---adding-dashboard-to-a-separate-web-application"></a>

## 示例 - 将显示板添加到单独网页应用程序

对于您想要将仪表板与您的 silos分开托管的设想情景:

```csharp
using Orleans.Dashboard;
using System.Net;

var builder = WebApplication.CreateBuilder(args);

// Configure Orleans client
builder.UseOrleansClient(clientBuilder =>
{
    clientBuilder.UseStaticClustering(options =>
        options.Gateways.Add(new IPEndPoint(IPAddress.Loopback, 30000).ToGatewayUri()));

    // Add dashboard services
    clientBuilder.AddDashboard();
});

var app = builder.Build();

// Map dashboard endpoints
app.MapOrleansDashboard();

await app.RunAsync();
```

<a id="configuration-options"></a>

## 配置选项

仪表板可以配置各种选项:

```csharp
siloBuilder.AddDashboard(options =>
{
    options.CounterUpdateIntervalMs = 1000; // Metrics update interval (default: 1000ms)
});
```

您可以在绘制仪表板端点时自定义路径前缀 :

```csharp
// Map dashboard at a custom path
app.MapOrleansDashboard(routePrefix: "/dashboard");

// Add authentication
app.MapOrleansDashboard().RequireAuthorization();
```

<a id="dashboard-url"></a>

## 显示板 URL
仪表板配置后,可在下列地点进入:
- 默认值: `http://localhost:{AppPort}/` ( AppPort 是您的网络应用程序端口)
- 与 routePrefix: `http://localhost:{AppPort}/{routePrefix}/`

<a id="examples"></a>

## 示例
完整的工作实例见操场项目:
- `playground/DashboardCohosted` - 与 Orleans 在网上应用程序中共同主办了达什板
- `playground/DashboardSeparateHost` - 单独一个连接到 Orleans 群集的网络应用程序中的机件板

<a id="documentation"></a>

## 文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件
关于更全面的文件,请参见:
- [Microsoft Orleans 文档](https://dotnet.github.io/orleans/docs/)
- [Orleans 易观测](https://dotnet.github.io/orleans/docs/host/monitoring/)
- [服务器配置](https://dotnet.github.io/orleans/docs/host/configuration-guide/server-configuration/)

<a id="feedback--contributing"></a>

## 反馈和贡献
- 如果有任何问题或希望提供反馈,请 [GitHub上的开关](https://github.com/dotnet/orleans/issues)
- 加入我们社区 [不一致](https://aka.ms/orleans-discord)
- 跟随 [@msftorleans 地图上写着:](https://twitter.com/msftorleans) 推特账户发布 Orleans 通知
- 请审查我们的 [会费捐款准则](https://github.com/dotnet/orleans/blob/main/CONTRIBUTING.md)
- 该项目在 [麻省理工学院许可证](https://github.com/dotnet/orleans/blob/main/LICENSE)下获得许可证
