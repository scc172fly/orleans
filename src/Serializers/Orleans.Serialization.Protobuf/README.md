<a id="microsoft-orleans-serialization-for-protobuf"></a>

# Microsoft Orleans 原生虫的序列化

<a id="introduction"></a>

## 简介
ProtobufMicrosoft Orleans 序列化为 Microsoft Orleans 提供协议缓冲(Protobuf)序列化支持,使用 **Google.Protobuf**。这个软件包将谷歌的官方 `Google.Protobuf` 库与 Orleans合并,允许您在您的 grain 接口和执行中使用协议缓冲信息。

<a id="getting-started"></a>

## 入门
要使用此软件包, 请通过 NuGet安装 :

```shell
dotnet add package Microsoft.Orleans.Serialization.Protobuf
```

<a id="example---configuring-protobuf-serialization"></a>

## 实例 - 配置方案方案的序列化
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

## 使用 Orleans的质效虫类型
此软件包支持类型 `.proto` 使用 Google.Protobuf。有关创建Protobuff 消息和配置您的工程的详细信息,请参见 [为 .NET 应用程序创建 Protobuf 消息](https://learn.microsoft.com/aspnet/core/grpc/protobuf).

一旦定义了您的 Protobuf 信件并配置了代码生成, 您可以在您的 grain 接口中直接使用它们:

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

**注:** Google.Protobuf 收藏类型 (`RepeatedField<T>`, `MapField<TKey, TValue>`,和 `ByteString`)自动支持。

<a id="documentation"></a>

## 文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件
关于更全面的文件,请参见:
- [为 .NET 应用程序创建 Protobuf 消息](https://learn.microsoft.com/aspnet/core/grpc/protobuf) - 与《保护生物和毒素武器( .NET
- [Microsoft Orleans 文档](https://dotnet.github.io/orleans/docs/)
- [Orleans 序号](https://dotnet.github.io/orleans/docs/host/configuration-guide/serialization/)

<a id="feedback--contributing"></a>

## 反馈和贡献
- 如果有任何问题或希望提供反馈,请 [GitHub上的开关](https://github.com/dotnet/orleans/issues)
- 加入我们社区 [不一致](https://aka.ms/orleans-discord)
- 跟随 [@msftorleans 地图上写着:](https://twitter.com/msftorleans) 推特账户发布 Orleans 通知
- 请审查我们的 [会费捐款准则](https://github.com/dotnet/orleans/blob/main/CONTRIBUTING.md)
- 该项目在 [麻省理工学院许可证](https://github.com/dotnet/orleans/blob/main/LICENSE)下获得许可证