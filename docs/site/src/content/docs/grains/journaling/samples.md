---
title: 运行 Orleans Journaling 示例
description: 运行实验性 Orleans Journaling 的 Azure Blob 和 Redis 示例。
ms.date: 08/21/2026
ms.topic: tutorial
---

<a id="run-orleans-journaling-samples"></a>

# 运行 Orleans Journaling 示例

[使用 Azure Blob JSON 的 Journaling 示例](https://github.com/dotnet/orleans/tree/main/samples/JournalingAzureBlobJson) 对实验性 Journaling 编程模型进行端到端验证。它使用 .NET Aspire 和 Azure Storage 模拟器，并使用本地模拟器凭据运行。

该示例演示以下内容：

- 一个普通的 <xref:Orleans.Grain>，注入了 <xref:Orleans.Durable.Journaling.IDurableStateManager>，使用构造期间的 `GetOrAdd` 声明及激活生命周期恢复机制。
- <xref:Orleans.Durable.Journaling.IDurableDictionary`2>、<xref:Orleans.Durable.Journaling.IDurableList`1>、<xref:Orleans.Durable.Journaling.IDurableQueue`1> 和 <xref:Orleans.Durable.Journaling.IDurableSet`1>。
- <xref:Orleans.Durable.Journaling.IDurableValue`1>、以日志为后端的 <xref:Orleans.Runtime.IPersistentState`1>，以及 <xref:Orleans.Durable.Journaling.IDurableTaskCompletionSource`1>。
- 所有日志持久化有效载荷类型的 JSON 源码生成元数据。
- 自定义 Azure Blob 的 WAL 和检查点名称。
- 已确认的写入、Grain 停用、重新激活及恢复状态验证。
- 检查原始 JSON Lines 日志。

<a id="run-with-aspire"></a>

## 使用 Aspire 运行

安装 .NET SDK、[Aspire CLI](https://aspire.dev/get-started/install-cli/) 和兼容 Docker 的容器运行时。在示例目录中运行：

```powershell
aspire run --project JournalingAzureBlobJson.AppHost
```

AppHost 启动 Azure Storage 模拟器及示例服务。服务写入每种内置持久化状态类别，停用 Grain，在新的激活实例上验证恢复值，并输出存储的 JSON Lines。

此示例维护自身的 NuGet 包声明。在管理器 API 尚待发布期间，仓库的 `samples\Build-Samples.ps1` 会提供根据当前源码构建的包。

<a id="run-the-redis-sample"></a>

## 运行 Redis 示例

[Redis Journaling 代码片段项目](https://github.com/dotnet/orleans/tree/main/docs/site/src/content/docs/grains/journaling/snippets/redis-journaling) 保留了键控注入和 <xref:Orleans.Durable.Journaling.DurableGrain> 辅助类。它配置 Redis，写入一个 <xref:Orleans.Durable.Journaling.IDurableValue`1>，停用 Grain，然后在新的激活实例上验证恢复情况：

:::code language="csharp" source="./snippets/redis-journaling/Program.cs" id="redis_journal_counter":::

Journaling 代码片段项目引用当前仓库源码，因此可以验证文档中的 API，而不会使用较早的包替代。启动本地 Redis 服务器，然后从仓库根目录运行示例：

```powershell
dotnet run --project docs/site/src/content/docs/grains/journaling/snippets/redis-journaling -- "localhost:6379"
```

如果 Redis 位于其他端点，请将 StackExchange.Redis 连接字符串作为第一个参数传入。示例使用独立的键前缀 `journaling-docs-sample`。评估服务器重启后的恢复能力之前，应配置 Redis 的持久化和复制。
