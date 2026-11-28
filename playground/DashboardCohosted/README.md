<a id="orleans-dashboard---cohosted-example"></a>

# Orleans Dashboard - 同宿主示例

此示例演示如何在运行 Orleans Silo 的同一个 Web 应用程序中共同托管 Orleans Dashboard。

<a id="overview"></a>

## 总览

这是向应用程序添加 Orleans Dashboard 最简单的方式。Dashboard 使用 ASP.NET Core 最小 API，与 Silo 托管在同一个进程中。

<a id="key-features"></a>

## 主要特性

- **单进程**：Orleans 和 Dashboard 在同一个 Web 应用程序中运行
- **最简配置**：仅需几行代码即可完成设置
- **ASP.NET Core 集成**：使用 `WebApplication.CreateBuilder()` 和最小 API

<a id="how-it-works"></a>

## 工作原理

本示例演示：

1. 使用 `WebApplication.CreateBuilder()` 创建 Web 应用程序
2. 使用 `builder.UseOrleans()` 配置 Orleans
3. 使用 `siloBuilder.AddDashboard()` 添加 Dashboard
4. 使用 `app.MapOrleansDashboard()` 映射 Dashboard 终结点

<a id="running-the-example"></a>

## 运行示例

```bash
dotnet run
```

启动后，在浏览器中打开：
- **Dashboard**：https://localhost:55355/ （或 http://localhost:55356/ ）

<a id="code-walkthrough"></a>

## 代码解读

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

## 适用场景

在以下情况下使用同宿主方式：
- 希望采用最简单的配置
- 正在构建同时托管 Orleans 的 Web 应用程序
- 希望通过与 Web 应用相同的端口访问 Dashboard
- 只运行一个 Silo，或不需要集中式 Dashboard

<a id="customization"></a>

## 自定义配置

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

<a id="configure-update-interval"></a>

### 配置更新间隔

```csharp
siloBuilder.AddDashboard(options =>
{
    options.CounterUpdateIntervalMs = 2000; // Update every 2 seconds
});
```

<a id="important-notes"></a>

## 重要说明

> [!WARNING]
> Orleans Dashboard **仅为开发和测试场景设计**。不建议用于生产部署，因为它可能对集群性能产生显著影响。

<a id="related-examples"></a>

## 相关示例

- **DashboardSeparateHost**：演示如何在独立应用程序中托管 Dashboard
- **DashboardToy**：集成 .NET Aspire 的进阶示例

<a id="learn-more"></a>

## 了解更多

- [Orleans Dashboard 文档](../../../src/Dashboard/Orleans.Dashboard/README.md)
- [Microsoft Orleans 文档](https://learn.microsoft.com/dotnet/orleans/)
