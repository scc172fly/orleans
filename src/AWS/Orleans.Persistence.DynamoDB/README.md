<a id="microsoft-orleans-persistence-for-dynamodb"></a>

# Microsoft Orleans 持久性 DynamoDB

<a id="introduction"></a>

## 简介
Microsoft Orleans 持久性 DynamoDB 提供 grain 持久性或持久性 Microsoft Orleans 使用亚马逊 DynamoDB。这样可以让您 grains 坚持自己在 DynamoDB 当他们被重新造化的时候,你们要装填它。

<a id="getting-started"></a>

## 入门
要使用此软件包, 请通过 NuGet安装 :

```shell
dotnet add package Microsoft.Orleans.Persistence.DynamoDB
```

<a id="example---configuring-dynamodb-persistence"></a>

## 示例 - 配置 DynamoDB 持久性
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

## 服务身份和升级

明确为每个提供者设置 `DynamoDBStorageOptions.ServiceId` 。 它将每一个 grain 键的前缀标为 `{ServiceId}_{grainId}` , 并且必须保持共享状态的部署之间的稳定性。 当启动验证报告未设置时, 启动验证报告为 `OrleansConfigurationException` 。

对于新部署,选择一个稳定的应用程序识别标识符,例如分组 `ServiceId`。升级时,保留用于写入现有状态的标识符。如果部署使用了前一个空字符串默认,则设置 `ServiceId = string.Empty` 代码或 `"ServiceId": ""` 配置中保存这些密钥的存取权限。选择不同的识别符号可以选择不同的州名空间;在前缀下访问现有状态需要单独的数据迁移 。

<a id="endpoints-and-credentials"></a>

## 终点和全权证书

`Service` 选择 AWS 区域或 HTTP/HTTPS 端点 URL。 `AccessKey` 和 `SecretKey` 提供明确的全权证书;添加 `Token` 会话证书。 `ProfileName` 在省略明确密钥时选择名为 AWS 的配置文件。这些全权证书适用于区域和端点 URL 。

AWS SDK 证书链路由提供区域及HTTPS终点点的证书,如果删除明确的密钥和剖面, HTTP终点点在这种情况下使用当地开发证书 `dummy` 和 `dummyKey` 。当 DynamoDB 地方机构使用访问键选择数据库时,配置明确的全权证书。

<a id="example---using-grain-storage-in-a-grain"></a>

## 实例 - 使用 Grain 储存在 Grain
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

## 文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件
关于更全面的文件,请参见:
- [Microsoft Orleans 文档](https://dotnet.github.io/orleans/docs/)
- [Grain 持久性](https://dotnet.github.io/orleans/docs/grains/grain-persistence/)
- [.NET 文档的 AWSSDK](https://docs.aws.amazon.com/sdk-for-net/index.html)

<a id="feedback--contributing"></a>

## 反馈和贡献
- 如果有任何问题或希望提供反馈,请 [GitHub上的开关](https://github.com/dotnet/orleans/issues)
- 加入我们社区 [不一致](https://aka.ms/orleans-discord)
- 跟随 [@msftorleans 地图上写着:](https://twitter.com/msftorleans) 推特账户发布 Orleans 通知
- 请审查我们的 [会费捐款准则](https://github.com/dotnet/orleans/blob/main/CONTRIBUTING.md)
- 该项目在 [麻省理工学院许可证](https://github.com/dotnet/orleans/blob/main/LICENSE)下获得许可证