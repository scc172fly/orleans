<a id="microsoft-orleans-serialization-for-messagepack"></a>

# Microsoft Orleans MessagePack 序列化

<a id="introduction"></a>

## 简介
Microsoft Orleans MessagePack 序列化使用 MessagePack 格式为 Microsoft Orleans 提供序列化支持。这种高性能二进制序列化格式适用于要求高效序列化与反序列化的场景。

<a id="getting-started"></a>

## 入门
要使用此包，请通过 NuGet 安装：

```shell
dotnet add package Microsoft.Orleans.Serialization.MessagePack
```

<a id="example---configuring-messagepack-serialization"></a>

## 示例 - 配置 MessagePack 序列化
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
            // Configure MessagePack as a serializer
            .AddSerializer(serializerBuilder => serializerBuilder.AddMessagePackSerializer());
    });

// Run the host
await builder.RunAsync();
```

<a id="example---using-messagepack-with-a-custom-type"></a>

## 示例 - 将 MessagePack 用于自定义类型
```csharp
using Orleans;
using Orleans.Serialization.Cloning;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.Configuration;
using Orleans.Serialization.Serializers;
using MessagePack;
namespace ExampleGrains;

// Define a class with MessagePack attributes
[MessagePackObject]
public class MyMessagePackClass
{
    [Key(0)]
    public string Name { get; set; }
    
    [Key(1)]
    public int Age { get; set; }
    
    [Key(2)]
    public List<string> Tags { get; set; }
}

// You can use it directly in your grain interfaces and implementation
public interface IMyGrain : IGrainWithStringKey
{
    Task<MyMessagePackClass> GetData();
    Task SetData(MyMessagePackClass data);
}

public class MyGrain : Grain, IMyGrain
{
    private MyMessagePackClass _data;

    public Task<MyMessagePackClass> GetData()
    {
        return Task.FromResult(_data);
    }

    public Task SetData(MyMessagePackClass data)
    {
        _data = data;
        return Task.CompletedTask;
    }
}
```

<a id="documentation"></a>

## 文档
有关更全面的文档，请参阅：
- [Microsoft Orleans 文档](https://dotnet.github.io/orleans/docs/)
- [Orleans 序列化](https://dotnet.github.io/orleans/docs/host/configuration-guide/serialization/)
- [MessagePack for C#](https://github.com/neuecc/MessagePack-CSharp)

<a id="feedback--contributing"></a>

## 反馈与贡献
- 如果遇到问题或希望提供反馈，请[在 GitHub 上创建 Issue](https://github.com/dotnet/orleans/issues)
- 加入我们的 [Discord](https://aka.ms/orleans-discord) 社区
- 关注 Twitter 上的 [@msftorleans](https://twitter.com/msftorleans) 账号，获取 Orleans 公告
- 欢迎贡献！请阅读我们的[贡献指南](https://github.com/dotnet/orleans/blob/main/CONTRIBUTING.md)
- 本项目采用 [MIT 许可证](https://github.com/dotnet/orleans/blob/main/LICENSE)