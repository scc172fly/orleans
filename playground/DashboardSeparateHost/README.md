<a id="orleans-dashboard---separate-host-example"></a>

# Orleans 键盘 - 单独主机

本示例显示如何将 Orleans 机头板作为单独的网络应用程序,作为客户连接到 Orleans 组群。

<a id="overview"></a>

## 概述

此方法将仪表板网络服务与您的 silos区分开来。

<a id="key-features"></a>

## 关键字属性

- **独立进程**:Dashboard网络服务独立于 Orleans silos
- **客户连接**:连接到 Orleans 以组为集合 Orleans 客户
- **独立 Silo**:包括一个单独的 silo 证明东道主
- **测试 Grains**:包括为仪表板生成活动而举行的测试 grains

<a id="how-it-works"></a>

## 如何运作

实例由两个组成部分组成:

1. **Silo 主机**:独立运行的独立 Orleans silo
2. **达什板网页应用程序**:一个作为 Orleans 客户端连接并主持仪表板的网络应用程序

<a id="running-the-example"></a>

## 运行示例

```bash
dotnet run
```

申请将:
1. 开始 Orleans silo 11111(港口港口和港口港 11111(silo和30000(起点)
2. 启动仪表板网络应用程序
3. 自动生成测试 grain 活动

一旦运行, 将浏览器打开到默认端口( 通常为 https://localhost:5001 或 http://localhost:5000) 。

<a id="code-walkthrough"></a>

## 代码走过

<a id="silo-host"></a>

### Silo 主机

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

### 仪表板客户

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

## 建筑结构架构

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

## 自定义

<a id="connect-to-remote-cluster"></a>

### 连接到远程集

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

### 添加认证

```csharp
app.MapOrleansDashboard().RequireAuthorization();
```

<a id="important-notes"></a>

## 重要注释

> [!WARNING]
> Orleans 机头板的设计仅用于 **开发和测试情景**。不建议进行生产部署,因为其能对您的组群产生显著性能影响。

<a id="related-examples"></a>

## 相关实例

- **DashboardCohosted**:显示如何在 silo内存放仪表板
- **DashboardToy**:高级示例, .NET 临时整合

<a id="learn-more"></a>

## 学习更多

- [Orleans 机布文档](../../../src/Dashboard/Orleans.Dashboard/README.md)
- [Orleans 客户端配置](https://learn.microsoft.com/dotnet/orleans/host/configuration-guide/client-configuration)
- [Microsoft Orleans 文档](https://learn.microsoft.com/dotnet/orleans/)
