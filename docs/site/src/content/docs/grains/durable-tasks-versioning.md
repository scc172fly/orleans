---
title: "版本耐久任务"
description: "在等待处决期间,修改持久任务代码,每个执行分支都从记录版本中产生 DurableTask.GetVersion 处,并安全地淘汰旧版本。"
ms.date: 10/16/2026
ms.topic: how-to
---

<a id="version-durable-tasks"></a>

# 版本耐久任务

部署期间将恢复执行新守则的持久任务,但这一任务尚未完成:下一个
激活从开始再次运行身体, 每个被记录的操作都返回
结果被记录。 `DurableTask.GetVersion` 允许一个机构持有每个版本的代码
修改后,并给每个执行者以下版本:
早先的代码开始 继续运行, 而新的处决采取新的。

本页以 [重弹和确定论](durable-tasks.md#replay-and-determinism) 和
[任务身份和参考](durable-tasks-identity.md). 仅添加
在现有操作后操作操作,或添加新方法的操作,不需要版本;见
[安全版本工作流程](durable-tasks-operations.md#version-workflows-safely).

<a id="branch-on-a-changes-version"></a>

## 更改版本的分支

之后部署的作业记录簿上载有承运人
之后的部署也通知客户。
检查显示代码支持的版本:

:::code source="../snippets/compiled-durable/Grains/DurableTaskVersioningSnippets.cs" id="durable_tasks_versioning_branch" language="csharp":::

执行执行中更改的第一次检查决定版本,记录在下面
执行时的 `$version:{name}`. 以后每检查一次执行的改变,
和每次重放,返回记录版本:

| 执行 执行 | 版本版本 |
| --- | --- |
| 第一次达到支票:新执行,或在此点之前停止的早期运行 | `maxSupported` |
| 未经检查, 过此点时在代码中输入 | `0`,修改前的代码 |
| 录音记录了版本 | 记录版本 |
| 在同一 grain 上附有执行的一步,记录了一个版本 | 随函附上的处决版本 |

重放识别执行时, 执行时已经从历史中跳过检查: 重放
检查到达时,前一个运行记录中的操作仍会提前完成。
将检查置于修改添加、删除或更改的第一个操作之前,这样
每一个执行者都与它所期望的行动 一起到达它那里

- 循环中的检查,或者在多个助手中,每次返回第一个决定。
- 记录以 `DurableTask.RunInline`形式与处决的下一个耐久写作一同保存
  和在写之前结束的重放,从同一
  历史。
- 版本决定的范围范围被扩大到援引任务执行树:呼叫另一树
  grain 自行决定其版本。
- 决定适用于记录该决定的执行和其下台步骤。
  其父/母在记录自己的决定之前检查变更的一步,该步骤
  父母双方没有约束性:父母后来的检查决定了父母的历史。
- 一项任务, 要求取消该任务时被请求取消的任务, 当该任务发生时重放请求
  身体首先观察了它, 所以身体就选择了赔偿路径 早期运行
  一张只算补偿的支票,没有记录过
  发现前一个运行期之前记录的操作,并决定 `0`。
  机构分支之前的更改,例如,在机构开始时,
  当补偿取决于版本时。
- 激活调度器上的检查运行,与 `DurableTask.RunInline` 一样。
  持久任务,返回 `maxSupported`。
- 名字遵循操作名称规则:它们不是空的,也不包含 `$`。
  [ORLEANS0218](../diagnostics/orleans0218.md) 报告名称和范围,但并非
  常数。

<a id="evolve-a-change"></a>

### 改变

每次部署变更都产生 `maxSupported` ,并增加一个分支:

1. 第一次部署检查 `GetVersion("carrier-booking", 0, 1)`:第一版a
   载体和0版保存仓库
2. 下一次部署检查 `GetVersion("carrier-booking", 0, 2)`:版本2添加
   以及记录第一版的处决 总是不通知就订票

身体其他地方的一处无关的变化, 就会得到自己的名字。 给每个版本的新版本
使用新名字,所以他们的记录永远无法与另一版本的记录相匹配。

<a id="declare-a-change-once"></a>

### 一次宣布变更

`DurableVersion` 在一个声明中持有更改的名称和支持范围,作为
`DurableSignal<T>` 持有信号的名称。每次检查更改,都会写相同
范围, 添加或退出版本的部署编辑声明:

:::code source="../snippets/compiled-durable/Grains/DurableTaskVersioningSnippets.cs" id="durable_tasks_versioning_descriptor" language="csharp":::

`Get()` 返回执行的版本, `IsAtLeast(version)` 报告是否
执行后继 `version` 或新。

<a id="retire-a-version"></a>

## 将版本用完

在收到未执行的处决后,将一个版本退役:

1. 每一处决以其决定为
   `$version:{name}` 记录,其答复为版本和 `VersionRecorded`
   记录下来的每项决定都报告;见 [监视器版本](#monitor-versions)。
2. 将 `minSupported` 提升到版本以上,并删除其分支:

   :::code source="../snippets/compiled-durable/Grains/DurableTaskVersioningSnippets.cs" id="durable_tasks_versioning_retired" language="csharp":::

3. 一旦 `minSupported` 等于 `maxSupported` ,每执行一次待执行之后
   版本,删除检查。 处决保存的记录 留在他们的树上
   直到树被删除,重播时跳过它们。

稍后更改后给一个新名称, 而不是重复使用退休的名称: 执行时运行的处决
之前的退休支票,在被添加前没有记录,
旧名称将决定版本 `0` 。

<a id="stalled-executions"></a>

## 处决总数

当运行中的代码无法重放时执行拖挂。 运行时间取消
以两个理由之一试图,因为它取消了停止引爆的尝试;
见见
[任务取消和任务取消](durable-tasks-structured-concurrency.md#task-cancellation-and-attempt-cancellation):

| 原因理由原因 | 原因原因原因 | 内内例外 |
| --- | --- | --- |
| `UnsupportedVersion` | 执行记录了运行代码所支持范围以外的版本。 | A `DurableTaskVersionException`,其中列出更改、记录版本和所支持的范围 |
| `Diverged` | 重播请求在已记录的ID下进行另一次操作,记录的结果不再编码,任务存储的请求不再编码,或者身体在较早运行时观察到任务取消之前结束。见 [诊断性重显示差异](durable-tasks-operations.md#diagnose-replay-divergence)。 | 标明所记录和要求操作、编码失败或未到达的取消点的 `InvalidOperationException` |

运行时间 :

- 将找到摊位的操作丢弃 `DurableTaskAttemptCanceledException` ,
  尝试的后期耐久操作也同样
  取消, 并且取消 `AttemptCancellationToken`
  ,尝试通过它的 `finally` 区块来解开。
- 丢弃尝试的结果, 无论身体做什么 除了例外。
- 任务还在等待,等待的任务在等待
- 记录警告和升起, `ExecutionStalled` 随带 `Reason` 和
  排 `Exception`.
- 保持唤醒 grain 间隔 `DurableTaskOptions.StalledExecutionRetryInterval`,
  默认为一分钟, 所以稍后激活后重播放任务, 例如在 silos
  下次部署的装备

版本的停顿发生于部署增加 `minSupported` 而处决随后
的代码,或者当支持较少版本的代码重播执行时
安装支持记录版本的代码,或支持
执行过程不一, 执行恢复; 或者中止任务,
见 [中止已挂停的任务](durable-tasks-operations.md#abort-stuck-tasks)。

<a id="rolling-deployments"></a>

### 滚滚部署

在滚动部署期间, grain 可在前一个运行的 silo 上激活
代码码,它阻止了死刑处决 记录的新版本比它的
`maxSupported`,并且不恢复其中任何一项直到一个有较新代码的 silo 激活
grain. 更改第一次检查前的代码没有检查, 所以它会继续每个
包括处决, 新的代码开始。

排排旧处决先行。 让一个部署, 增加改变的第一次检查, 或者提高
`maxSupported`,在工作取决于新版本之前完成每个 silo ,然后让
而在后来部署之前,
版本。

<a id="monitor-versions"></a>

## 监视器版本

下 `DurableTaskEvents` 听众报告每个记录的决定和每个摊位。
监控计数决定显示哪些版本的处决仍然记录,以及日志
空格:

:::code source="../snippets/compiled-durable/Grains/DurableTaskVersioningSnippets.cs" id="durable_tasks_versioning_monitor" language="csharp":::

`VersionRecorded` 报告执行、更改的名称和版本: `0`
先前的代码执行时经过了检查。 `ExecutionStalled` 报告被停止
执行, 和 `Reason`, `UnsupportedVersion` 或 `Diverged`和 `Exception` 减去
描述摊位。

<a id="choose-versions-or-new-methods"></a>

## 选择版本或新方法

- **版本已到位** ,用于在机构内部进行变更,例如替换的步数、变化
  或附加通知,但处决尚待执行。
- **添加新工作流程形状的方法** 。与新方法一起安排新的工作,以及
  保留旧方法,直到完成待决任务。
- 启动一个源**的** ,用于一个无限期运行的工作流程:每代目的和
  以独立任务(如 `subscription-{id}-{generation}`)和
  新一代的代码从开始时的当前代码开始。
