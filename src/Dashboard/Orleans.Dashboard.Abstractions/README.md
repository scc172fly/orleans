<a id="microsoft-orleans-dashboard-core"></a>

# Microsoft Orleans 键盘核心

<a id="introduction"></a>

## 简介
Microsoft Orleans Dashboard Core提供 Orleans Dashboard的基础基础设施和数据收集服务。该软件包包含核心 grain 服务、指标收集和仪表盘界面使用的数据模型。

<a id="getting-started"></a>

## 入门
安装 `Microsoft.Orleans.Dashboard`时,通常会自动引用此软件包。一般不需要直接引用该软件包,除非您正在建立自定义监测解决方案或扩展仪表板功能。

要直接使用此软件包, 请通过 NuGet安装 :

```shell
dotnet add package Microsoft.Orleans.Dashboard.Abstractions
```

<a id="whats-included"></a>

## 包含的内容
本包提供:
- **计量采集服务**: Grain-- 收集运行时统计数据的服务
- **数据模型**:代表 silo 和 grain 统计数据的共享类型
- **历史跟踪**:业绩指标的时间序列数据储存
- **Grain 预测**:方法级绩效跟踪基础设施

<a id="documentation"></a>

## 文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件
关于更全面的文件,请参见:
- [Microsoft Orleans 文档](https://dotnet.github.io/orleans/docs/)
- [Orleans 易观测](https://dotnet.github.io/orleans/docs/host/monitoring/)
- [Orleans 机套件](https://www.nuget.org/packages/Microsoft.Orleans.Dashboard/)

<a id="feedback--contributing"></a>

## 反馈和贡献
- 如果有任何问题或希望提供反馈,请 [GitHub上的开关](https://github.com/dotnet/orleans/issues)
- 加入我们社区 [不一致](https://aka.ms/orleans-discord)
- 跟随 [@msftorleans 地图上写着:](https://twitter.com/msftorleans) 推特账户发布 Orleans 通知
- 请审查我们的 [会费捐款准则](https://github.com/dotnet/orleans/blob/main/CONTRIBUTING.md)
- 该项目在 [麻省理工学院许可证](https://github.com/dotnet/orleans/blob/main/LICENSE)下获得许可证
