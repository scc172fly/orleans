---
title: "运行 Orleans 清点样本"
description: "运行 Azure 布线和 Redis 实验 Orleans 日志示例。"
ms.date: 08/21/2026
ms.topic: tutorial
---

<a id="run-orleans-journaling-samples"></a>

# 运行 Orleans 清点样本

[与 Azure 布线 JSON](https://github.com/dotnet/orleans/tree/main/samples/JournalingAzureBlobJson) 样本将实验性日记编程模式结束,使用 .NET Aspire和 Azure 存储模拟器,并使用本地仿真证书运行。

样本表明:

- 普通 <xref:Orleans.Grain> 注射了, <xref:Orleans.Durable.Journaling.IDurableStateManager>,使用建造时间 `GetOrAdd` 并激活生命周期恢复
- <xref:Orleans.Durable.Journaling.IDurableDictionary`2>、 <xref:Orleans.Durable.Journaling.IDurableList`1>、 <xref:Orleans.Durable.Journaling.IDurableQueue`1>和 <xref:Orleans.Durable.Journaling.IDurableSet`1>。
- <xref:Orleans.Durable.Journaling.IDurableValue`1>、日记支持 <xref:Orleans.Runtime.IPersistentState`1>和 <xref:Orleans.Durable.Journaling.IDurableTaskCompletionSource`1>。
- JSON 所有日记有效载荷类型的源生成元数据。
- Azure 球形WAL和检查站名称定制。
- 一份确认的书面、 grain 的解除、恢复和收复状态核查。
- 检查原始 JSON 行日记 。

<a id="run-with-aspire"></a>

## 与阿西普尔运行

安装 .NET SDK、 [指定 CLI](https://aspire.dev/get-started/install-cli/)和一个与多克兼容的容器运行时间。从样本目录运行:

```powershell
aspire run --project JournalingAzureBlobJson.AppHost
```

应用程序主机启动 Azure 存储模拟器和样本服务。 该服务写入了每个内建的耐久状态类别, 禁用了 grain, 验证新激活上回收的价值, 并打印存储的 JSON 线 。

样本保留了自己的 NuGet 一揽子申报单,经理的API等待公布,而仓库 `samples\Build-Samples.ps1` 供应包则从现有来源建立。

<a id="run-the-redis-sample"></a>

## 运行 Redis 样本

[Redis 日记片断工程](https://github.com/dotnet/orleans/tree/main/docs/site/src/content/docs/grains/journaling/snippets/redis-journaling) 保留键入注入和 <xref:Orleans.Durable.Journaling.DurableGrain> 帮助。 它配置 Redis, 写一个 <xref:Orleans.Durable.Journaling.IDurableValue`1>, 停止使用 grain, 并核实新激活的恢复情况:

:::code language="csharp" source="./snippets/redis-journaling/Program.cs" id="redis_journal_counter":::

日记片段项目引用当前仓库源, 因此它们会练习已记录的 API 而不替换旧软件包。 启动本地 Redis 服务器, 然后从仓库根运行样本 :

```powershell
dotnet run --project docs/site/src/content/docs/grains/journaling/snippets/redis-journaling -- "localhost:6379"
```

通行证a StackExchange.Redis 将连接字符串作为第一个参数 Redis 可在另一个端点上找到。 样本使用孤立的密钥前缀 `journaling-docs-sample`配置 Redis 在评价服务器重新启动恢复之前,要保持并复制。
