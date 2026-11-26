<a id="microsoft-orleans-core-abstractions"></a>

# Microsoft Orleans 核心事件

<a id="introduction"></a>

## 简介
Microsoft Orleans 核心文摘社是 Orleans 的基础图书馆,载有用于执行 grains 和客户代码的公共方案编制动画片动画片。该软件包定义了构成 Orleans 编程模式的核心抽象内容,包括 grain 接口、 grain 参考界面和属性。

<a id="getting-started"></a>

## 入门
要使用此软件包, 请通过 NuGet安装 :

```shell
dotnet add package Microsoft.Orleans.Core.Abstractions
```

此软件包既依赖客户端,也依赖 silo (服务器) 应用程序,当您引用 Orleans SDK或 Orleans 客户/服务器的元软件包时自动包含此软件包。

<a id="example---defining-a-grain-interface"></a>

## 示例 - 定义 Grain 接口
```csharp
using Orleans;

namespace MyGrainInterfaces;

public interface IHelloGrain : IGrainWithStringKey
{
    Task<string> SayHello(string greeting);
}
```

<a id="documentation"></a>

## 文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件
关于更全面的文件,请参见:
- [Microsoft Orleans 文档](https://dotnet.github.io/orleans/docs/)
- [开发 grains](https://dotnet.github.io/orleans/docs/grains/)
- [Grain 引用](https://dotnet.github.io/orleans/docs/grains/grain-references/)

<a id="feedback--contributing"></a>

## 反馈和贡献
- 如果有任何问题或希望提供反馈,请 [GitHub上的开关](https://github.com/dotnet/orleans/issues)
- 加入我们社区 [不一致](https://aka.ms/orleans-discord)
- 跟随 [@msftorleans 地图上写着:](https://twitter.com/msftorleans) 推特账户发布 Orleans 通知
- 请审查我们的 [会费捐款准则](https://github.com/dotnet/orleans/blob/main/CONTRIBUTING.md)
- 该项目在 [麻省理工学院许可证](https://github.com/dotnet/orleans/blob/main/LICENSE)下获得许可证