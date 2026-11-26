<a id="microsoft-orleans-durable-azure-storage-providers"></a>

# Microsoft Orleans 耐久 Azure 储存提供者

<a id="introduction"></a>

## 简介
Microsoft Orleans 持久 Azure 存储供应商在 Azure 布布存储或 Azure 表表 储存 `Microsoft.Orleans.Durable`:

- [日记储存](https://github.com/dotnet/orleans/blob/main/src/Azure/Orleans.Durable.AzureStorage/Journaling/README.md) 用于日记 grain 登记在 `AddAzureBlobJournalStorage` 或 `AddAzureTableJournalStorage`.
- 耐用工作碎片[持久工作存储](https://github.com/dotnet/orleans/blob/main/src/Azure/Orleans.Durable.AzureStorage/DurableJobs/README.md) ,在 `UseAzureBlobDurableJobs` 或 `UseAzureTableDurableJobs`登记。

<a id="getting-started"></a>

## 入门
要使用此软件包, 请通过 NuGet安装 :

```shell
dotnet add package Microsoft.Orleans.Durable.AzureStorage --prerelease
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
