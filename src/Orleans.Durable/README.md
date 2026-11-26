<a id="microsoft-orleans-durable"></a>

# Microsoft Orleans 持久

<a id="introduction"></a>

## 简介
Microsoft Orleans 持久持久提供持久的状态、工作、信息传递和任务, Orleans grains 日期 时 .NET 11 (`net11.0`每个特征都独立登记在 silo 建设器 :

| 特征 | 登记登记 | 命名空间 | 指南指南指南指南 |
|---|---|---|---|
| 日刊 | `AddJournaling()` | `Orleans.Durable.Journaling` | [日刊](https://github.com/dotnet/orleans/blob/main/src/Orleans.Durable/Journaling/README.md) |
| 持久就业 | `AddDurableJobs()` | `Orleans.Durable.Jobs` | [持久就业](https://github.com/dotnet/orleans/blob/main/src/Orleans.Durable/DurableJobs/README.md) |
| 通信通信 | `AddMessaging()` | `Orleans.Durable.Messaging` | [通信通信](https://github.com/dotnet/orleans/blob/main/src/Orleans.Durable/Messaging/README.md) |
| 持久任务 | `AddDurableTasks()` | `Orleans.Durable.Tasks` | [持久任务](https://github.com/dotnet/orleans/blob/main/src/Orleans.Durable/DurableTasks/README.md) |

储存提供者船舶分别位于 `Microsoft.Orleans.Durable.AzureStorage`、 `Microsoft.Orleans.Durable.S3`和 `Microsoft.Orleans.Durable.Redis`。 `Microsoft.Orleans.Durable.Abstractions` 界定了持久任务方案编制模式。 Grain 接口项目宣布 `DurableTask` 方法,客户也称其为参考 `Microsoft.Orleans.Durable`,提供用于耐用任务职业的编码生成图和客户 `AddDurableTasks()` 登记。

<a id="getting-started"></a>

## 入门
要使用此软件包, 请通过 NuGet安装 :

```shell
dotnet add package Microsoft.Orleans.Durable --prerelease
```

<a id="documentation"></a>

## 文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件
关于更全面的文件,请参见:
- [Microsoft Orleans 文档](https://learn.microsoft.com/dotnet/orleans/)

<a id="feedback--contributing"></a>

## 反馈和贡献
- 如果有任何问题或希望提供反馈,请 [GitHub上的开关](https://github.com/dotnet/orleans/issues)
- 加入我们社区 [不一致](https://aka.ms/orleans-discord)
- 跟随 [@msftorleans 地图上写着:](https://twitter.com/msftorleans) 推特账户发布 Orleans 通知
- 请审查我们的 [会费捐款准则](https://github.com/dotnet/orleans/blob/main/CONTRIBUTING.md)
- 该项目在 [麻省理工学院许可证](https://github.com/dotnet/orleans/blob/main/LICENSE)下获得许可证