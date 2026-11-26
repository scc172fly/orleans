<a id="microsoft-orleans-clustering-for-dynamodb"></a>

# Microsoft Orleans 分组 DynamoDB

<a id="introduction"></a>

## 简介
Microsoft Orleans 分组 DynamoDB 提供分组成员功能 Microsoft Orleans 使用亚马逊 DynamoDB。此选项允许 Orleans silos 协调和组成一个组群,使用 DynamoDB 做为后援商店

<a id="getting-started"></a>

## 入门
要使用此软件包, 请通过 NuGet安装 :

```shell
dotnet add package Microsoft.Orleans.Clustering.DynamoDB
```

<a id="example---configuring-dynamodb-membership"></a>

## 示例 - 配置 DynamoDB 成员
```csharp
using Microsoft.Extensions.Hosting;
using Orleans.Hosting;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;

namespace ExampleGrains;

// Define a grain interface
public interface IHelloGrain : IGrainWithStringKey
{
    Task<string> SayHello(string greeting);
}

// Implement the grain interface
public class HelloGrain : Grain, IHelloGrain
{
    public Task<string> SayHello(string greeting)
    {
        return Task.FromResult($"Hello, {greeting}!");
    }
}

var builder = Host.CreateApplicationBuilder(args)
    .UseOrleans(siloBuilder =>
    {
        siloBuilder
            // Configure DynamoDB clustering
            .UseDynamoDBClustering(options =>
            {
                options.Service = "us-east-1";
                options.TableName = "OrleansClusteringTable";
                options.UseProvisionedThroughput = false;
            });
    });

var host = builder.Build();
await host.StartAsync();

// Get a reference to a grain and call it
var client = host.Services.GetRequiredService<IClusterClient>();
var grain = client.GetGrain<IHelloGrain>("user123");
var response = await grain.SayHello("DynamoDB");

// Print the result
Console.WriteLine($"Grain response: {response}");

// Keep the host running until the application is shut down
await host.WaitForShutdownAsync();
```

<a id="endpoints-and-credentials"></a>

## 终点和全权证书

`Service` 选择 AWS 区域或 HTTP/HTTPS 端点 URL。 `AccessKey` 和 `SecretKey` 提供明确的全权证书;添加 `Token` 会话证书。 `ProfileName` 在省略明确密钥时选择名为 AWS 的配置文件。这些全权证书适用于区域和端点 URL 。

AWS SDK 证书链路由提供区域及HTTPS终点点的证书,如果删除明确的密钥和剖面, HTTP终点点在这种情况下使用当地开发证书 `dummy` 和 `dummyKey` 。当 DynamoDB 地方机构使用访问键选择数据库时,配置明确的全权证书。

<a id="documentation"></a>

## 文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件
关于更全面的文件,请参见:
- [Microsoft Orleans 文档](https://dotnet.github.io/orleans/docs/)
- [配置指南](https://dotnet.github.io/orleans/docs/host/configuration-guide/)
- [Orleans 组合](https://dotnet.github.io/orleans/docs/implementation/cluster-management/)
- [.NET 文档的 AWSSDK](https://docs.aws.amazon.com/sdk-for-net/index.html)

<a id="feedback--contributing"></a>

## 反馈和贡献
- 如果有任何问题或希望提供反馈,请 [GitHub上的开关](https://github.com/dotnet/orleans/issues)
- 加入我们社区 [不一致](https://aka.ms/orleans-discord)
- 跟随 [@msftorleans 地图上写着:](https://twitter.com/msftorleans) 推特账户发布 Orleans 通知
- 请审查我们的 [会费捐款准则](https://github.com/dotnet/orleans/blob/main/CONTRIBUTING.md)
- 该项目在 [麻省理工学院许可证](https://github.com/dotnet/orleans/blob/main/LICENSE)下获得许可证