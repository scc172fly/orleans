<a id="microsoft-orleans-serialization-for-protobuf"></a>

# Microsoft Orleans Protobuf 序列化

<a id="introduction"></a>

## 简介
Microsoft Orleans Protobuf 序列化使用 **Google.Protobuf** 为 Microsoft Orleans 提供 Protocol Buffers（Protobuf）序列化支持。此包将 Google 官方 `Google.Protobuf` 库与 Orleans 集成，使你能够在 Grain 接口及实现中使用 Protocol Buffers 消息。

<a id="getting-started"></a>

## 入门
要使用此包，请通过 NuGet 安装：

```shell
dotnet add package Microsoft.Orleans.Serialization.Protobuf
```

<a id="example---configuring-protobuf-serialization"></a>

## 示例 - 配置 Protobuf 序列化
```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans.Hosting;
using Orleans.Serialization;

var builder = Host.CreateApplicationBuilder(args)
    .UseOrleans(siloBuilder =>
    {
        siloBuilder
            .UseLocalhostClustering()
            // Configure Protobuf as a serializer
            .AddSerializer(serializerBuilder => serializerBuilder.AddProtobufSerializer());
    });

// Run the host
await builder.RunAsync();
```

<a id="using-protobuf-types-with-orleans"></a>

## 在 Orleans 中使用 Protobuf 类型
此包支持通过 Google.Protobuf 从 `.proto` 文件生成的类型。有关创建 Protobuf 消息及配置项目的详细信息，请参阅[为 .NET 应用创建 Protobuf 消息](https://learn.microsoft.com/aspnet/core/grpc/protobuf)。

定义 Protobuf 消息并配置代码生成后，即可直接在 Grain 接口中使用它们：

```csharp
using Orleans;
using MyApp.Models;

public interface IMyGrain : IGrainWithStringKey
{
    Task<MyProtobufClass> GetData();
    Task SetData(MyProtobufClass data);
}

public class MyGrain : Grain, IMyGrain
{
    private MyProtobufClass _data;

    public Task<MyProtobufClass> GetData() => Task.FromResult(_data);
    public Task SetData(MyProtobufClass data)
    {
        _data = data;
        return Task.CompletedTask;
    }
}
```

**注意：** 自动支持 Google.Protobuf 集合类型（`RepeatedField<T>`、`MapField<TKey, TValue>` 和 `ByteString`）。

<a id="documentation"></a>

## 文档
有关更全面的文档，请参阅：
- [为 .NET 应用创建 Protobuf 消息](https://learn.microsoft.com/aspnet/core/grpc/protobuf) - 在 .NET 中使用 Protobuf 的官方指南
- [Microsoft Orleans 文档](https://dotnet.github.io/orleans/docs/)
- [Orleans 序列化](https://dotnet.github.io/orleans/docs/host/configuration-guide/serialization/)

<a id="feedback--contributing"></a>

## 反馈与贡献
- 如果遇到问题或希望提供反馈，请[在 GitHub 上创建 Issue](https://github.com/dotnet/orleans/issues)
- 加入我们的 [Discord](https://aka.ms/orleans-discord) 社区
- 关注 Twitter 上的 [@msftorleans](https://twitter.com/msftorleans) 账号，获取 Orleans 公告
- 欢迎贡献！请阅读我们的[贡献指南](https://github.com/dotnet/orleans/blob/main/CONTRIBUTING.md)
- 本项目采用 [MIT 许可证](https://github.com/dotnet/orleans/blob/main/LICENSE)