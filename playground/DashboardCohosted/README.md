<a id="orleans-dashboard---cohosted-example"></a>

# Orleans 机首板 - 共同主办的示例

本例子说明如何共同主办 Orleans 在同一网络应用程序中运行您的 Orleans silo.

<a id="overview"></a>

## 概述

这是将 Orleans 头板添加到您的应用程序中的最简单方式。 仪表板与您 silo一样, 使用 ASP.NET 核心最小API , 以同样程序托管。

<a id="key-features"></a>

## 关键字属性

- **单一过程**: Orleans 和在同一个网络应用程序中运行的仪表板都在同一网上应用中
- **最小配置**:简单设置,仅使用几行代码
- **[ASP.NET 核心整合**:使用 `WebApplication.CreateBuilder()` 和最低API

<a id="how-it-works"></a>

## 如何运作

举例显示:

1. 创建有 `WebApplication.CreateBuilder()`的网络应用程序
2. 配置 Orleans 使用 `builder.UseOrleans()`
3. 添加 `siloBuilder.AddDashboard()`
4. 带有 `app.MapOrleansDashboard()`的绘图仪表板端点

<a id="running-the-example"></a>

## 运行示例

```bash
dotnet run
```

一旦运行, 打开您的浏览器到 :
- **仪表板**: https://localhost:55355/ (或) http://localhost:55356/)

<a id="code-walkthrough"></a>

## 代码走过

```csharp
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

<a id="when-to-use-this-approach"></a>

## 何时使用此方法

使用此联合主持方式当 :
- 您想要最简单的设置
- 您正在建立一个网络应用程序, 同时也主机 Orleans
- 您想要在您的网络应用程序的同一个端口使用仪表板
- 运行一个单 silo 或不需要集中仪表板

<a id="customization"></a>

## 自定义

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

<a id="configure-update-interval"></a>

### 配置更新间隔

```csharp
siloBuilder.AddDashboard(options =>
{
    options.CounterUpdateIntervalMs = 2000; // Update every 2 seconds
});
```

<a id="important-notes"></a>

## 重要注释

> [!WARNING]
> Orleans 机头板的设计仅用于 **开发和测试情景**。不建议进行生产部署,因为其能对您的组群产生显著性能影响。

<a id="related-examples"></a>

## 相关实例

- **DashboardSeparateHost**:显示如何在单独的应用程序中托管仪表板
- **DashboardToy**:高级示例, .NET 临时整合

<a id="learn-more"></a>

## 学习更多

- [Orleans 机布文档](../../../src/Dashboard/Orleans.Dashboard/README.md)
- [Microsoft Orleans 文档](https://learn.microsoft.com/dotnet/orleans/)
