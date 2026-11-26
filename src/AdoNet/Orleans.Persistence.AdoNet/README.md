<a id="microsoft-orleans-persistence-for-adonet"></a>

# Microsoft Orleans 持久性 ADO.NET

<a id="introduction"></a>

## 简介
Microsoft Orleans 持久性 ADO.NET 提供 grain 持久性或持久性 Microsoft Orleans 使用关系数据库 ADO.NET。此提供者允许您 grains 各种关系数据库中,包括 SQL 服务器 MySQL, PostgreSQL和甲骨文。

<a id="getting-started"></a>

## 入门
要使用此软件包, 请通过 NuGet安装 :

```shell
dotnet add package Microsoft.Orleans.Persistence.AdoNet
```

您还需要为您的数据库系统安装合适的数据库驱动程序包 :

- SQL 服务器: `Microsoft.Data.SqlClient`
- MySQL: `MySql.Data` 或 `MySqlConnector`
- PostgreSQL: `Npgsql`
- 甲骨文: `Oracle.ManagedDataAccess.Core`
- SQLite: `Microsoft.Data.Sqlite`

<a id="example---configuring-adonet-persistence"></a>

## 示例 - 配置 ADO.NET 持久性

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

## 实例 - 使用 Grain 储存在 Grain

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

在使用 ADO.NET 提供者之前,您需要先设置必要的数据库表格。不同的数据库系统的脚本可在 Orleans 源存储库中查到:

- [SQL 服务器脚本](https://github.com/dotnet/orleans/tree/main/src/AdoNet/Orleans.Persistence.AdoNet/SQLServer-Persistence.sql)
- [MySQL 脚本](https://github.com/dotnet/orleans/tree/main/src/AdoNet/Orleans.Persistence.AdoNet/MySQL-Persistence.sql)
- [PostgreSQL 脚本](https://github.com/dotnet/orleans/tree/main/src/AdoNet/Orleans.Persistence.AdoNet/PostgreSQL-Persistence.sql)
- [甲骨文脚本](https://github.com/dotnet/orleans/tree/main/src/AdoNet/Orleans.Persistence.AdoNet/Oracle-Persistence.sql)
- [SQLite 主脚本](https://github.com/dotnet/orleans/blob/main/src/AdoNet/Shared/Sqlite-Main.sql)
- [SQLite 持久性脚本](https://github.com/dotnet/orleans/blob/main/src/AdoNet/Orleans.Persistence.AdoNet/Sqlite-Persistence.sql)

支持设置和可重复升级的持久性脚本:重新应用
当前脚本在保存时刷新持久性查询和常规
grain 状态。对于新数据库,先运行共享的主脚本
数据库可以创建查询目录,然后运行其持久性脚本。

Grain 国家版本周期从1至2147483647年。
比较了AETGs的
平等。在2147483647年同行的突变之后,ETag重复出现。
在整个整个周期保留ETag的作家可以与当前版本匹配
并覆盖更新的状态。

升级现有数据库,停止所有 silos 使用该数据库并重新应用当前
数据库的持久性脚本。 MySQL 重建其程序, 因此重新应用
其后任何例行 `EXECUTE` 赠款。甲骨和SQLite也实现正常化
保存有效载荷时,其整数大于最大值的版本。
应用脚本后重新启动 silos ,使每个提供者都装载更新的
查询。包装的版本符合旧 silos所用的整数ETags,因此
数据库更新后,可进行二进制滚动升级。

<a id="storage-key-format"></a>

## 存储键格式

`AdoNetGrainStorageOptions.KeyFormat` 控制 grain 国家地图如何行。
用于新数据库,使用 `GrainTypeQualified`,它提供了
grain 类型、国名和 grain 键本身的行:

```csharp
options.KeyFormat = AdoNetGrainStorageKeyFormat.GrainTypeQualified;
```

使用 `GrainTypeQualified`、 grain 键和 grain 类型及国名合并,
每个小节限在255个UTF-8字节之间;
`ArgumentException`.

默认值 `Legacy`,以先前使用的格式读写状态
以这种格式, grains 不同类型、具有相同钥匙和状态的不同
已经持有状态数据库的数据库保持 `Legacy`,因为
以一种格式写成的状态,另一个格式无法看到。 `GrainTypeQualified`
无法与 `UseOrleans3CompatibleHasher()`合并。

<a id="documentation"></a>

## 文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件
关于更全面的文件,请参见:
- [Microsoft Orleans 文档](https://dotnet.github.io/orleans/docs/)
- [Grain 持久性](https://dotnet.github.io/orleans/docs/grains/grain-persistence/)
- [关系数据库持久性](https://dotnet.github.io/orleans/docs/grains/grain-persistence/relational-storage/)

<a id="feedback--contributing"></a>

## 反馈和贡献
- 如果有任何问题或希望提供反馈,请 [GitHub上的开关](https://github.com/dotnet/orleans/issues)
- 加入我们社区 [不一致](https://aka.ms/orleans-discord)
- 跟随 [@msftorleans 地图上写着:](https://twitter.com/msftorleans) 推特账户发布 Orleans 通知
- 请审查我们的 [会费捐款准则](https://github.com/dotnet/orleans/blob/main/CONTRIBUTING.md)
- 该项目在 [麻省理工学院许可证](https://github.com/dotnet/orleans/blob/main/LICENSE)下获得许可证
