---
title: "Orleans 分析器诊断"
description: "Orleans 汇编器分析器诊断识别码、影响、修正、代码修正和抑制指导的参考。"
ms.date: 10/19/2026
ms.topic: reference
---

<a id="orleans-analyzer-diagnostics"></a>

# Orleans 分析器诊断

Orleans 分析器检测无支持的 RPC 合同、序列化计划问题、不安全 grain 执行模式、在耐久任务中重现危险以及兼容性变化。选择一个诊断ID,用于其原因、影响、补救、代码化行为和抑制指导。

分析器帮助链接使用诊断编号作为 URL 碎片 `https://aka.ms/orleans/diagnostics` 。因此,单重定向可以保持稳定,而该索引和详细页则在变化中。

## ORLEANS0001

[将 AlwaysInterleave 放在 grain 接口上](orleans0001.md) -错误。 `[AlwaysInterleave]` 适用于执行方法。

## ORLEANS0002

[不允许引用参数修改器](orleans0002.md) ——错误。远程接口方法使用 `ref`、 `out`或 `in`。

## ORLEANS0003

[从 Grain继承继承](orleans0003.md) -删除。当前 Orleans 版本支持 POCO grain 类。

## ORLEANS0004

[添加缺失的序列化属性](orleans0004.md) ——错误。生成的序列器类型有一个无注释的成员。

## ORLEANS0005

[添加 GenerateSerializer](orleans0005.md) - 信息。 A `[Serializable]` 类型类型不选择 Orleans 源生成序列化。

## ORLEANS0006

[不能将静态或抽象成员序列化](orleans0006.md) -错误。为无效成员指定了 Orleans 序列化标识。

## ORLEANS0007

[使用一个 Orleans 激活建构器](orleans0007.md) ——错误。构造选择元数据模糊不清或无效。

## ORLEANS0008

[Grain 接口无法包含属性](orleans0008.md) -错误。 Orleans 远程界面会宣布实例属性。

## ORLEANS0009

[使用已注册的 grain-调用返回类型](orleans0009.md) -错误。 Orleans 无法将远程方法返回类型映射成可引用的请求。

## ORLEANS0010

[添加一个稳定的别名](orleans0010.md) ——信息。一种类型或RPC方法使用姓名衍生身份。

## ORLEANS0011

[重命名重复的别名](orleans0011.md) ——错误。别名的类型或方法相撞。

## ORLEANS0012

[更改重复的序列编号d](orleans0012.md) ——错误。一个序列型的成员重新使用字段编号。

## ORLEANS0013

[从 grain 类中删除序列化身份属性](orleans0013.md) -错误。 grain 执行标记数据序列化。

## ORLEANS0014

[保存 grain 执行上下文](orleans0014.md) -警告。 `ConfigureAwait` 可在 grain 排程器之外移动续延 。

## ORLEANS0016

[Grain 界面未在 OrleansContracts.txt](orleans0016.md) -警告。在舱单中缺少或留存一个 grain 接口。

## ORLEANS0017

[Grain 接口版本错配](orleans0017.md) ——警告。来源和清单版本各有不同。

## ORLEANS0018

[Grain 接口成员未申报](orleans0018.md) ——警告。清单中没有RPC的签名。

## ORLEANS0019

[删除 grain 接口未退出](orleans0019.md) -警告。源代码中不再存在活动表单接口 。

## ORLEANS0020

[OrleansContracts.txt 丢失](orleans0020.md) ——信息。合同分析在没有舱单的情况下进行。

## ORLEANS0021

[复制 grain 接口声明](orleans0021.md) -警告。清单重复接口名称或身份。

## ORLEANS0022

[Grain 类在 OrleansContracts.txt中不活动](orleans0022.md) -警告。 grain 类中缺少或已退出。

## ORLEANS0023

[Grain 类身份错配](orleans0023.md) - 警告:来源和清单 grain 类型不同。

## ORLEANS0024

[删除的 grain 类没有被留存](orleans0024.md) -警告。源代码中不再存在活动状态的 舱单 grain 类。

## ORLEANS0025

[重复 grain 类声明](orleans0025.md) -警告。清单重复了 grain 类名称或身份。

## ORLEANS0026

[无效的可引用的基型基本类型映射](orleans0026.md) -错误。自定义 grain-调用返回型号映射无法产生有效的可援引请求 。

## ORLEANS0027

[从源中移除的Grain 接口成员](orleans0027.md) ——警告。清单保留了RPC的签名,但来源没有这种签名。

## ORLEANS0028

[无效的 Orleans 身份](orleans0028.md) -错误。别名 grain 类型或 grain 接口类型违反了身份语法。

## ORLEANS0200

[记录效果或最后一次耐用手术后获取 grain 状态](orleans0200.md) - 警告:持久任务代码读或修改 grain 状态,重放再次运行。

## ORLEANS0201

[产生记录效应的非确定值](orleans0201.md) -警告。耐久任务代码产生一个时钟、随机或环境值,当重播再次运行时。

## ORLEANS0202

[分阶段或最后持久行动结束后等待外部业务](orleans0202.md) -警告。持久任务代码等待服务或其他外部工作,如果重播再次运行的话。

## ORLEANS0203

[更改 grain 状态, 仅于最后外部等待的](orleans0203.md) ——警告。在外部等待之前,一步或身体尾部的改变 grain 状态,以便进行改变而不产生结果。

## ORLEANS0204

[返回 DurableTask.RunInline 效果的输出](orleans0204.md) -警告。一个 `DurableTask.RunInline` 效果指定了正文的变量,而重播则不指定该变量。

## ORLEANS0205

[等待、时间安排或张贴持久任务定义](orleans0205.md) -错误。持久任务代码创建了一个耐久的任务定义, 从未运行它 。

## ORLEANS0206

[运行每次可持久任务定义一次](orleans0206.md) -警告。持久任务代码不止一次使用单一用途的持久任务定义。

## ORLEANS0207

[使用不使用“$”的耐久操作名称](orleans0207.md) -错误。一个 `WithId` 或 `DurableTask.RunInline` 名称含有 `$`,运行时拒绝。

## ORLEANS0208

[给每个持久作业呼叫站点自己命名](orleans0208.md) 警告:一个机构的两个呼叫站点同样地列出其持久作业,因此其识别资料取决于顺序。

## ORLEANS0209

[使持久任务机构保持排期](orleans0209.md) -警告。 耐久任务代码从激活的调度器开始线条、计时器或线状资源库工作。

## ORLEANS0210

[通过耐久组合运行并行的持久工作](orleans0210.md) -警告。耐久任务代码将耐久工作与 `Task` 组合器而不是耐久组合器结合起来。

## ORLEANS0211

[等待时间拖久](orleans0211.md) -警告。耐久任务代码等待重播重播的 `Task.Delay` 或 `Thread.Sleep`。

## ORLEANS0212

[由 IsTaskCancellationRequested过滤器取消处理器](orleans0212.md) ——警告。持久任务代码吊销捕获,但并未说明取消尝试取消的任务。

## ORLEANS0213

[将一项任务从非同步代码返回到长期任务机构](orleans0213.md) ——警告。一个持久任务机构含有 `async void` 代码,但该机构无法观察到。

## ORLEANS0214

[为每次重复等待绘制一个信号名](orleans0214.md) -Info. 耐久任务代码等待一个循环中的信号名, 所以每个等待返回第一个值 。

## ORLEANS0215

[通过重放安全日志记录器登录](orleans0215.md) -Info. 通过一个重播时再次登录的日志记录器进行持久任务代码日志。

## ORLEANS0216

[长期任务 grains 或长期任务机构](orleans0216.md) ——警告。 grains 之外和持久任务机构设置的代码是长期的任务,可以抛出。

## ORLEANS0217

[通行证通过 AttemptCancellationToken 进行任务取消清理](orleans0217.md) -警告。任务取消后清理已被取消的任务取消时的通行证 `CancellationToken`,因此其通话即告结束。

## ORLEANS0218

[通过常数版本名称和范围](orleans0218.md) - 警告。版本检查计算其更改名称或支持的运行时范围。

## ORLEANS0219

[等待、合并或归还在册儿童的手柄](orleans0219.md) ——信息。一个持久任务机构安排一名随身儿童,从不看管其处理器。

## ORLEANS0220

[列出职位可能改变的持久作业](orleans0220.md) -Info. 一个未知的持久操作在循环、状态或另一个未命名的操作之后运行,因此其生成的身份识别符可以移动。

## ORLEANS0221

[运行与 DurableTask.RunInline同步的工作](orleans0221.md) ——信息。一个 `DurableTask.Run` 步只运行同步的工作,其效果记录没有被处决的儿童。

## ORLEANS0222

[代表通过通行证,他们返回了一项不同步工作的任务](orleans0222.md) ——错误。 `DurableTask.Run` 或 `DurableTask.RunInline` 接待一名 `async void` 代表,该代表的完成记录在其工作结束前。

## ORLEANS0223

[将新值指定到 grain 状态,而不是将其修改](orleans0223.md) -警告。持久任务代码更改了 grain 状态持有的对象,日记记录没有变化。

## ORLEANS0225

[等待长期任务,而不是在持久任务机构投票](orleans0225.md) ——错误。一个持久的任务机构对预定的任务进行民意测验,然后投出 `NotSupportedException`。

## ORLEANS0226

[继续在其日程安排上设立持久任务机构](orleans0226.md) -错误。一个耐久任务机构等待 `ConfigureAwait(false)`,它从 grain的排程器上恢复工作。

## ORLEANS0227

[在 DurableTask.RunInline 效果之外运行耐久操作](orleans0227.md) -错误。一个 `DurableTask.RunInline` 效果开始任务或检查一个版本,然后扔出 `InvalidOperationException`。

## ORLEANS0228

[分离耐久 grain 调用](orleans0228.md) -错误。 耐久任务代码会切除一个步骤、一个 `async DurableTask` 方法或组合器,它们只作为开始执行的一部分运行。
