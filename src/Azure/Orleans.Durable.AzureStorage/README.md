<a id="microsoft-orleans-durable-azure-storage-providers"></a>

# Microsoft Orleans 持久化功能的 Azure Storage 提供程序

<a id="introduction"></a>

## 简介
Microsoft Orleans 持久化功能的 Azure Storage 提供程序为 `Microsoft.Orleans.Durable` 将日志存储在 Azure Blob Storage 或 Azure Table Storage 中：

- 用于日志化 Grain 状态的[日志存储](https://github.com/dotnet/orleans/blob/main/src/Azure/Orleans.Durable.AzureStorage/Journaling/README.md)，通过 `AddAzureBlobJournalStorage` 或 `AddAzureTableJournalStorage` 注册。
- 用于持久化作业分片的[持久化作业存储](https://github.com/dotnet/orleans/blob/main/src/Azure/Orleans.Durable.AzureStorage/DurableJobs/README.md)，通过 `UseAzureBlobDurableJobs` 或 `UseAzureTableDurableJobs` 注册。

<a id="getting-started"></a>

## 入门
通过 NuGet 安装此包：

```shell
dotnet add package Microsoft.Orleans.Durable.AzureStorage --prerelease
```

<a id="documentation"></a>

## 文档
有关更详细的文档，请参阅：
- [Microsoft Orleans 文档](https://learn.microsoft.com/dotnet/orleans/)

<a id="feedback--contributing"></a>

## 反馈与贡献
- 如果遇到问题或希望提供反馈，请[在 GitHub 上提交 Issue](https://github.com/dotnet/orleans/issues)。
- 欢迎加入我们的 [Discord](https://aka.ms/orleans-discord) 社区。
- 关注 Twitter 账号 [@msftorleans](https://twitter.com/msftorleans)，获取 Orleans 公告。
- 欢迎参与贡献！请先阅读[贡献指南](https://github.com/dotnet/orleans/blob/main/CONTRIBUTING.md)。
- 本项目采用 [MIT 许可证](https://github.com/dotnet/orleans/blob/main/LICENSE)。
