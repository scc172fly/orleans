---
title: ADO.NET 数据库配置
description: 查找 Orleans ADO.NET 数据库架构脚本及提供程序固定名称。
ms.date: 10/02/2026
ms.topic: reference
---

<a id="adonet-database-configuration"></a>
# ADO.NET 数据库配置

Orleans 将 ADO.NET 数据库架构脚本与各个提供程序的源代码放在一起。先运行主脚本，再运行各功能脚本。使用与应用程序部署的包属于同一 Orleans 版本的脚本。

<a id="driver-invariants"></a>
## 驱动程序固定名称

| 数据库 | 驱动程序包 | Orleans 固定名称（invariant name） |
|---|---|---|
| SQL Server | [Microsoft.Data.SqlClient](https://www.nuget.org/packages/Microsoft.Data.SqlClient/) | `Microsoft.Data.SqlClient` |
| PostgreSQL | [Npgsql](https://www.nuget.org/packages/Npgsql/) | `Npgsql` |
| MySQL/MariaDB | [MySql.Data](https://www.nuget.org/packages/MySql.Data/) | `MySql.Data.MySqlClient` |
| Oracle | [Oracle.ManagedDataAccess.Core](https://www.nuget.org/packages/Oracle.ManagedDataAccess.Core/) | `Oracle.DataAccess.Client` |

> [!IMPORTANT]
> 对于 SQL Server，请使用 `Microsoft.Data.SqlClient`，而非 `System.Data.SqlClient`。

<a id="main-scripts"></a>
## 主脚本

- [SQL Server](https://github.com/dotnet/orleans/blob/main/src/AdoNet/Shared/SQLServer-Main.sql)
- [PostgreSQL](https://github.com/dotnet/orleans/blob/main/src/AdoNet/Shared/PostgreSQL-Main.sql)
- [MySQL/MariaDB](https://github.com/dotnet/orleans/blob/main/src/AdoNet/Shared/MySQL-Main.sql)
- [Oracle](https://github.com/dotnet/orleans/blob/main/src/AdoNet/Shared/Oracle-Main.sql)
- [SQLite](https://github.com/dotnet/orleans/blob/main/src/AdoNet/Shared/Sqlite-Main.sql)，用于受支持的本地持久化场景

<a id="clustering"></a>
## 集群成员管理

- [SQL Server](https://github.com/dotnet/orleans/blob/main/src/AdoNet/Orleans.Clustering.AdoNet/SQLServer-Clustering.sql)
- [PostgreSQL](https://github.com/dotnet/orleans/blob/main/src/AdoNet/Orleans.Clustering.AdoNet/PostgreSQL-Clustering.sql)
- [MySQL/MariaDB](https://github.com/dotnet/orleans/blob/main/src/AdoNet/Orleans.Clustering.AdoNet/MySQL-Clustering.sql)
- [Oracle](https://github.com/dotnet/orleans/blob/main/src/AdoNet/Orleans.Clustering.AdoNet/Oracle-Clustering.sql)

<a id="persistence"></a>
## 持久化

- [SQL Server](https://github.com/dotnet/orleans/blob/main/src/AdoNet/Orleans.Persistence.AdoNet/SQLServer-Persistence.sql)
- [PostgreSQL](https://github.com/dotnet/orleans/blob/main/src/AdoNet/Orleans.Persistence.AdoNet/PostgreSQL-Persistence.sql)
- [MySQL/MariaDB](https://github.com/dotnet/orleans/blob/main/src/AdoNet/Orleans.Persistence.AdoNet/MySQL-Persistence.sql)
- [Oracle](https://github.com/dotnet/orleans/blob/main/src/AdoNet/Orleans.Persistence.AdoNet/Oracle-Persistence.sql)
- [SQLite](https://github.com/dotnet/orleans/blob/main/src/AdoNet/Orleans.Persistence.AdoNet/Sqlite-Persistence.sql)

SQLite 脚本具有幂等性。重复应用脚本会刷新持久化查询，
同时保留 Grain 状态。

<a id="reminders"></a>
## 提醒器

- [SQL Server](https://github.com/dotnet/orleans/blob/main/src/AdoNet/Orleans.Reminders.AdoNet/SQLServer-Reminders.sql)
- [PostgreSQL](https://github.com/dotnet/orleans/blob/main/src/AdoNet/Orleans.Reminders.AdoNet/PostgreSQL-Reminders.sql)
- [MySQL/MariaDB](https://github.com/dotnet/orleans/blob/main/src/AdoNet/Orleans.Reminders.AdoNet/MySQL-Reminders.sql)
- [Oracle](https://github.com/dotnet/orleans/blob/main/src/AdoNet/Orleans.Reminders.AdoNet/Oracle-Reminders.sql)

<a id="grain-directory-scripts"></a>
## Grain 目录脚本

- [SQL Server](https://github.com/dotnet/orleans/blob/main/src/AdoNet/Orleans.GrainDirectory.AdoNet/SQLServer-GrainDirectory.sql)
- [PostgreSQL](https://github.com/dotnet/orleans/blob/main/src/AdoNet/Orleans.GrainDirectory.AdoNet/PostgreSQL-GrainDirectory.sql)
- [MySQL/MariaDB](https://github.com/dotnet/orleans/blob/main/src/AdoNet/Orleans.GrainDirectory.AdoNet/MySQL-GrainDirectory.sql)

并非每项功能都支持所有数据库。该 Orleans 版本是否支持某项功能，应以提供程序目录中是否存在相应脚本为准。

<a id="apply-and-upgrade-schemas"></a>
## 应用及升级数据库架构

1. 按数据库恢复策略备份应用程序数据。
2. 对新数据库应用主脚本。
3. 为每项已配置的 Orleans 功能应用相应脚本。
4. 从旧版数据库架构升级时，审查并应用提供程序的 `Migrations` 目录中的脚本。
5. 使用相同驱动程序及数据库引擎版本的预发布集群进行验证。

<a id="production-database-layout"></a>
## 生产数据库布局

提供的脚本是初始数据库架构，而不是通用的生产数据库布局。脚本在数据库用户的默认架构中创建未限定架构名称的表，ADO.NET 提供程序不公开架构或文件组配置。生产部署可以选择专用架构、文件组、分区或其他数据库特定的存储功能，但这些选择必须在数据库部署中实现，并与 `OrleansQuery` 中存储的查询保持一致。保留提供程序期望的表名、列、参数和结果结构，并在发布之前使用预发布集群验证自定义脚本及查询。

有关宿主配置，请参阅[配置 ADO.NET 提供程序](configuring-ado-dot-net-providers.md)。
