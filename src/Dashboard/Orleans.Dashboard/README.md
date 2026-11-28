<a id="microsoft-orleans-dashboard"></a>

# Microsoft Orleans Dashboard

<a id="introduction"></a>

## 简介
Microsoft Orleans Dashboard 是 Orleans 应用程序的 Web 监控和诊断工具。它通过直观的 Web 界面实时呈现 Grain 激活实例、运行时统计信息、Silo 健康状况及性能指标。

<a id="features"></a>

## 功能
- **实时 Grain 统计信息**：监控活跃 Grain 数量、调用速率和异常发生率
- **Silo 健康监控**：查看 Silo 状态、CPU 使用率、内存消耗及网络活动
- **Silo 计数器**：从网关获取已连接客户端信息，从 Silo 的 Dashboard 遥测采集中获取累计消息字节数，每秒刷新一次。累计字节数涵盖自采集开始以来记录的流量。
- **响应式导航**：在窄屏上使用页眉按钮打开导航菜单；通过 Escape 键、背景遮罩或导航链接关闭菜单
- **性能指标**：跟踪方法调用延迟、吞吐量及错误率
- **Grain 方法性能分析**：分析各个 Grain 方法的性能
- **提醒器跟踪**：查看整个集群中已安排的提醒器
- **Dashboard 自定义**：配置身份验证、端口及其他选项

<a id="getting-started"></a>

## 入门
要使用此包，请通过 NuGet 安装：

```shell
dotnet add package Microsoft.Orleans.Dashboard
```

<a id="example---adding-dashboard-to-a-silo"></a>

## 示例 - 将 Dashboard 添加到 Silo

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

可通过应用程序的基 URL 访问 Dashboard（例如 `http://localhost:5000/`）。

<a id="example---adding-dashboard-to-a-separate-web-application"></a>

## 示例 - 将 Dashboard 添加到独立 Web 应用程序

适用于希望将 Dashboard 与 Silo 分开托管的场景：

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

可以使用多种选项配置 Dashboard：

```csharp
siloBuilder.AddDashboard(options =>
{
    options.CounterUpdateIntervalMs = 1000; // Metrics update interval (default: 1000ms)
});
```

映射 Dashboard 终结点时，可以自定义路由前缀：

```csharp
// Map dashboard at a custom path
app.MapOrleansDashboard(routePrefix: "/dashboard");

// Add authentication
app.MapOrleansDashboard().RequireAuthorization();
```

<a id="dashboard-url"></a>

## Dashboard URL
完成配置后，可通过以下地址访问 Dashboard：
- 默认：`http://localhost:{AppPort}/`（AppPort 为 Web 应用程序的端口）
- 指定 routePrefix：`http://localhost:{AppPort}/{routePrefix}/`

<a id="examples"></a>

## 示例
有关完整可运行的示例，请参阅实验项目：
- `playground/DashboardCohosted` - Dashboard 与 Orleans 在同一个 Web 应用程序中共同托管
- `playground/DashboardSeparateHost` - Dashboard 位于独立 Web 应用程序中并连接到 Orleans 集群

<a id="documentation"></a>

## 文档
有关更全面的文档，请参阅：
- [Microsoft Orleans 文档](https://dotnet.github.io/orleans/docs/)
- [Orleans 可观测性](https://dotnet.github.io/orleans/docs/host/monitoring/)
- [服务器配置](https://dotnet.github.io/orleans/docs/host/configuration-guide/server-configuration/)

<a id="feedback--contributing"></a>

## 反馈与贡献
- 如果遇到问题或希望提供反馈，请[在 GitHub 上创建 Issue](https://github.com/dotnet/orleans/issues)
- 加入我们的 [Discord](https://aka.ms/orleans-discord) 社区
- 关注 Twitter 上的 [@msftorleans](https://twitter.com/msftorleans) 账号，获取 Orleans 公告
- 欢迎贡献！请阅读我们的[贡献指南](https://github.com/dotnet/orleans/blob/main/CONTRIBUTING.md)
- 本项目采用 [MIT 许可证](https://github.com/dotnet/orleans/blob/main/LICENSE)
