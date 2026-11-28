<a id="microsoft-orleans-grain-directory-for-azure-storage"></a>

# Microsoft Orleans 的 Azure Storage Grain 目录

<a id="introduction"></a>

## 简介
Microsoft Orleans 的 Azure Storage Grain 目录提供了基于 Azure Storage 的 Grain 目录实现。Grain 目录用于在整个集群中定位已激活的 Grain 实例，此包允许 Orleans 将这些信息存储在 Azure Storage 中。

<a id="getting-started"></a>

## 入门
通过 NuGet 安装此包：

```shell
dotnet add package Microsoft.Orleans.GrainDirectory.AzureStorage
```

<a id="example---configuring-azure-storage-grain-directory"></a>

## 示例：配置 Azure Storage Grain 目录
```csharp
using Microsoft.Extensions.Hosting;
using Orleans.Hosting;

var builder = Host.CreateApplicationBuilder(args)
    .UseOrleans(siloBuilder =>
    {
        siloBuilder
            .UseLocalhostClustering()
            // Configure Azure Storage as grain directory
            .UseAzureStorageGrainDirectoryAsDefault(options =>
            {
                options.ConnectionString = "YOUR_AZURE_STORAGE_CONNECTION_STRING";
                options.TableName = "GrainDirectory";
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
- [实现细节](https://dotnet.github.io/orleans/docs/implementation/)

<a id="feedback--contributing"></a>

## 反馈与贡献
- 如果遇到问题或希望提供反馈，请[在 GitHub 上提交 Issue](https://github.com/dotnet/orleans/issues)。
- 欢迎加入我们的 [Discord](https://aka.ms/orleans-discord) 社区。
- 关注 Twitter 账号 [@msftorleans](https://twitter.com/msftorleans)，获取 Orleans 公告。
- 欢迎参与贡献！请先阅读[贡献指南](https://github.com/dotnet/orleans/blob/main/CONTRIBUTING.md)。
- 本项目采用 [MIT 许可证](https://github.com/dotnet/orleans/blob/main/LICENSE)。