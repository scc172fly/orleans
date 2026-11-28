<a id="microsoft-orleans-durable"></a>

# Microsoft Orleans Durable

<a id="introduction"></a>

## 简介
Microsoft Orleans Durable 为 .NET 11（`net11.0`）上的 Orleans Grain 提供持久化状态、作业、消息传递和任务功能。每项功能均在 Silo 构建器上独立注册：

| 功能 | 注册方法 | 命名空间 | 指南 |
|---|---|---|---|
| 日志化状态 | `AddJournaling()` | `Orleans.Durable.Journaling` | [Journaling 指南](https://github.com/dotnet/orleans/blob/main/src/Orleans.Durable/Journaling/README.md) |
| 持久化作业 | `AddDurableJobs()` | `Orleans.Durable.Jobs` | [持久化作业](https://github.com/dotnet/orleans/blob/main/src/Orleans.Durable/DurableJobs/README.md) |
| 消息传递 | `AddMessaging()` | `Orleans.Durable.Messaging` | [消息传递](https://github.com/dotnet/orleans/blob/main/src/Orleans.Durable/Messaging/README.md) |
| 持久化任务 | `AddDurableTasks()` | `Orleans.Durable.Tasks` | [持久化任务](https://github.com/dotnet/orleans/blob/main/src/Orleans.Durable/DurableTasks/README.md) |

存储提供程序通过 `Microsoft.Orleans.Durable.AzureStorage`、`Microsoft.Orleans.Durable.S3` 和 `Microsoft.Orleans.Durable.Redis` 分别发布。`Microsoft.Orleans.Durable.Abstractions` 定义持久化任务的编程模型。声明 `DurableTask` 方法的 Grain 接口项目和调用这些方法的客户端还需要引用 `Microsoft.Orleans.Durable`，该包提供持久化任务调用的代码生成映射以及客户端 `AddDurableTasks()` 注册方法。

<a id="getting-started"></a>

## 入门
要使用此包，请通过 NuGet 安装：

```shell
dotnet add package Microsoft.Orleans.Durable --prerelease
```

<a id="documentation"></a>

## 文档
有关更全面的文档，请参阅：
- [Microsoft Orleans 文档](https://learn.microsoft.com/dotnet/orleans/)

<a id="feedback--contributing"></a>

## 反馈与贡献
- 如果遇到问题或希望提供反馈，请[在 GitHub 上创建 Issue](https://github.com/dotnet/orleans/issues)
- 欢迎加入我们的 [Discord](https://aka.ms/orleans-discord) 社区
- 关注 Twitter 账号 [@msftorleans](https://twitter.com/msftorleans)，获取 Orleans 公告
- 欢迎贡献！请查阅我们的[贡献指南](https://github.com/dotnet/orleans/blob/main/CONTRIBUTING.md)
- 本项目采用 [MIT 许可证](https://github.com/dotnet/orleans/blob/main/LICENSE)