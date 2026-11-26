<a id="microsoft-orleans-serialization-for-systemtextjson"></a>

# Microsoft Orleans 用于 System.Text.Json

<a id="introduction"></a>

## 简介
Microsoft Orleans 用于 System.Text.Json 提供 JSON 用于 Microsoft Orleans 使用现代 System.Text.Json 库中,您可以使用高性能 JSON 用于从 .NET 运行时间在您的 Orleans 应用程序。

<a id="getting-started"></a>

## 入门
要使用此软件包, 请通过 NuGet安装 :

```shell
dotnet add package Microsoft.Orleans.Serialization.SystemTextJson
```

<a id="example---configuring-systemtextjson-serialization"></a>

## 示例 - 配置 System.Text.Json 序列化
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
            // Configure System.Text.Json as a serializer
            .AddSerializer(serializerBuilder => serializerBuilder.AddSystemTextJsonSerializer(type => type.Namespace.StartsWith("ExampleGrains")));
    });

// Run the host
await builder.RunAsync();
```

<a id="example---using-systemtextjson-with-a-custom-type"></a>

## 示例 - 使用自定义类型 System.Text.Json
```csharp
using Orleans;
using System.Text.Json.Serialization;
namespace ExampleGrains;

// Define a class with System.Text.Json attributes
public class MyJsonClass
{
    [JsonPropertyName("full_name")]
    public string Name { get; set; }
    
    [JsonPropertyName("age")]
    public int Age { get; set; }
    
    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; }
    
    [JsonIgnore]
    public string SecretData { get; set; }
}

// You can use it directly in your grain interfaces and implementation
public interface IMyGrain : IGrainWithStringKey
{
    Task<MyJsonClass> GetData();
    Task SetData(MyJsonClass data);
}

public class MyGrain : Grain, IMyGrain
{
    private MyJsonClass _data;

    public Task<MyJsonClass> GetData()
    {
        return Task.FromResult(_data);
    }

    public Task SetData(MyJsonClass data)
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
- [System.Text.Json 文档](https://learn.microsoft.com/dotnet/standard/serialization/system-text-json/overview)

<a id="feedback--contributing"></a>

## 反馈和贡献
- 如果有任何问题或希望提供反馈,请 [GitHub上的开关](https://github.com/dotnet/orleans/issues)
- 加入我们社区 [不一致](https://aka.ms/orleans-discord)
- 跟随 [@msftorleans 地图上写着:](https://twitter.com/msftorleans) 推特账户发布 Orleans 通知
- 请审查我们的 [会费捐款准则](https://github.com/dotnet/orleans/blob/main/CONTRIBUTING.md)
- 该项目在 [麻省理工学院许可证](https://github.com/dotnet/orleans/blob/main/LICENSE)下获得许可证