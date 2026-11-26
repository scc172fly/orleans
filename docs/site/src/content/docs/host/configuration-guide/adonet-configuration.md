---
title: "ADO.NET 数据库配置"
description: "查找 Orleans ADO.NET 样稿脚本和提供者变量。"
ms.date: 10/02/2026
ms.topic: reference
---

<a id="adonet-database-configuration"></a>

# ADO.NET 数据库配置

Orleans 将 ADO.NET schema 脚本保存在每个提供者源代码旁边。 在能力脚本之前运行主脚本。 使用应用程序所安装的软件包的同一种 Orleans 释放的脚本。

<a id="driver-invariants"></a>

## 驱动变量

| 数据库数据库 | 司机软件包 | Orleans 变量 |
|---|---|---|
| SQL 服务器 | [Microsoft.Data.SqlClient](https://www.nuget.org/packages/Microsoft.Data.SqlClient/) | `Microsoft.Data.SqlClient` |
| PostgreSQL | [Npgsql 数字](https://www.nuget.org/packages/Npgsql/) | `Npgsql` |
| MySQL/MariaDB | [MySql.Data](https://www.nuget.org/packages/MySql.Data/) | `MySql.Data.MySqlClient` |
| 甲骨甲 | [Oracle.ManagedDataAccess.Core](https://www.nuget.org/packages/Oracle.ManagedDataAccess.Core/) | `Oracle.DataAccess.Client` |

> [!IMPORTANT]
> 使用 `Microsoft.Data.SqlClient`,而不是 `System.Data.SqlClient`,用于 SQL 服务器

<a id="main-scripts"></a>

## 主要脚本

- [SQL 服务器](https://github.com/dotnet/orleans/blob/main/src/AdoNet/Shared/SQLServer-Main.sql)
- [PostgreSQL](https://github.com/dotnet/orleans/blob/main/src/AdoNet/Shared/PostgreSQL-Main.sql)
- [MySQL/MariaDB](https://github.com/dotnet/orleans/blob/main/src/AdoNet/Shared/MySQL-Main.sql)
- [甲骨甲](https://github.com/dotnet/orleans/blob/main/src/AdoNet/Shared/Oracle-Main.sql)
- 支持的当地持久性假设情景[SQLite 标准](https://github.com/dotnet/orleans/blob/main/src/AdoNet/Shared/Sqlite-Main.sql)

<a id="clustering"></a>

## 集群组合

- [SQL 服务器](https://github.com/dotnet/orleans/blob/main/src/AdoNet/Orleans.Clustering.AdoNet/SQLServer-Clustering.sql)
- [PostgreSQL](https://github.com/dotnet/orleans/blob/main/src/AdoNet/Orleans.Clustering.AdoNet/PostgreSQL-Clustering.sql)
- [MySQL/MariaDB](https://github.com/dotnet/orleans/blob/main/src/AdoNet/Orleans.Clustering.AdoNet/MySQL-Clustering.sql)
- [甲骨甲](https://github.com/dotnet/orleans/blob/main/src/AdoNet/Orleans.Clustering.AdoNet/Oracle-Clustering.sql)

<a id="persistence"></a>

## 持久性

- [SQL 服务器](https://github.com/dotnet/orleans/blob/main/src/AdoNet/Orleans.Persistence.AdoNet/SQLServer-Persistence.sql)
- [PostgreSQL](https://github.com/dotnet/orleans/blob/main/src/AdoNet/Orleans.Persistence.AdoNet/PostgreSQL-Persistence.sql)
- [MySQL/MariaDB](https://github.com/dotnet/orleans/blob/main/src/AdoNet/Orleans.Persistence.AdoNet/MySQL-Persistence.sql)
- [甲骨甲](https://github.com/dotnet/orleans/blob/main/src/AdoNet/Orleans.Persistence.AdoNet/Oracle-Persistence.sql)
- [SQLite 标准](https://github.com/dotnet/orleans/blob/main/src/AdoNet/Orleans.Persistence.AdoNet/Sqlite-Persistence.sql)

SQLite 脚本是一等的。 重新应用它们可以刷新持久性查询
保存 grain 状态。

<a id="reminders"></a>

## 提醒提醒

- [SQL 服务器](https://github.com/dotnet/orleans/blob/main/src/AdoNet/Orleans.Reminders.AdoNet/SQLServer-Reminders.sql)
- [PostgreSQL](https://github.com/dotnet/orleans/blob/main/src/AdoNet/Orleans.Reminders.AdoNet/PostgreSQL-Reminders.sql)
- [MySQL/MariaDB](https://github.com/dotnet/orleans/blob/main/src/AdoNet/Orleans.Reminders.AdoNet/MySQL-Reminders.sql)
- [甲骨甲](https://github.com/dotnet/orleans/blob/main/src/AdoNet/Orleans.Reminders.AdoNet/Oracle-Reminders.sql)

<a id="grain-directory-scripts"></a>

## Grain 目录脚本

- [SQL 服务器](https://github.com/dotnet/orleans/blob/main/src/AdoNet/Orleans.GrainDirectory.AdoNet/SQLServer-GrainDirectory.sql)
- [PostgreSQL](https://github.com/dotnet/orleans/blob/main/src/AdoNet/Orleans.GrainDirectory.AdoNet/PostgreSQL-GrainDirectory.sql)
- [MySQL/MariaDB](https://github.com/dotnet/orleans/blob/main/src/AdoNet/Orleans.GrainDirectory.AdoNet/MySQL-GrainDirectory.sql)

并非所有能力都支持每个数据库。在提供者目录中存在脚本是发布 Orleans 的权威性支持信号。

<a id="apply-and-upgrade-schemas"></a>

## 应用和升级改造方案

1. 根据数据库回收政策备份应用程序数据。
2. 应用主脚本到新数据库中。
3. 应用每个配置的 Orleans 能力脚本。
4. 在从旧的系统图中升级时,审查并应用提供者 `Migrations` 目录下的脚本。
5. 使用相同的驱动器和数据库引擎版本, 以中继集校验 。

<a id="production-database-layout"></a>

## 生产数据库布局

提供的脚本是一种开始的预案,不是普遍制作
数据库数据库的布局。 在数据库用户
默认 schema 默认 schma, 而 ADO.NET 提供商不会暴露 schema 或 file group
配置。生产部署可选择专门的计划,
文档组、分区或其他数据库专用存储功能,但
在数据库部署和保持过程中必须落实这些选择
查询中存储的查询 `OrleansQuery`保存表格名称,
提供商期待的和验证的结果形状
推出前,先用集成组别定制脚本和查询。

主机配置见 [配置 ADO.NET 提供者](configuring-ado-dot-net-providers.md) 。
