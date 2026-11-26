<a id="microsoft-orleans-grain-directory-for-azure-storage"></a>

# Microsoft Orleans Grain 目录目录 Azure 储存

<a id="introduction"></a>

## 简介
Microsoft Orleans Grain 目录目录 Azure 储存提供a grain 使用目录执行 Azure 储存。 grain 用于定位活动目录 grain 横跨组群的 数例,而此套包允许 Orleans 将信息存储在 Azure 储存。

<a id="getting-started"></a>

## 入门
要使用此软件包, 请通过 NuGet安装 :

```shell
dotnet add package Microsoft.Orleans.GrainDirectory.AzureStorage
```

<a id="example---configuring-azure-storage-grain-directory"></a>

## 示例 - 配置 Azure 存储 Grain 目录
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

## 文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件
关于更全面的文件,请参见:
- [Microsoft Orleans 文档](https://dotnet.github.io/orleans/docs/)
- [配置指南](https://dotnet.github.io/orleans/docs/host/configuration-guide/)
- [实施细节](https://dotnet.github.io/orleans/docs/implementation/)

<a id="feedback--contributing"></a>

## 反馈和贡献
- 如果有任何问题或希望提供反馈,请 [GitHub上的开关](https://github.com/dotnet/orleans/issues)
- 加入我们社区 [不一致](https://aka.ms/orleans-discord)
- 跟随 [@msftorleans 地图上写着:](https://twitter.com/msftorleans) 推特账户发布 Orleans 通知
- 请审查我们的 [会费捐款准则](https://github.com/dotnet/orleans/blob/main/CONTRIBUTING.md)
- 该项目在 [麻省理工学院许可证](https://github.com/dotnet/orleans/blob/main/LICENSE)下获得许可证