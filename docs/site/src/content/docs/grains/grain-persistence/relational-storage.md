---
title: "ADO.NET grain 持久性"
description: "配置关系数据库,包括SQLite,用于 Orleans grain 持久性。"
ms.date: 08/08/2026
ms.topic: how-to
---

<a id="adonet-grain-persistence"></a>

# ADO.NET grain 持久性

[`Microsoft.Orleans.Persistence.AdoNet`](https://www.nuget.org/packages/Microsoft.Orleans.Persistence.AdoNet) 软件包存储状态 grain ,使用通过 ADO.NET接触的数据库特定查询。 Orleans 包括持久性脚本,用于:

| 数据库数据库 | 司机软件包 | `Invariant` | 脚本 |
|---|---|---|---|
| SQL 服务器 | `Microsoft.Data.SqlClient` | `Microsoft.Data.SqlClient` | `SQLServer-Persistence.sql` |
| MySQL 或 MariaDB | `MySql.Data` 或 `MySqlConnector` | Driver-specific | `MySQL-Persistence.sql` |
| PostgreSQL | `Npgsql` | `Npgsql` | `PostgreSQL-Persistence.sql` |
| 甲骨甲 | `Oracle.ManagedDataAccess.Core` | `Oracle.DataAccess.Client` | `Oracle-Persistence.sql` |
| SQLite 标准 | `Microsoft.Data.Sqlite` | `System.Data.SQLite` | `Sqlite-Persistence.sql` |

变量是 Orleans 提供者标识符, 且不总是匹配驱动程序软件包名称。 请安装驱动程序套件, 并为选中的数据库运行共享的 `*-Main.sql` 脚本和 `*-Persistence.sql` 脚本 。

<a id="configure-a-provider"></a>

## 配置提供者

:::code language="csharp" source="../../snippets/compiled/Grains/PersistenceSnippets.cs" id="configure_ado_net_storage":::
对于 SQLite :

:::code language="csharp" source="../../snippets/compiled/Grains/PersistenceSnippets.cs" id="configure_sqlite_storage":::
SQLite 可用于本地的单一进程情景。 它的文件锁定、 部署地貌和可用性特性通常不适应多silo 生产组群 。

<a id="serialization-and-schema-evolution"></a>

## 序列化和系统演变

配置配置 <xref:Orleans.Configuration.AdoNetGrainStorageOptions.GrainStorageSerializer> 默认时间 JSON 代表不符合申请要求:

:::code language="csharp" source="./snippets/persistence/StorageConfiguration.cs" id="configure_adonet_serializer":::

更改序列器不是数据库迁移。 新序列器必须读取现有的有效载荷, 否则应用程序必须分别迁移 。

<a id="concurrency-contract"></a>

## 折货币合同

ADO.NET 持久性脚本执行 Orleans记录级的乐观和货币。数据库版本以ETag的形式被应用代码所暴露。 将预期版本进行比较,并因不匹配而失败。

提供方询问必须保存 Orleans所期望的参数名、结果名称和类型。 持久性写作运行在数据库交易中, 失败时必须倒回。 此交易覆盖一个 grain- State record; 它不会将多个 <xref:Orleans.Runtime.IPersistentState`1> 原案写入文字 。

<a id="customize-queries"></a>

## 自定义查询

下 `OrleansQuery` 管理人可在保存报表的同时,对这些报表进行调音。 Orleans 查询合同. 将定制脚本置于源控制之下,通过正常数据库部署程序应用这些脚本——例如使用 [数据级应用(DACPAC)](https://learn.microsoft.com/sql/tools/sql-database-projects/concepts/data-tier-applications/overview)测试读、写、清、第一手的种族,

<a id="operational-query-shape"></a>

### 操作查询形状

供应商提供共同身份参数 `GrainIdHash`、 `GrainIdN0`、 `GrainIdN1`、 `GrainTypeHash`、 `GrainTypeString`、 `GrainIdExtensionString`和 `ServiceId`。 `WriteToStorageKey` 还接收 `GrainStateVersion` 和 `PayloadBinary`; `ClearStorageKey` 和任择 `DeleteStorageKey` 接收 `GrainStateVersion`。

`ReadFromStorageKey` 必须返回名为 `PayloadBinary` 和 `Version`的列。 `WriteToStorageKey` 必须将结果版本还给 `NewGrainStateVersion`。清除并删除查询返回第一个结果栏中的一版值; 其名称被忽略。 成功操作返回所有结果集中完全一行, 包含高级版本。 一个未变或缺失的价值信号表示ETag冲突。 版本必须作为签名32位整数来代表 。

每个提供机构实例都写道 `OrleansQuery` 期间 silo 启动启动后不会在之后进行民意调查。 因此更改表格只影响新初始化的提供者。 滚动 silo 新的新问题和计划可以共存时, 重新开机是合适的; 如果无法的话, 协调断电或分阶段迁移。

具体数据库的定制可使用 [配有分隔表和索引](https://learn.microsoft.com/sql/relational-databases/partitions/partitioned-tables-and-indexes)、 [内存优化表格](https://learn.microsoft.com/sql/relational-databases/in-memory-oltp/overview-and-usage-scenarios)、 [本地汇编模块](https://learn.microsoft.com/sql/relational-databases/in-memory-oltp/native-compilation-of-tables-and-stored-procedures)、 [PolyBase](https://learn.microsoft.com/sql/relational-databases/polybase/overview)或 [连接的服务器](https://learn.microsoft.com/sql/relational-databases/linked-servers/linked-servers-database-engine) 等功能,如果这些功能适合部署。
