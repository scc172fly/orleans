<a id="microsoft-orleans-serialization-abstractions"></a>

# Microsoft Orleans 序号

<a id="introduction"></a>

## 简介
Orleans 序列化抽象软件包提供了 Orleans 序列化所需的核心界面和属性。该软件包包含用于序列化的定义,但并不包含序列化实施本身。

<a id="getting-started"></a>

## 入门
要使用此软件包, 请通过 NuGet安装 :

```shell
dotnet add package Microsoft.Orleans.Serialization.Abstractions
```

当您引用 Orleans 序列化包或 Orleans SDK时,此软件包会自动包含。

<a id="example"></a>

## 示例

```csharp
using Orleans.Serialization;

// Define a serializable class
[GenerateSerializer]
public class MyData
{
    [Id(0)]
    public string Name { get; set; }
    
    [Id(1)]
    public int Age { get; set; }
    
    [Id(2)]
    public List<string> Tags { get; set; }
}
```

<a id="documentation"></a>

## 文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件
关于更全面的文件,请参见:
- [Microsoft Orleans 文档](https://dotnet.github.io/orleans/docs/)
- [Orleans中的序列化](https://dotnet.github.io/orleans/docs/host/configuration-guide/serialization/)
- [配置序列序列化](https://dotnet.github.io/orleans/docs/host/configuration-guide/serialization-configuration/)

<a id="feedback--contributing"></a>

## 反馈和贡献
- 如果有任何问题或希望提供反馈,请 [GitHub上的开关](https://github.com/dotnet/orleans/issues)
- 加入我们社区 [不一致](https://aka.ms/orleans-discord)
- 跟随 [@msftorleans 地图上写着:](https://twitter.com/msftorleans) 推特账户发布 Orleans 通知
- 请审查我们的 [会费捐款准则](https://github.com/dotnet/orleans/blob/main/CONTRIBUTING.md)
- 该项目在 [麻省理工学院许可证](https://github.com/dotnet/orleans/blob/main/LICENSE)下获得许可证