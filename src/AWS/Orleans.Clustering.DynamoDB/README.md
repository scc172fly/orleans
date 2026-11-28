<a id="microsoft-orleans-clustering-for-dynamodb"></a>

# Microsoft Orleans DynamoDB 集群提供程序

<a id="introduction"></a>

## 简介
Microsoft Orleans DynamoDB 集群提供程序利用 Amazon DynamoDB 为 Microsoft Orleans 提供集群成员资格功能，使 Orleans Silo 能够以 DynamoDB 为后备存储进行协调并组成集群。

<a id="getting-started"></a>

## 入门
要使用此包，请通过 NuGet 安装：

```shell
dotnet add package Microsoft.Orleans.Clustering.DynamoDB
```

<a id="example---configuring-dynamodb-membership"></a>

## 示例 - 配置 DynamoDB 成员资格
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

## 终结点与凭据

`Service` 用于选择 AWS 区域或 HTTP/HTTPS 终结点 URL。`AccessKey` 和 `SecretKey` 提供显式凭据；添加 `Token` 可提供会话凭据。省略显式密钥时，`ProfileName` 用于选择命名 AWS 配置文件。这些凭据同时适用于区域和终结点 URL。

省略显式密钥和配置文件时，区域及 HTTPS 终结点的凭据由 AWS SDK 凭据链提供。在此情况下，HTTP 终结点使用本地开发凭据 `dummy` 和 `dummyKey`。如果 DynamoDB Local 实例通过访问密钥选择数据库，请配置显式凭据。

<a id="documentation"></a>

## 文档
有关更全面的文档，请参阅：
- [Microsoft Orleans 文档](https://dotnet.github.io/orleans/docs/)
- [配置指南](https://dotnet.github.io/orleans/docs/host/configuration-guide/)
- [Orleans 集群](https://dotnet.github.io/orleans/docs/implementation/cluster-management/)
- [AWS SDK for .NET 文档](https://docs.aws.amazon.com/sdk-for-net/index.html)

<a id="feedback--contributing"></a>

## 反馈与贡献
- 如果遇到问题或希望提供反馈，请[在 GitHub 上创建 Issue](https://github.com/dotnet/orleans/issues)
- 加入我们的 [Discord](https://aka.ms/orleans-discord) 社区
- 关注 Twitter 上的 [@msftorleans](https://twitter.com/msftorleans) 账号，获取 Orleans 公告
- 欢迎贡献！请阅读我们的[贡献指南](https://github.com/dotnet/orleans/blob/main/CONTRIBUTING.md)
- 本项目采用 [MIT 许可证](https://github.com/dotnet/orleans/blob/main/LICENSE)