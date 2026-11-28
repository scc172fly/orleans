<a id="microsoft-orleans-persistence-for-adonet"></a>

# Microsoft Orleans ADO.NET 持久化提供程序

<a id="introduction"></a>

## 简介
Microsoft Orleans ADO.NET 持久化提供程序通过 ADO.NET 使用关系数据库为 Microsoft Orleans 提供 Grain 持久化功能，使 Grain 能够将状态持久化到 SQL Server、MySQL、PostgreSQL 和 Oracle 等多种关系数据库。

<a id="getting-started"></a>

## 入门
要使用此包，请通过 NuGet 安装：

```shell
dotnet add package Microsoft.Orleans.Persistence.AdoNet
```

还需要为所用数据库系统安装相应的数据库驱动程序包：

- SQL Server: `Microsoft.Data.SqlClient`
- MySQL: `MySql.Data` 或 `MySqlConnector`
- PostgreSQL: `Npgsql`
- Oracle: `Oracle.ManagedDataAccess.Core`
- SQLite: `Microsoft.Data.Sqlite`

<a id="example---configuring-adonet-persistence"></a>

## 示例 - 配置 ADO.NET 持久化

```csharp
using Microsoft.Extensions.Hosting;
using Orleans.Configuration;
using Orleans.Hosting;

var builder = Host.CreateApplicationBuilder(args)
    .UseOrleans(siloBuilder =>
    {
        siloBuilder
            .UseLocalhostClustering()
            // Configure ADO.NET as grain storage
            .AddAdoNetGrainStorage(
                name: "AdoNetStore",
                configureOptions: options =>
                {
                    options.Invariant = "Microsoft.Data.SqlClient";  // Or other providers like "MySql.Data.MySqlClient", "Npgsql", etc.
                    options.ConnectionString = "Server=localhost;Database=OrleansStorage;User Id=myUsername;******;";
                    // Optional: Configure custom queries
                });
    });

// Run the host
await builder.RunAsync();
```

<a id="example---using-grain-storage-in-a-grain"></a>

## 示例 - 在 Grain 中使用 Grain 存储

```csharp
using System;
using System.Threading.Tasks;
using Orleans;
using Orleans.Runtime;

// Define grain state class
public class MyGrainState
{
    public string Data { get; set; }
    public int Version { get; set; }
}

// Grain implementation that uses the ADO.NET storage
public class MyGrain : Grain, IMyGrain, IGrainWithStringKey
{
    private readonly IPersistentState<MyGrainState> _state;

    public MyGrain([PersistentState("state", "AdoNetStore")] IPersistentState<MyGrainState> state)
    {
        _state = state;
    }

    public async Task SetData(string data)
    {
        _state.State.Data = data;
        _state.State.Version++;
        await _state.WriteStateAsync();
    }

    public Task<string> GetData()
    {
        return Task.FromResult(_state.State.Data);
    }
}
```

<a id="database-setup"></a>

## 数据库设置

使用 ADO.NET 提供程序之前，需要创建所需的数据库表。Orleans 源码仓库提供了不同数据库系统的脚本：

- [SQL Server 脚本](https://github.com/dotnet/orleans/tree/main/src/AdoNet/Orleans.Persistence.AdoNet/SQLServer-Persistence.sql)
- [MySQL 脚本](https://github.com/dotnet/orleans/tree/main/src/AdoNet/Orleans.Persistence.AdoNet/MySQL-Persistence.sql)
- [PostgreSQL 脚本](https://github.com/dotnet/orleans/tree/main/src/AdoNet/Orleans.Persistence.AdoNet/PostgreSQL-Persistence.sql)
- [Oracle 脚本](https://github.com/dotnet/orleans/tree/main/src/AdoNet/Orleans.Persistence.AdoNet/Oracle-Persistence.sql)
- [SQLite 主脚本](https://github.com/dotnet/orleans/blob/main/src/AdoNet/Shared/Sqlite-Main.sql)
- [SQLite 持久化脚本](https://github.com/dotnet/orleans/blob/main/src/AdoNet/Orleans.Persistence.AdoNet/Sqlite-Persistence.sql)

持久化脚本支持初始设置和可重复执行的升级：重新执行
当前脚本会更新持久化查询及数据库例程，同时保留
Grain 状态。对于新数据库，请先运行对应数据库的共享 Main 脚本，
创建查询目录，然后运行其持久化脚本。

Grain 状态版本在 1 到 2147483647 之间循环。每次成功写入或
清除都会推进版本，乐观并发控制通过比较 ETag 是否
相等来实现。同一行经历 2147483647 次变更后，ETag 会重复。
如果写入者在整个循环期间一直保留同一个 ETag，它就可能与当前版本匹配，
并覆盖较新的状态。

升级现有数据库时，请停止所有使用该数据库的 Silo，重新执行当前
对应数据库的持久化脚本。MySQL 会重新创建存储过程，因此之后请重新授予
各例程专用的 `EXECUTE` 权限。Oracle 和 SQLite 还会对超过整数上限的版本
进行规范化，同时保留其数据内容。
执行脚本后重启 Silo，使每个提供程序加载更新后的
查询。循环回绕的版本仍符合旧版 Silo 使用的整数 ETag 格式，因此
完成此数据库更新后即可进行二进制程序的滚动升级。

<a id="storage-key-format"></a>

## 存储键格式

`AdoNetGrainStorageOptions.KeyFormat` 控制 Grain 状态如何映射到数据库行。
对于新数据库，请使用 `GrainTypeQualified`，它为每个
Grain 类型、状态名称和 Grain 键的组合分别分配一行：

```csharp
options.KeyFormat = AdoNetGrainStorageKeyFormat.GrainTypeQualified;
```

使用 `GrainTypeQualified` 时，Grain 键以及 Grain 类型与状态名称的组合
分别限制为约 255 个 UTF-8 字节；超出限制将引发
`ArgumentException`。

默认值 `Legacy` 采用早期版本的格式读写状态。
该格式下，键和状态名称相同的不同类型 Grain
共享同一行。已有状态的数据库应继续使用 `Legacy`，因为
采用一种格式写入的状态无法通过另一种格式访问。`GrainTypeQualified`
不能与 `UseOrleans3CompatibleHasher()` 一起使用。

<a id="documentation"></a>

## 文档
有关更全面的文档，请参阅：
- [Microsoft Orleans 文档](https://dotnet.github.io/orleans/docs/)
- [Grain 持久化](https://dotnet.github.io/orleans/docs/grains/grain-persistence/)
- [关系数据库持久化](https://dotnet.github.io/orleans/docs/grains/grain-persistence/relational-storage/)

<a id="feedback--contributing"></a>

## 反馈与贡献
- 如果遇到问题或希望提供反馈，请[在 GitHub 上创建 Issue](https://github.com/dotnet/orleans/issues)
- 加入我们的 [Discord](https://aka.ms/orleans-discord) 社区
- 关注 Twitter 上的 [@msftorleans](https://twitter.com/msftorleans) 账号，获取 Orleans 公告
- 欢迎贡献！请阅读我们的[贡献指南](https://github.com/dotnet/orleans/blob/main/CONTRIBUTING.md)
- 本项目采用 [MIT 许可证](https://github.com/dotnet/orleans/blob/main/LICENSE)
