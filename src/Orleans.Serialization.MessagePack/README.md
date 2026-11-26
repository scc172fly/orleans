<a id="microsoft-orleans-serialization-for-messagepack"></a>

# Microsoft Orleans 用于 MessagePack

<a id="introduction"></a>

## 简介
Microsoft Orleans 用于 MessagePack 提供 MessagePack 用于 Microsoft Orleans 使用 MessagePack 格式。这种高性能的二进制序列化格式对于需要高效序列化和机密化的设想情景是理想的。

<a id="getting-started"></a>

## 入门
要使用此软件包, 请通过 NuGet安装 :

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

## 示例 - 使用自定义类型 MessagePack
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

## 文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件
关于更全面的文件,请参见:
- [Microsoft Orleans 文档](https://dotnet.github.io/orleans/docs/)
- [Orleans 序号](https://dotnet.github.io/orleans/docs/host/configuration-guide/serialization/)
- [MessagePack C # 的 C #](https://github.com/neuecc/MessagePack-CSharp)

<a id="feedback--contributing"></a>

## 反馈和贡献
- 如果有任何问题或希望提供反馈,请 [GitHub上的开关](https://github.com/dotnet/orleans/issues)
- 加入我们社区 [不一致](https://aka.ms/orleans-discord)
- 跟随 [@msftorleans 地图上写着:](https://twitter.com/msftorleans) 推特账户发布 Orleans 通知
- 请审查我们的 [会费捐款准则](https://github.com/dotnet/orleans/blob/main/CONTRIBUTING.md)
- 该项目在 [麻省理工学院许可证](https://github.com/dotnet/orleans/blob/main/LICENSE)下获得许可证