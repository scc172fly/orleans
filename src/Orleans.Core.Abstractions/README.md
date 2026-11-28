<a id="microsoft-orleans-core-abstractions"></a>

# Microsoft Orleans 核心抽象库

<a id="introduction"></a>

## 简介
Microsoft Orleans 核心抽象库是 Orleans 的基础库，包含实现 Grain 及客户端代码所用的公共编程 API。此包定义构成 Orleans 编程模型的核心抽象，包括 Grain 接口、Grain 引用接口及特性。

<a id="getting-started"></a>

## 入门
要使用此包，请通过 NuGet 安装：

```shell
dotnet add package Microsoft.Orleans.Core.Abstractions
```

此包是客户端和 Silo（服务器）应用程序共同依赖的包；引用 Orleans SDK 或 Orleans 客户端/服务器元包时会自动包含它。

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

## 文档
有关更全面的文档，请参阅：
- [Microsoft Orleans 文档](https://dotnet.github.io/orleans/docs/)
- [开发 Grain](https://dotnet.github.io/orleans/docs/grains/)
- [Grain 引用](https://dotnet.github.io/orleans/docs/grains/grain-references/)

<a id="feedback--contributing"></a>

## 反馈与贡献
- 如果遇到问题或希望提供反馈，请[在 GitHub 上创建 Issue](https://github.com/dotnet/orleans/issues)
- 加入我们的 [Discord](https://aka.ms/orleans-discord) 社区
- 关注 Twitter 上的 [@msftorleans](https://twitter.com/msftorleans) 账号，获取 Orleans 公告
- 欢迎贡献！请阅读我们的[贡献指南](https://github.com/dotnet/orleans/blob/main/CONTRIBUTING.md)
- 本项目采用 [MIT 许可证](https://github.com/dotnet/orleans/blob/main/LICENSE)