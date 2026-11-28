---
title: ADO.NET Grain 持久化
description: 配置关系数据库（包括 SQLite）以实现 Orleans Grain 持久化。
ms.date: 08/08/2026
ms.topic: how-to
---

<a id="adonet-grain-persistence"></a>
# ADO.NET Grain 持久化

[`Microsoft.Orleans.Persistence.AdoNet`](https://www.nuget.org/packages/Microsoft.Orleans.Persistence.AdoNet) 包使用通过 ADO.NET 公开的数据库专用查询来存储 Grain 状态。Orleans 提供以下数据库的持久化脚本：

| 数据库 | 驱动程序包 | `Invariant` | 脚本 |
|---|---|---|---|
| SQL Server | `Microsoft.Data.SqlClient` | `Microsoft.Data.SqlClient` | `SQLServer-Persistence.sql` |
| MySQL 或 MariaDB | `MySql.Data` 或 `MySqlConnector` | 取决于驱动程序 | `MySQL-Persistence.sql` |
| PostgreSQL | `Npgsql` | `Npgsql` | `PostgreSQL-Persistence.sql` |
| Oracle | `Oracle.ManagedDataAccess.Core` | `Oracle.DataAccess.Client` | `Oracle-Persistence.sql` |
| SQLite | `Microsoft.Data.Sqlite` | `System.Data.SQLite` | `Sqlite-Persistence.sql` |

固定名称（invariant name）是 Orleans 提供程序标识符，不一定与驱动程序包名称相同。安装驱动程序包，并针对所选数据库同时运行共享的 `*-Main.sql` 脚本及 `*-Persistence.sql` 脚本。

<a id="configure-a-provider"></a>
## 配置提供程序

:::code language="csharp" source="../../snippets/compiled/Grains/PersistenceSnippets.cs" id="configure_ado_net_storage":::
对于 SQLite：

:::code language="csharp" source="../../snippets/compiled/Grains/PersistenceSnippets.cs" id="configure_sqlite_storage":::
SQLite 适合本地单进程场景。其文件锁定、部署拓扑及可用性特征通常不适合包含多个 Silo 的生产集群。

<a id="serialization-and-schema-evolution"></a>
## 序列化与架构演进

当默认 JSON 表示无法满足应用程序要求时，配置 <xref:Orleans.Configuration.AdoNetGrainStorageOptions.GrainStorageSerializer>：

:::code language="csharp" source="./snippets/persistence/StorageConfiguration.cs" id="configure_adonet_serializer":::

更改序列化器并不是数据库迁移。新序列化器必须能够读取现有有效载荷，或者由应用程序单独进行迁移。

<a id="concurrency-contract"></a>
## 并发契约

ADO.NET 持久化脚本实现 Orleans 记录级乐观并发控制。数据库版本以 ETag 的形式向应用程序代码公开。写入和清除操作会比较预期版本，如果版本不匹配则失败。

提供程序查询必须保留 Orleans 所期望的参数名称、结果名称及类型。持久化写入在数据库事务中运行，失败时必须回滚。此事务仅涵盖一条 Grain 状态记录，不会使对多个 <xref:Orleans.Runtime.IPersistentState`1> 实例的写入具备原子性。

<a id="customize-queries"></a>
## 自定义查询

`OrleansQuery` 表包含提供程序使用的数据库供应商专用语句。管理员可以优化这些语句，但必须保留 Orleans 查询契约。应将自定义脚本纳入源代码管理，通过常规数据库部署流程应用脚本（例如使用[数据层应用程序（DACPAC）](https://learn.microsoft.com/sql/tools/sql-database-projects/concepts/data-tier-applications/overview)），并在每次更改后测试读取、写入、清除、首次写入竞态及 ETag 冲突。

<a id="operational-query-shape"></a>
### 操作查询的结构

提供程序传入通用标识参数 `GrainIdHash`、`GrainIdN0`、`GrainIdN1`、`GrainTypeHash`、`GrainTypeString`、`GrainIdExtensionString` 和 `ServiceId`。`WriteToStorageKey` 还接收 `GrainStateVersion` 和 `PayloadBinary`；`ClearStorageKey` 及可选的 `DeleteStorageKey` 接收 `GrainStateVersion`。

`ReadFromStorageKey` 必须返回名为 `PayloadBinary` 和 `Version` 的列。`WriteToStorageKey` 必须以 `NewGrainStateVersion` 返回操作后的版本。清除及删除查询在第一个结果列中返回一个版本值；该列名称会被忽略。成功操作在所有结果集中合计恰好返回一行，其中包含递增后的版本。值未改变或缺失则表示 ETag 冲突。版本必须能以有符号 32 位整数表示。

每个提供程序实例会在 Silo 启动期间读取 `OrleansQuery`，此后不会轮询该表。因此，更改该表仅影响新初始化的提供程序。当新旧查询及架构能够共存时，可使用 Silo 滚动重启；不能共存时，则需协调停机或分阶段迁移。

如果部署适用相应功能，针对数据库的自定义可以使用[分区表及索引](https://learn.microsoft.com/sql/relational-databases/partitions/partitioned-tables-and-indexes)、[内存优化表](https://learn.microsoft.com/sql/relational-databases/in-memory-oltp/overview-and-usage-scenarios)、[本机编译模块](https://learn.microsoft.com/sql/relational-databases/in-memory-oltp/native-compilation-of-tables-and-stored-procedures)、[PolyBase](https://learn.microsoft.com/sql/relational-databases/polybase/overview) 或[链接服务器](https://learn.microsoft.com/sql/relational-databases/linked-servers/linked-servers-database-engine)等特性。
