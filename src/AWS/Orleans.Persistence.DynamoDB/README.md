<a id="microsoft-orleans-persistence-for-dynamodb"></a>

# Microsoft Orleans DynamoDB 持久化提供程序

<a id="introduction"></a>

## 简介
Microsoft Orleans DynamoDB 持久化提供程序利用 Amazon DynamoDB 为 Microsoft Orleans 提供 Grain 持久化功能，使 Grain 能够将状态持久化到 DynamoDB，并在重新激活时加载状态。

<a id="getting-started"></a>

## 入门
要使用此包，请通过 NuGet 安装：

```shell
dotnet add package Microsoft.Orleans.Persistence.DynamoDB
```

<a id="example---configuring-dynamodb-persistence"></a>

## 示例 - 配置 DynamoDB 持久化
```csharp
using Microsoft.Extensions.Hosting;
using Orleans.Configuration;
using Orleans.Hosting;

var builder = Host.CreateApplicationBuilder(args)
    .UseOrleans(siloBuilder =>
    {
        siloBuilder
            .UseLocalhostClustering()
            // Configure DynamoDB as grain storage
            .AddDynamoDBGrainStorage(
                name: "dynamoStore",
                configureOptions: options =>
                {
                    options.Service = "us-east-1";
                    options.ServiceId = "my-application";
                    options.TableName = "OrleansGrainState";
                    options.UseProvisionedThroughput = false;
                });
    });

// Run the host
await builder.RunAsync();
```

<a id="service-identity-and-upgrades"></a>

## 服务标识与升级

请为每个提供程序显式设置 `DynamoDBStorageOptions.ServiceId`。它为每个 Grain 状态键添加服务标识前缀，形成 `{ServiceId}_{grainId}` 格式，并且必须在共享状态的各次部署之间保持稳定。未设置时，启动验证将报告 `OrleansConfigurationException`。

对于新部署，请选择稳定的应用程序标识符，例如集群的 `ServiceId`。升级时保留写入现有状态所使用的标识符。如果部署使用了先前默认的空字符串，请在代码中设置 `ServiceId = string.Empty`，或在配置中设置 `"ServiceId": ""`，以保留对这些键的访问能力。选择不同的标识符会切换到另一个状态命名空间；要在该前缀下访问现有状态，需要另行迁移数据。

<a id="endpoints-and-credentials"></a>

## 终结点与凭据

`Service` 用于选择 AWS 区域或 HTTP/HTTPS 终结点 URL。`AccessKey` 和 `SecretKey` 提供显式凭据；添加 `Token` 可提供会话凭据。省略显式密钥时，`ProfileName` 用于选择命名 AWS 配置文件。这些凭据同时适用于区域和终结点 URL。

省略显式密钥和配置文件时，区域及 HTTPS 终结点的凭据由 AWS SDK 凭据链提供。在此情况下，HTTP 终结点使用本地开发凭据 `dummy` 和 `dummyKey`。如果 DynamoDB Local 实例通过访问密钥选择数据库，请配置显式凭据。

<a id="example---using-grain-storage-in-a-grain"></a>

## 示例 - 在 Grain 中使用 Grain 存储
```csharp
// Define grain state class

public class MyGrainState
{
    public string Data { get; set; }
    public int Version { get; set; }
}

// Grain implementation that uses the DynamoDB storage
public class MyGrain : Grain, IMyGrain, IGrainWithStringKey
{
    private readonly IPersistentState<MyGrainState> _state;

    public MyGrain([PersistentState("state", "dynamoStore")] IPersistentState<MyGrainState> state)
    {
        _state = state;
    }

    public async Task SetData(string data)
    {
        _state.State.Data = data;
        _state.State.Version++;
        await _state.WriteStateAsync();
    }

    public Task<string> GetData()
    {
        return Task.FromResult(_state.State.Data);
    }
}
```

<a id="documentation"></a>

## 文档
有关更全面的文档，请参阅：
- [Microsoft Orleans 文档](https://dotnet.github.io/orleans/docs/)
- [Grain 持久化](https://dotnet.github.io/orleans/docs/grains/grain-persistence/)
- [AWS SDK for .NET 文档](https://docs.aws.amazon.com/sdk-for-net/index.html)

<a id="feedback--contributing"></a>

## 反馈与贡献
- 如果遇到问题或希望提供反馈，请[在 GitHub 上创建 Issue](https://github.com/dotnet/orleans/issues)
- 加入我们的 [Discord](https://aka.ms/orleans-discord) 社区
- 关注 Twitter 上的 [@msftorleans](https://twitter.com/msftorleans) 账号，获取 Orleans 公告
- 欢迎贡献！请阅读我们的[贡献指南](https://github.com/dotnet/orleans/blob/main/CONTRIBUTING.md)
- 本项目采用 [MIT 许可证](https://github.com/dotnet/orleans/blob/main/LICENSE)