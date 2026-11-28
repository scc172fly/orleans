<a id="microsoft-orleans-clustering-provider-for-adonet"></a>

# Microsoft Orleans ADO.NET 集群提供程序

<a id="introduction"></a>

## 简介
Microsoft Orleans ADO.NET 集群提供程序允许 Orleans Silo 通过 ADO.NET 使用关系数据库组成集群。该提供程序使 Silo 能够彼此发现、维护集群成员资格，以及检测并处理故障。

<a id="getting-started"></a>

## 入门
要使用此包，请通过 NuGet 安装：

```shell
dotnet add package Microsoft.Orleans.Clustering.AdoNet
```

还需要为所用数据库系统安装相应的数据库驱动程序包：

- SQL Server: `Microsoft.Data.SqlClient`
- MySQL: `MySql.Data` 或 `MySqlConnector`
- PostgreSQL: `Npgsql`
- Oracle: `Oracle.ManagedDataAccess.Core`
- SQLite: `Microsoft.Data.Sqlite`

<a id="example---configuring-adonet-clustering"></a>

## 示例 - 配置 ADO.NET 集群

```csharp
using Microsoft.Extensions.Hosting;
using Orleans.Configuration;
using Orleans.Hosting;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;

// Define a grain interface
public interface IHelloGrain : IGrainWithStringKey
{
    Task<string> SayHello(string greeting);
}

// Implement the grain interface
public class HelloGrain : Grain, IHelloGrain
{
    public Task<string> SayHello(string greeting)
    {
        return Task.FromResult($"Hello, {greeting}!");
    }
}

var builder = Host.CreateApplicationBuilder(args)
    .UseOrleans(siloBuilder =>
    {
        siloBuilder
            // Configure ADO.NET for clustering
            .UseAdoNetClustering(options =>
            {
                options.Invariant = "System.Data.SqlClient";  // Or other providers like "MySql.Data.MySqlClient", "Npgsql", etc.
                options.ConnectionString = "Server=localhost;Database=OrleansCluster;User Id=myUsername;******;";
            });
    });

var host = builder.Build();
await host.StartAsync();

// Get a reference to a grain and call it
var client = host.Services.GetRequiredService<IClusterClient>();
var grain = client.GetGrain<IHelloGrain>("user123");
var response = await grain.SayHello("World");

// Print the result
Console.WriteLine($"Grain response: {response}");

// Keep the host running until the application is shut down
await host.WaitForShutdownAsync();
```

<a id="example---configuring-client-to-connect-to-cluster"></a>

## 示例 - 配置客户端连接到集群

```csharp
using Microsoft.Extensions.Hosting;
using Orleans;
using Orleans.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;

// Define a grain interface
public interface IHelloGrain : IGrainWithStringKey
{
    Task<string> SayHello(string greeting);
}

var clientBuilder = Host.CreateApplicationBuilder(args)
    .UseOrleansClient(clientBuilder =>
    {
        clientBuilder
            // Configure the client to use ADO.NET for clustering
            .UseAdoNetClustering(options =>
            {
                options.Invariant = "Microsoft.Data.SqlClient";  // Or other providers like "MySql.Data.MySqlClient", "Npgsql", etc.
                options.ConnectionString = "Server=localhost;Database=OrleansCluster;User Id=myUsername;******;";
            });
    });

var host = clientBuilder.Build();
await host.StartAsync();
var client = host.Services.GetRequiredService<IClusterClient>();

// Get a reference to a grain and call it
var grain = client.GetGrain<IHelloGrain>("user123");
var response = await grain.SayHello("World");

// Print the result
Console.WriteLine($"Grain response: {response}");

// Keep the host running until the application is shut down
await host.WaitForShutdownAsync();
```

<a id="database-setup"></a>

## 数据库设置

使用 ADO.NET 集群提供程序之前，需要创建所需的数据库表。Orleans 源码仓库提供了不同数据库系统的脚本：
namespace ExampleGrains;

- [SQL Server 脚本](https://github.com/dotnet/orleans/tree/main/src/AdoNet/Orleans.Clustering.AdoNet/SQLServer-Clustering.sql)
- [MySQL 脚本](https://github.com/dotnet/orleans/tree/main/src/AdoNet/Orleans.Clustering.AdoNet/MySQL-Clustering.sql)
- [PostgreSQL 脚本](https://github.com/dotnet/orleans/tree/main/src/AdoNet/Orleans.Clustering.AdoNet/PostgreSQL-Clustering.sql)
- [Oracle 脚本](https://github.com/dotnet/orleans/tree/main/src/AdoNet/Orleans.Clustering.AdoNet/Oracle-Clustering.sql)

<a id="optional-membership-sql-enhancements"></a>

### 可选的成员资格 SQL 增强功能

每个 Silo 自行负责其心跳。一次心跳是针对主键向 `IAmAliveTime` 赋值一次，并作为单条数据库命令发送。心跳不会改变逻辑成员资格行及表的版本令牌，因此成员资格更新可以使用心跳前获取的令牌。权威成员资格的一致性由这些逻辑令牌保证；心跳时间戳只是尽力而为的存活状态观测值。

升级 Silo 或使用 ADO.NET 发现网关的客户端时，仍支持现有已安装的查询定义。数据库更新是可选的。如果使用现有查询目录，清理操作将继续采用其中安装的 `CleanupDefunctSiloEntriesKey` 查询及其行为。

现有 `*-Clustering.sql` 安装脚本包含改进后的条件写入回滚和 Dead 状态行清理功能。其可选的 `CleanupDefunctSiloEntryKey` 支持依据最新的启动、心跳和怀疑时间戳进行清理，并通过基于已捕获行的条件保护并发变更。全新安装通过常规数据库设置获得这些定义。

SQL Server 对成员资格的联接读取使用 `READ_COMMITTED_SNAPSHOT` 提供的语句级快照，现有 `SQLServer-Main.sql` 设置脚本会启用此选项。

提供程序在初始化时加载并缓存查询。现有部署继续使用已安装的查询定义。运维人员如选择采用修订后的定义，可以使用正常的数据库变更管理流程；之后重新初始化提供程序，以加载修订后的查询目录。原有查询键和参数集仍受支持。

SQL 增强功能在各进程采用更新后的查询时分别生效。在此之前，缓存的 SQL Server/MySQL 更新查询仍保留原有的行不存在时递增版本的行为，缓存的清理查询仍使用原有状态筛选条件。所有成员资格写入者均采用这些定义后，增强行为才在整个集群生效。SQL Server 和 MySQL 的插入操作保留原先先写入行、再更新版本的语句顺序。

<a id="documentation"></a>

## 文档
有关更全面的文档，请参阅：
- [Microsoft Orleans 文档](https://dotnet.github.io/orleans/docs/)
- [集群提供程序](https://dotnet.github.io/orleans/docs/implementation/cluster-management/)
- [ADO.NET 数据库配置](https://dotnet.github.io/orleans/docs/host/configuration-guide/adonet-configuration/)

<a id="feedback--contributing"></a>

## 反馈与贡献
- 如果遇到问题或希望提供反馈，请[在 GitHub 上创建 Issue](https://github.com/dotnet/orleans/issues)
- 加入我们的 [Discord](https://aka.ms/orleans-discord) 社区
- 关注 Twitter 上的 [@msftorleans](https://twitter.com/msftorleans) 账号，获取 Orleans 公告
- 欢迎贡献！请阅读我们的[贡献指南](https://github.com/dotnet/orleans/blob/main/CONTRIBUTING.md)
- 本项目采用 [MIT 许可证](https://github.com/dotnet/orleans/blob/main/LICENSE)
