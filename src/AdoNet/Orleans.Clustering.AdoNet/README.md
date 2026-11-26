<a id="microsoft-orleans-clustering-provider-for-adonet"></a>

# Microsoft Orleans 集束器 ADO.NET

<a id="introduction"></a>

## 简介
Microsoft Orleans 集束器 ADO.NET 允许 Orleans silos 将自己组织成一个集群,通过 ADO.NET。此提供者启用 silos 相互发现,保持集群成员身份,并发现和处理失败。

<a id="getting-started"></a>

## 入门
要使用此软件包, 请通过 NuGet安装 :

```shell
dotnet add package Microsoft.Orleans.Clustering.AdoNet
```

您还需要为您的数据库系统安装合适的数据库驱动程序包 :

- SQL 服务器: `Microsoft.Data.SqlClient`
- MySQL: `MySql.Data` 或 `MySqlConnector`
- PostgreSQL: `Npgsql`
- 甲骨文: `Oracle.ManagedDataAccess.Core`
- SQLite: `Microsoft.Data.Sqlite`

<a id="example---configuring-adonet-clustering"></a>

## 示例 - 配置 ADO.NET 群集

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

在使用 ADO.NET 组合集提供者之前,您需要先建立必要的数据库表格。不同的数据库系统的脚本可在 Orleans 源库中查到:
名称空间 ExampleGrains;

- [SQL 服务器脚本](https://github.com/dotnet/orleans/tree/main/src/AdoNet/Orleans.Clustering.AdoNet/SQLServer-Clustering.sql)
- [MySQL 脚本](https://github.com/dotnet/orleans/tree/main/src/AdoNet/Orleans.Clustering.AdoNet/MySQL-Clustering.sql)
- [PostgreSQL 脚本](https://github.com/dotnet/orleans/tree/main/src/AdoNet/Orleans.Clustering.AdoNet/PostgreSQL-Clustering.sql)
- [甲骨文脚本](https://github.com/dotnet/orleans/tree/main/src/AdoNet/Orleans.Clustering.AdoNet/Oracle-Clustering.sql)

<a id="optional-membership-sql-enhancements"></a>

### 选择性成员 SQL 强化

每个 silo 都有心跳。心跳是作为单一数据库命令发送的 `IAmAliveTime`的主要关键目标任务之一。心跳保留了逻辑成员行和表格符号,因此会员更新可以使用心跳前所捕捉的标记。能成为会员的一致性由这些逻辑标志决定;心跳时间戳是最理想的生命观察。

升级 silos 或 ADO.NET 网关发现客户端保留对现有查询装置的支持。数据库更新是可选的。有了现有的目录,清理工作将继续使用安装的 `CleanupDefunctSiloEntriesKey` 查询和行为。

现有的 `*-Clustering.sql` 安装脚本包括改进有条件的滚回和死递清理。它们的可选 `CleanupDefunctSiloEntryKey` 能够根据最新的开始、心跳和怀疑时间戳进行清理,并有一个捕捉到的行时戳条件来保护同时的变化。新设施通过通常的数据库设置来接收这些定义。

SQL 服务器加入为成员, `READ_COMMITTED_SNAPSHOT`现有 `SQLServer-Main.sql` 设置启用 。

初始化过程中供应商的负载和缓存查询。 现有的部署继续使用已安装的定义。 选择采用订正定义的操作员可以使用正常的数据库变更管理程序; 在启动时重新启用供应商以装入订正目录。 原始查询键和参数组仍然支持 。

SQL 增强功能在通过更新查询时按程序生效。在此之前,缓存的 SQL 服务器/MySQL 更新保留了它们以前的缺失行版本递增行为,缓存的清理保留了其先前的状况过滤器。强化的行为一旦所有成员作者使用这些定义,即适用于整个集群。插入的 SQL 服务器和 MySQL 保留原始行代换语句命令。

<a id="documentation"></a>

## 文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件
关于更全面的文件,请参见:
- [Microsoft Orleans 文档](https://dotnet.github.io/orleans/docs/)
- [集群集供应商](https://dotnet.github.io/orleans/docs/implementation/cluster-management/)
- [ADO.NET 数据库配置](https://dotnet.github.io/orleans/docs/host/configuration-guide/adonet-configuration/)

<a id="feedback--contributing"></a>

## 反馈和贡献
- 如果有任何问题或希望提供反馈,请 [GitHub上的开关](https://github.com/dotnet/orleans/issues)
- 加入我们社区 [不一致](https://aka.ms/orleans-discord)
- 跟随 [@msftorleans 地图上写着:](https://twitter.com/msftorleans) 推特账户发布 Orleans 通知
- 请审查我们的 [会费捐款准则](https://github.com/dotnet/orleans/blob/main/CONTRIBUTING.md)
- 该项目在 [麻省理工学院许可证](https://github.com/dotnet/orleans/blob/main/LICENSE)下获得许可证
