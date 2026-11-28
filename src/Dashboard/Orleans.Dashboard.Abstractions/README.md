<a id="microsoft-orleans-dashboard-core"></a>

# Microsoft Orleans Dashboard 核心库

<a id="introduction"></a>

## 简介
Microsoft Orleans Dashboard 核心库为 Orleans Dashboard 提供基础设施及数据采集服务。此包包含 Dashboard UI 所使用的核心 Grain 服务、指标采集功能和数据模型。

<a id="getting-started"></a>

## 入门
安装 `Microsoft.Orleans.Dashboard` 时通常会自动引用此包。除非构建自定义监控解决方案或扩展 Dashboard 功能，否则通常无需直接引用此包。

如需直接使用此包，请通过 NuGet 安装：

```shell
dotnet add package Microsoft.Orleans.Dashboard.Abstractions
```

<a id="whats-included"></a>

## 包含内容
此包提供：
- **指标采集服务**：基于 Grain 的运行时统计信息采集服务
- **数据模型**：表示 Silo 和 Grain 统计信息的共享类型
- **历史记录跟踪**：性能指标的时间序列数据存储
- **Grain 性能分析**：方法级性能跟踪基础设施

<a id="documentation"></a>

## 文档
有关更全面的文档，请参阅：
- [Microsoft Orleans 文档](https://dotnet.github.io/orleans/docs/)
- [Orleans 可观测性](https://dotnet.github.io/orleans/docs/host/monitoring/)
- [Orleans Dashboard 包](https://www.nuget.org/packages/Microsoft.Orleans.Dashboard/)

<a id="feedback--contributing"></a>

## 反馈与贡献
- 如果遇到问题或希望提供反馈，请[在 GitHub 上创建 Issue](https://github.com/dotnet/orleans/issues)
- 加入我们的 [Discord](https://aka.ms/orleans-discord) 社区
- 关注 Twitter 上的 [@msftorleans](https://twitter.com/msftorleans) 账号，获取 Orleans 公告
- 欢迎贡献！请阅读我们的[贡献指南](https://github.com/dotnet/orleans/blob/main/CONTRIBUTING.md)
- 本项目采用 [MIT 许可证](https://github.com/dotnet/orleans/blob/main/LICENSE)
