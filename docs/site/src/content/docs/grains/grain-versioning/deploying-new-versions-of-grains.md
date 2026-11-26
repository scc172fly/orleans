---
title: "安装新 grain 接口版本"
description: "在保持调用器兼容性的同时推出新的 Orleans grain 接口版本。"
ms.date: 08/02/2026
ms.topic: how-to
---

<a id="deploy-new-grain-interface-versions"></a>

# 安装新 grain 接口版本

安全滚动升级将版本路由与应用合同相结合,较新的实施程序可以满足更老的呼叫者。

<a id="prepare-the-contract"></a>

## 拟订合同

部署前:

1. 保持现有方法识别、参数含义、返回含义和序列有效载荷兼容。
1. 使用新方法或可选/可转换-可容有效载荷字段添加新的行为。
1. 使新的实施能够处理仍在部署的电话员发出的每一项请求。
1. 如果需要回滚, 则让两个版本的状态都可读取。
1. 测试混合调用器和 silo 版本,而不仅仅是单设的每个版本。

仅在定义了本合同之后才应用更高的 `[Version]` 值。数字声称路径兼容;它不会产生兼容性。

<a id="rolling-upgrade"></a>

## 滚滚升级

默认组合用于后向兼容的推出:

:::code language="csharp" source="../../snippets/compiled/Grains/RequestsAndVersioningSnippets.cs" id="configure_grain_versioning":::
然后:

1. 开始版本 2 silos ,而版本 1 silos 和调用者则保留。
1. 版本1请求可使用版本1或版本2启动。
1. 仅在足够多的版本 2 silos 完成后才开始第二版呼叫。
1. 版本 2 请求无法使用版本 1 激活。 如果它达到一个, Orleans 禁用会启动并设置兼容的版本 。
1. 排水系统1号呼叫器,然后是1号 silos。
1. 保留后向兼容合同,直到不再需要回退。

当新激活更喜欢最新的兼容实施时使用 <xref:Orleans.Versions.Selector.LatestVersion> 。 在分阶段验证期间, 当新激活应保持最低兼容版本时使用 <xref:Orleans.Versions.Selector.MinimumVersion> 。 战略中都不主动更新兼容的激活 。

<a id="rollback"></a>

## 返回回滚

运行回滚只有在旧代码能够读取新代码所写的国家和消息时才安全。 如果版本2写出不兼容的存储代表或释放不兼容的有效载荷, 停止版本 2 silos 无法恢复兼容性 。

计划数据演变和界面演变相结合:

- 先部署落后兼容的读者
- 延迟不可逆转地写到不再需要回退。
- 使用语义可以差异的操作和 schema 版本字段。
- 演练在混合版本测试组中回滚

<a id="observe-the-rollout"></a>

## 观察推出

监控不相容请求的禁用、按版本启动安装、不成功放置、序列处理失败和状态读取失败。 替换激活率上升可以表明与热 grain 身份共享不兼容的呼叫者或过于严格的策略。
