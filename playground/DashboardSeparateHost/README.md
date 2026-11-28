<a id="orleans-dashboard---separate-host-example"></a>

# Orleans Dashboard - 独立宿主示例

此示例演示如何在独立 Web 应用程序中托管 Orleans Dashboard，该应用作为客户端连接 Orleans 集群。

<a id="overview"></a>

## 总览

此方式将 Dashboard Web 服务与 Silo 分离。

<a id="key-features"></a>

## 主要特性

- **独立进程**：Dashboard Web 服务独立于 Orleans Silo 运行
- **客户端连接**：作为 Orleans 客户端连接 Orleans 集群
- **独立 Silo**：包含用于演示的独立 Silo 宿主
- **测试 Grain**：包含用于产生 Dashboard 活动数据的测试 Grain

<a id="how-it-works"></a>

## 工作原理

本示例由两个组件组成：

1. **Silo 宿主**：独立运行的 Orleans Silo
2. **Dashboard Web 应用**：作为 Orleans 客户端连接集群并托管 Dashboard 的 Web 应用程序

<a id="running-the-example"></a>

## 运行示例

```bash
dotnet run
```

应用程序将：
1. 在 11111（Silo）和 30000（网关）端口启动 Orleans Silo
2. 启动 Dashboard Web 应用程序
3. 自动产生测试 Grain 活动

启动后，在浏览器中访问默认端口（通常为 https://localhost:5001 或 http://localhost:5000 ）。

<a id="code-walkthrough"></a>

## 代码解读

<a id="silo-host"></a>

### Silo 宿主

```csharp
var siloHost = Host.CreateDefaultBuilder(args)
    .UseOrleans((_, builder) =>
    {
        builder.UseDevelopmentClustering(options =>
            options.PrimarySiloEndpoint = new IPEndPoint(IPAddress.Loopback, 11111));
        builder.ConfigureEndpoints(IPAddress.Loopback, 11111, 30000);
        builder.AddDashboard();
        // ... other configuration
    })
    .Build();
```

<a id="dashboard-client"></a>

### Dashboard 客户端

```csharp
var dashboardBuilder = WebApplication.CreateBuilder(args);

// Configure Orleans client
dashboardBuilder.UseOrleansClient(clientBuilder =>
{
    clientBuilder.UseStaticClustering(options =>
        options.Gateways.Add(new IPEndPoint(IPAddress.Loopback, 30000).ToGatewayUri()));

    // Add dashboard services
    clientBuilder.AddDashboard();
});

var app = dashboardBuilder.Build();

// Map dashboard endpoints
app.MapOrleansDashboard();

await app.RunAsync();
```


<a id="architecture"></a>

## 架构

```
┌─────────────────┐
│  Silo Host      │
│  (Port 11111)   │
│  Gateway: 30000 │
└────────▲────────┘
         │
         │ Orleans Client Connection
         │
┌────────┴────────┐
│  Dashboard      │
│  Web App        │
│  (Port 5000)    │
└─────────────────┘
```

<a id="customization"></a>

## 自定义配置

<a id="connect-to-remote-cluster"></a>

### 连接远程集群

```csharp
dashboardBuilder.UseOrleansClient(clientBuilder =>
{
    clientBuilder.UseStaticClustering(options =>
    {
        options.Gateways.Add(new IPEndPoint(IPAddress.Parse("10.0.0.1"), 30000).ToGatewayUri());
        options.Gateways.Add(new IPEndPoint(IPAddress.Parse("10.0.0.2"), 30000).ToGatewayUri());
    });

    clientBuilder.AddDashboard();
});
```

<a id="custom-route-prefix"></a>

### 自定义路由前缀

```csharp
app.MapOrleansDashboard(routePrefix: "/dashboard");
```

<a id="add-authentication"></a>

### 添加身份验证

```csharp
app.MapOrleansDashboard().RequireAuthorization();
```

<a id="important-notes"></a>

## 重要说明

> [!WARNING]
> Orleans Dashboard **仅为开发和测试场景设计**。不建议用于生产部署，因为它可能对集群性能产生显著影响。

<a id="related-examples"></a>

## 相关示例

- **DashboardCohosted**：演示如何在 Silo 内托管 Dashboard
- **DashboardToy**：集成 .NET Aspire 的进阶示例

<a id="learn-more"></a>

## 了解更多

- [Orleans Dashboard 文档](../../../src/Dashboard/Orleans.Dashboard/README.md)
- [Orleans 客户端配置](https://learn.microsoft.com/dotnet/orleans/host/configuration-guide/client-configuration)
- [Microsoft Orleans 文档](https://learn.microsoft.com/dotnet/orleans/)
