---
title: "配置 ADO.NET 提供者"
description: "配置 Orleans 集束、提醒、存储和 grain 目录,并配有 ADO.NET。"
ms.date: 08/10/2026
ms.topic: how-to
---

<a id="configure-adonet-providers"></a>

# 配置 ADO.NET 提供者

Orleans ADO.NET 供应商使用关系数据库来提供一个或多个运行时间能力:

| 能力 | 套包 | 配置配置 |
|---|---|---|
| 集群组合 | [`Microsoft.Orleans.Clustering.AdoNet`](https://www.nuget.org/packages/Microsoft.Orleans.Clustering.AdoNet) | Silos 和外部客户 |
| Grain 储存 | [`Microsoft.Orleans.Persistence.AdoNet`](https://www.nuget.org/packages/Microsoft.Orleans.Persistence.AdoNet) | Silos |
| 提醒提醒 | [`Microsoft.Orleans.Reminders.AdoNet`](https://www.nuget.org/packages/Microsoft.Orleans.Reminders.AdoNet) | Silos |
| Grain 目录 | [`Microsoft.Orleans.GrainDirectory.AdoNet`](https://www.nuget.org/packages/Microsoft.Orleans.GrainDirectory.AdoNet) | Silos |
| 串流 | [`Microsoft.Orleans.Streaming.AdoNet`](https://www.nuget.org/packages/Microsoft.Orleans.Streaming.AdoNet) | Silos 和外部客户 |

只安装应用程序所用能力的软件包。 另请参考数据库驱动程序包 。

<a id="prepare-the-database"></a>

## 准备数据库

首先运行数据库的主脚本, 之后是每个功能脚本。 例如, SQL 服务器组合和持久性需要:

1. `src/AdoNet/Shared/SQLServer-Main.sql`
2. `src/AdoNet/Orleans.Clustering.AdoNet/SQLServer-Clustering.sql`
3. `src/AdoNet/Orleans.Persistence.AdoNet/SQLServer-Persistence.sql`

方案脚本和变量链接见 [ADO.NET 数据库配置](adonet-configuration.md) 。

将机率变化应用为控制部署步骤。 不要仅仅为了在运行时创建表格而给予 silos 机型拥有者许可 。

脚本创建表格和 `OrleansQuery` 使用数据库用户
默认默认 schma , 提供方的查询以无限制方式引用这些对象
名称 Orleans 没有提供选择 schema 或文件组的选项。 如果
您的生产数据库需要专门的系统、文件组、分区、
或其它存储布局,将脚本作为数据库的一部分加以调整
设置,并保留相应的表格名称、列、参数和查询
与 Orleans兼容的形状。应用和测试
应用程序启动路径。

<a id="configure-sql-server"></a>

## 配置 SQL 服务器

使用 `Microsoft.Data.SqlClient` 用于 SQL 服务器 :

:::code language="csharp" source="../snippets/hosting/HostingExamples.cs" id="adonet_silo":::

配置一个使用相同组群数据库的外部客户端:

:::code language="csharp" source="../snippets/hosting/HostingExamples.cs" id="adonet_client":::

> [!IMPORTANT]
> `System.Data.SqlClient` 不是 SQL 服务器变量。 请参考 `Microsoft.Data.SqlClient` 软件包, 并使用 `Microsoft.Data.SqlClient` 变量。

<a id="configure-another-database"></a>

## 配置另一个数据库

Orleans 配置形状对 PostgreSQL、 MySQL/MariaDB和甲骨文相同。将驱动程序包、变量、连接字符串和 SQL 脚本一起修改:

| 数据库数据库 | 司机软件包 | 变量 |
|---|---|---|
| SQL 服务器 | `Microsoft.Data.SqlClient` | `Microsoft.Data.SqlClient` |
| PostgreSQL | `Npgsql` | `Npgsql` |
| MySQL/MariaDB | `MySql.Data` | `MySql.Data.MySqlClient` |
| 甲骨甲 | `Oracle.ManagedDataAccess.Core` | `Oracle.DataAccess.Client` |

Orleans 还确认 `MySqlConnector` 排排 MySqlConnector 驱动程序。验证选定能力是否为选定的数据库有脚本。

<a id="use-a-data-source"></a>

## 使用数据源

每个 ADO.NET 提供者选项类型都接受连接字符串或 <xref:System.Data.Common.DbDataSource>。当数据库驱动程序需要无法在连接字符串中代表的配置时,数据源是有用的,例如定期刷新验证符。

配置一个完全连接源。 Orleans 拒绝提供 `ConnectionString` 和 `DataSource`或两者均不提供的配置。 继续设置 `Invariant` , 因为 Orleans 使用它选择特定数据库的查询和行为 。

将数据源登记为单吨,并通过提供者的 `OptionsBuilder` 配置超载来解决这个问题。命名的 Orleans 供应商可以解决不同的关键数据源。依赖性注射容器或应用程序拥有数据源,必须在 Orleans 提供者的寿命期间保持数据源; Orleans 打开并处理个人连接,但不处置数据源。

<a id="configure-declaratively"></a>

## 公开配置

已安装 ADO.NET 供应商软件包注册器 `AdoNet` 用于声明配置。 例如:

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

将连接字符串存储在秘密提供者或部署环境中, 而不是保存在已承诺设置文件中 。

<a id="operational-guidance"></a>

## 业务指导

- 保持 <xref:Orleans.Configuration.ClusterOptions.ServiceId> 稳定,这样 Orleans 读取预期应用行。
- 总共 silo 和客户程序所需的连接集合大小。
- 加密连接和使用最不重要的数据库身份。
- 监测数据库的延迟、振动、僵局和池塘耗竭。
- 测试数据库失败和滚动部署行为正在负荷中。
- 根据应用程序回收目标备份 grain 状态;成员行是短暂的,不替换州备份。

[!INCLUDE [managed-identities](../../../includes/managed-identities.md)]
