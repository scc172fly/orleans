<a id="microsoft-orleans-clustering-for-azure-cosmos-db"></a>

# Microsoft Orleans 的 Azure Cosmos DB 集群提供程序

<a id="introduction"></a>

## 简介
Microsoft Orleans 的 Azure Cosmos DB 集群提供程序使用 Azure Cosmos DB 为 Orleans 提供集群成员资格管理功能，使 Orleans Silo 能够以 Azure Cosmos DB 为后端存储进行协调并组成集群。

<a id="getting-started"></a>

## 入门
通过 NuGet 安装此包：

```shell
dotnet add package Microsoft.Orleans.Clustering.Cosmos
```

<a id="example---configuring-azure-cosmos-db-membership"></a>

## 示例：配置 Azure Cosmos DB 成员资格管理
```csharp
using Microsoft.Extensions.Hosting;
using Orleans.Hosting;

var builder = Host.CreateApplicationBuilder(args)
    .UseOrleans(siloBuilder =>
    {
        siloBuilder
            .UseCosmosClustering(options =>
            {
                options.AccountEndpoint = "https://YOUR_COSMOS_ENDPOINT";
                options.AccountKey = "YOUR_COSMOS_KEY";
                options.DB = "YOUR_DATABASE_NAME";
                options.CanCreateResources = true;
            });
    });

// Run the host
await builder.RunAsync();
```

<a id="documentation"></a>

## 文档
有关更详细的文档，请参阅：
- [Microsoft Orleans 文档](https://dotnet.github.io/orleans/docs/)
- [配置指南](https://dotnet.github.io/orleans/docs/host/configuration-guide/)
- [Orleans 集群管理](https://dotnet.github.io/orleans/docs/implementation/cluster-management/)

<a id="feedback--contributing"></a>

## 反馈与贡献
- 如果遇到问题或希望提供反馈，请[在 GitHub 上提交 Issue](https://github.com/dotnet/orleans/issues)。
- 欢迎加入我们的 [Discord](https://aka.ms/orleans-discord) 社区。
- 关注 Twitter 账号 [@msftorleans](https://twitter.com/msftorleans)，获取 Orleans 公告。
- 欢迎参与贡献！请先阅读[贡献指南](https://github.com/dotnet/orleans/blob/main/CONTRIBUTING.md)。
- 本项目采用 [MIT 许可证](https://github.com/dotnet/orleans/blob/main/LICENSE)。