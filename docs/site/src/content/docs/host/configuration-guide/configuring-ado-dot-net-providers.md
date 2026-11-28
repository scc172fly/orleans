---
title: 配置 ADO.NET 提供程序
description: 使用 ADO.NET 配置 Orleans 集群、提醒器、存储和 Grain 目录。
ms.date: 08/10/2026
ms.topic: how-to
---

<a id="configure-adonet-providers"></a>

# 配置 ADO.NET 提供程序

Orleans ADO.NET 提供程序使用关系型数据库实现一种或多种运行时功能：

| 功能 | 包 | 配置位置 |
|---|---|---|
| 集群 | [`Microsoft.Orleans.Clustering.AdoNet`](https://www.nuget.org/packages/Microsoft.Orleans.Clustering.AdoNet) | Silo 和外部客户端 |
| Grain 存储 | [`Microsoft.Orleans.Persistence.AdoNet`](https://www.nuget.org/packages/Microsoft.Orleans.Persistence.AdoNet) | Silo |
| 提醒器 | [`Microsoft.Orleans.Reminders.AdoNet`](https://www.nuget.org/packages/Microsoft.Orleans.Reminders.AdoNet) | Silo |
| Grain 目录 | [`Microsoft.Orleans.GrainDirectory.AdoNet`](https://www.nuget.org/packages/Microsoft.Orleans.GrainDirectory.AdoNet) | Silo |
| 流 | [`Microsoft.Orleans.Streaming.AdoNet`](https://www.nuget.org/packages/Microsoft.Orleans.Streaming.AdoNet) | Silo 和外部客户端 |

仅安装应用所使用功能对应的包，并引用数据库驱动程序包。

<a id="prepare-the-database"></a>

## 准备数据库

首先运行对应数据库的主脚本，然后运行各功能的脚本。例如，SQL Server 集群及持久化需要：

1. `src/AdoNet/Shared/SQLServer-Main.sql`
2. `src/AdoNet/Orleans.Clustering.AdoNet/SQLServer-Clustering.sql`
3. `src/AdoNet/Orleans.Persistence.AdoNet/SQLServer-Persistence.sql`

有关架构脚本及提供程序固定名称的链接，请参见 [ADO.NET 数据库配置](adonet-configuration.md)。

将架构变更作为受控部署步骤执行。不要仅为使 Silo 在运行时创建表而向其授予架构所有者权限。

脚本使用数据库用户的默认架构创建各表及 `OrleansQuery` 表，而提供程序查询使用不带架构限定的名称引用这些对象。Orleans 不提供用于选择架构或文件组的选项。如果生产数据库需要专用架构、文件组、分区或其他存储布局，请在数据库部署过程中调整脚本，并保持生成的表名、列、参数及查询结果的结构与 Orleans 兼容。在应用启动流程之外应用并测试这些变更。

<a id="configure-sql-server"></a>

## 配置 SQL Server

对于 SQL Server，请使用 `Microsoft.Data.SqlClient`：

:::code language="csharp" source="../snippets/hosting/HostingExamples.cs" id="adonet_silo":::

使用相同的集群数据库配置外部客户端：

:::code language="csharp" source="../snippets/hosting/HostingExamples.cs" id="adonet_client":::

> [!IMPORTANT]
> `System.Data.SqlClient` 并不是此处的 SQL Server 提供程序固定名称。引用 `Microsoft.Data.SqlClient` 包并使用 `Microsoft.Data.SqlClient` 固定名称。

<a id="configure-another-database"></a>

## 配置其他数据库

对于 PostgreSQL、MySQL/MariaDB 和 Oracle，Orleans 的配置形式相同。请同时更改驱动程序包、提供程序固定名称、连接字符串和 SQL 脚本：

| 数据库 | 驱动程序包 | 提供程序固定名称 |
|---|---|---|
| SQL Server | `Microsoft.Data.SqlClient` | `Microsoft.Data.SqlClient` |
| PostgreSQL | `Npgsql` | `Npgsql` |
| MySQL/MariaDB | `MySql.Data` | `MySql.Data.MySqlClient` |
| Oracle | `Oracle.ManagedDataAccess.Core` | `Oracle.DataAccess.Client` |

Orleans 也支持 MySqlConnector 驱动程序使用的 `MySqlConnector` 固定名称。请确认所选功能具有适用于目标数据库的脚本。

<a id="use-a-data-source"></a>

## 使用数据源

每种 ADO.NET 提供程序选项类型均接受连接字符串或 <xref:System.Data.Common.DbDataSource>。当数据库驱动程序需要连接字符串无法表达的配置（例如定期刷新的身份验证令牌）时，使用数据源会很有帮助。

仅配置一种连接来源。如果同时提供 `ConnectionString` 和 `DataSource`，或两者均未提供，Orleans 都会拒绝该配置。仍须设置 `Invariant`，因为 Orleans 使用它来选择数据库专用的查询和行为。

将数据源注册为单例，并通过提供程序的 `OptionsBuilder` 配置重载解析它。命名 Orleans 提供程序可以解析不同的键控数据源。依赖注入容器或应用拥有数据源，并必须在 Orleans 提供程序的整个生存期内保持其可用；Orleans 会打开及释放各个连接，但不会释放数据源本身。

<a id="configure-declaratively"></a>

## 使用声明式配置

已安装的 ADO.NET 提供程序包会注册 `AdoNet`，以支持声明式配置。例如：

```json
{
  "Orleans": {
    "ServiceId": "orders",
    "ClusterId": "orders-production",
    "Clustering": {
      "ProviderType": "AdoNet",
      "Invariant": "Microsoft.Data.SqlClient",
      "ConnectionString": "..."
    },
    "Reminders": {
      "ProviderType": "AdoNet",
      "Invariant": "Microsoft.Data.SqlClient",
      "ConnectionString": "..."
    },
    "GrainStorage": {
      "Default": {
        "ProviderType": "AdoNet",
        "Invariant": "Microsoft.Data.SqlClient",
        "ConnectionString": "..."
      }
    }
  }
}
```

将连接字符串存储在机密提供程序或部署环境中，而不是提交到版本控制的设置文件中。

<a id="operational-guidance"></a>

## 运维指南

- 保持 <xref:Orleans.Configuration.ClusterOptions.ServiceId> 稳定，以便 Orleans 读取预期的应用数据行。
- 根据 Silo 和客户端进程的总数量确定连接池大小。
- 对连接加密，并使用最小权限的数据库标识。
- 监控数据库延迟、限流、死锁及连接池耗尽情况。
- 在负载下测试数据库故障转移和滚动部署行为。
- 根据应用恢复目标备份 Grain 状态；成员资格记录是临时数据，不能替代状态备份。

[!INCLUDE [managed-identities](../../../includes/managed-identities.md)]
