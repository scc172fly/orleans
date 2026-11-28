---
title: Orleans 分析器诊断
description: Orleans 编译器分析器诊断 ID、影响、修复方法、代码修复及抑制指南的参考。
ms.date: 10/19/2026
ms.topic: reference
---

<a id="orleans-analyzer-diagnostics"></a>

# Orleans 分析器诊断

Orleans 分析器可检测不受支持的 RPC 契约、序列化架构问题、不安全的 Grain 执行模式、持久化任务中的重放风险以及兼容性变更。选择诊断 ID，了解其原因、影响、修复方法、代码修复行为和抑制指南。

分析器帮助链接使用 `https://aka.ms/orleans/diagnostics`，并将诊断 ID 作为 URL 片段。因此，在此索引和详细页面不断演进时，只需维护一个稳定的重定向地址。

## ORLEANS0001

[将 AlwaysInterleave 放在 Grain 接口上](orleans0001.md) — 错误。`[AlwaysInterleave]` 被应用于实现方法。

## ORLEANS0002

[不允许使用引用参数修饰符](orleans0002.md) — 错误。远程接口方法使用 `ref`、`out` 或 `in`。

## ORLEANS0003

[继承 Grain（已移除）](orleans0003.md) — 已移除。当前 Orleans 版本支持 POCO Grain 类。

## ORLEANS0004

[添加缺失的序列化特性](orleans0004.md) — 错误。使用生成式序列化器的类型中存在未标注特性的成员。

## ORLEANS0005

[添加 GenerateSerializer](orleans0005.md) — 信息。`[Serializable]` 类型未启用 Orleans 源生成序列化。

## ORLEANS0006

[静态或抽象成员不能序列化](orleans0006.md) — 错误。无效成员被分配了 Orleans 序列化 ID。

## ORLEANS0007

[使用单个 Orleans 激活构造函数](orleans0007.md) — 错误。构造函数选择元数据存在歧义或无效。

## ORLEANS0008

[Grain 接口不能包含属性](orleans0008.md) — 错误。Orleans 远程接口声明了实例属性。

## ORLEANS0009

[使用已注册的 Grain 调用返回类型](orleans0009.md) — 错误。Orleans 无法将远程方法的返回类型映射为可调用请求。

## ORLEANS0010

[添加稳定的 Alias](orleans0010.md) — 信息。类型或 RPC 方法使用由名称派生的标识。

## ORLEANS0011

[重命名重复的 Alias](orleans0011.md) — 错误。类型或方法的别名发生冲突。

## ORLEANS0012

[修改重复的序列化 Id](orleans0012.md) — 错误。同一序列化类型的成员重复使用字段 ID。

## ORLEANS0013

[从 Grain 类中移除序列化标识特性](orleans0013.md) — 错误。Grain 实现被标记为数据序列化类型。

## ORLEANS0014

[保留 Grain 执行上下文](orleans0014.md) — 警告。`ConfigureAwait` 可能使延续操作离开 Grain 调度器。

## ORLEANS0016

[Grain 接口在 OrleansContracts.txt 中未处于活动状态](orleans0016.md) — 警告。清单中缺少某个 Grain 接口，或该接口已被停用。

## ORLEANS0017

[Grain 接口版本不匹配](orleans0017.md) — 警告。源码与清单中的版本不同。

## ORLEANS0018

[Grain 接口成员未声明](orleans0018.md) — 警告。清单中缺少某个 RPC 签名。

## ORLEANS0019

[已移除的 Grain 接口尚未标记为停用](orleans0019.md) — 警告。清单中仍处于活动状态的接口已不再存在于源码中。

## ORLEANS0020

[缺少 OrleansContracts.txt](orleans0020.md) — 信息。已启用契约分析，但缺少清单。

## ORLEANS0021

[重复的 Grain 接口声明](orleans0021.md) — 警告。清单中重复声明了接口名称或标识。

## ORLEANS0022

[Grain 类在 OrleansContracts.txt 中未处于活动状态](orleans0022.md) — 警告。清单中缺少某个 Grain 类，或该类已被停用。

## ORLEANS0023

[Grain 类标识不匹配](orleans0023.md) — 警告。源码与清单中的 Grain 类型标识不同。

## ORLEANS0024

[已移除的 Grain 类尚未标记为停用](orleans0024.md) — 警告。清单中仍处于活动状态的 Grain 类已不再存在于源码中。

## ORLEANS0025

[重复的 Grain 类声明](orleans0025.md) — 警告。清单中重复声明了 Grain 类名称或标识。

## ORLEANS0026

[无效的可调用对象基类型映射](orleans0026.md) — 错误。自定义 Grain 调用返回类型映射无法生成有效的可调用请求。

## ORLEANS0027

[Grain 接口成员已从源码移除](orleans0027.md) — 警告。清单仍保留源码中已不存在的 RPC 签名。

## ORLEANS0028

[无效的 Orleans 标识](orleans0028.md) — 错误。别名、Grain 类型或 Grain 接口类型违反标识语法。

## ORLEANS0200

[在已记录效果中或最后一次持久化操作之后访问 Grain 状态](orleans0200.md) — 警告。持久化任务代码在重放会再次运行的位置读取或修改 Grain 状态。

## ORLEANS0201

[在已记录效果中生成非确定性值](orleans0201.md) — 警告。持久化任务代码在重放会再次运行的位置获取时钟、随机值或环境值。

## ORLEANS0202

[在步骤中或最后一次持久化操作之后等待外部操作](orleans0202.md) — 警告。持久化任务代码在重放会再次运行的位置等待服务或其他外部工作。

## ORLEANS0203

[在最后一次外部等待之后修改 Grain 状态](orleans0203.md) — 警告。步骤或主体尾部在外部等待之前修改 Grain 状态，导致变更可能在结果尚未提交时先行提交。

## ORLEANS0204

[返回 DurableTask.RunInline 效果的输出](orleans0204.md) — 警告。`DurableTask.RunInline` 效果为主体变量赋值，而重放不会执行该赋值。

## ORLEANS0205

[等待、调度或投递持久化任务定义](orleans0205.md) — 错误。持久化任务代码创建持久化任务定义，却从未运行它。

## ORLEANS0206

[每个持久化任务定义只运行一次](orleans0206.md) — 警告。持久化任务代码多次运行只能使用一次的持久化任务定义。

## ORLEANS0207

[持久化操作名称不得包含 $](orleans0207.md) — 错误。`WithId` 或 `DurableTask.RunInline` 的名称包含 `$`，运行时会拒绝此名称。

## ORLEANS0208

[为每个持久化操作调用点赋予独有名称](orleans0208.md) — 警告。同一主体中两个调用位置为持久化操作使用相同名称，使标识符取决于顺序。

## ORLEANS0209

[使持久化任务主体始终在其调度器上执行](orleans0209.md) — 警告。持久化任务代码在激活实例的调度器之外启动线程、定时器或线程池工作。

## ORLEANS0210

[使用持久化组合器运行并发的持久化工作](orleans0210.md) — 警告。持久化任务代码使用 `Task` 组合器而非持久化组合器来组合持久化工作。

## ORLEANS0211

[使用持久化延迟进行等待](orleans0211.md) — 警告。持久化任务代码使用 `Task.Delay` 或 `Thread.Sleep` 等待，而重放会重新开始这些等待。

## ORLEANS0212

[使用 IsTaskCancellationRequested 筛选取消处理程序](orleans0212.md) — 警告。持久化任务代码捕获取消异常时未区分任务取消与执行尝试取消。

## ORLEANS0213

[持久化任务主体中的异步代码应返回任务](orleans0213.md) — 警告。持久化任务主体包含自身无法观察的 `async void` 代码。

## ORLEANS0214

[为每次重复等待派生信号名称](orleans0214.md) — 信息。持久化任务代码在循环中等待同一个信号名称，因此每次等待都返回第一个值。

## ORLEANS0215

[通过重放安全的日志记录器记录日志](orleans0215.md) — 信息。持久化任务代码通过会在重放时重复记录日志的日志记录器记录日志。

## ORLEANS0216

[从 Grain 或持久化任务主体投递持久化任务](orleans0216.md) — 警告。Grain 和持久化任务主体之外的代码投递持久化任务，此操作会抛出异常。

## ORLEANS0217

[在任务取消后的清理调用中传入 AttemptCancellationToken](orleans0217.md) — 警告。任务取消后的清理代码传入已因任务取消而被取消的 `CancellationToken`，因此调用会立即结束。

## ORLEANS0218

[传入常量版本名称和范围](orleans0218.md) — 警告。版本检查在运行时计算变更名称或支持范围。

## ORLEANS0219

[等待、组合或返回已调度子任务的句柄](orleans0219.md) — 信息。持久化任务主体调度附属子任务，却从未观察其句柄。

## ORLEANS0220

[为位置可能变化的持久化操作命名](orleans0220.md) — 信息。未命名的持久化操作在循环、条件分支或另一个未命名操作之后运行，因此生成的标识符可能发生偏移。

## ORLEANS0221

[使用 DurableTask.RunInline 运行同步工作](orleans0221.md) — 信息。`DurableTask.Run` 步骤仅运行同步工作，使用效果即可在不创建子执行实例的情况下记录该工作。

## ORLEANS0222

[为异步工作传入返回任务的委托](orleans0222.md) — 错误。`DurableTask.Run` 或 `DurableTask.RunInline` 接收 `async void` 委托，其完成状态会在实际工作完成前被记录。

## ORLEANS0223

[为 Grain 状态赋予新值，而不是就地修改](orleans0223.md) — 警告。持久化任务代码就地修改 Grain 状态所持有的对象，而日志不会记录该变更。

## ORLEANS0225

[在持久化任务主体中等待持久化任务，而不是轮询](orleans0225.md) — 错误。持久化任务主体轮询已调度任务，此操作会抛出 `NotSupportedException`。

## ORLEANS0226

[使持久化任务主体在其调度器上继续执行](orleans0226.md) — 错误。持久化任务主体使用 `ConfigureAwait(false)` 等待，使其在 Grain 调度器之外恢复执行。

## ORLEANS0227

[在 DurableTask.RunInline 效果之外运行持久化操作](orleans0227.md) — 错误。`DurableTask.RunInline` 效果启动任务或检查版本，此操作会抛出 `InvalidOperationException`。

## ORLEANS0228

[分离持久化 Grain 调用](orleans0228.md) — 错误。持久化任务代码分离步骤、`async DurableTask` 方法或组合器，而这些定义只能作为启动它们的执行实例的一部分运行。
