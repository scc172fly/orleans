<a id="sequential-messaging-throughput"></a>

# 顺序消息传递吞吐量

`SequentialMessagingBenchmark` 让一条消息链在**同一进程内 Silo 上的 2、4 或 8 个
不可重入 Grain 之间循环**。每个处理程序执行一次日志化的
计数器递增操作，暂存下一跳的消息信封，并同步调用 `Complete()`。
日志确认后，下一跳的传出意图即可释放并执行。

每次调用执行 **1,024 个已投递并确认的处理步骤**。
日志钩子观察每个不同消息跳的实际成功确认。
驱动程序等待全部 1,024 次确认；清理阶段验证业务效果的总次数及
各 Grain 的次数完全符合预期。测量路径包括消息信封构造
及普通应用程序序列化、调度、收件箱接受消息、处理程序执行、日志写入、
派发、接收方本地命令去重以及完成观察器。不包括集群
启动、激活、环形拓扑配置、预热、验证查询和关闭。

基准测试使用生产环境的消息传递和 Durable Jobs 实现，搭配
易失性日志存储和内存作业存储。它测量框架在
低延迟本地存储条件下的单链吞吐量。持久化提供程序延迟、
跨 Silo 网络通信以及并发独立消息链是分别进行比较的
维度。

已确认的意图和已完成的远程投递会立即唤醒激活实例的消息泵。
Durable Jobs 提供恢复和重试截止时间：只有 Grain 未在消息泵的一秒复查延迟内
安排到期唤醒时，写入才会安排一次唤醒，
因此繁忙消息链大约每次从空闲转为繁忙时执行一次 Durable Jobs 操作，
而不是每条消息执行一次。内存作业存储使这些操作的开销很低，
所以应与持久化作业存储比较以测量其成本。基准测试使用
易失性存储的默认快照限制：**100 次追加或 1 MiB**，以先达到者
为准。比较不同构建版本时应记录存储设置。

迭代初始化会重新创建集群并预热一条完整的消息链。每次迭代
包含一次纳入测量的调用，使日志及去重历史保持有界，
且具有可比性。基准测试采用真实时间和正常的消息泵。BDN 控制
迭代重复次数，不使用轮询或手动驱动的消息泵。

<a id="opaque-encoding-and-memory"></a>

<a id="typed-subjects-command-identity-and-memory"></a>

## 类型化主题、命令标识与内存

派发器使用 `benchmarks.sequential-hop.v1` 对应的键控
`MessageType<SequentialMessage>` 绑定对 `SequentialMessage` 解码。
该注册项将 Grain 保存为状态，并调用静态同步
委托。类型化发件箱的 `Send` 在更新业务
计数器之前编码并暂存下一跳消息。每个信封包含命令 ID、编码后的有效载荷、主题
及可选的发送方标头。传出消息另外指定
接收方；传入消息根据接收消息的 Grain 确定接收方。
正常的收件箱与发件箱投递经过
生产环境管道，日志钩子观察每次实际确认。

每次调用仅分配一次运行 ID。消息跳的 ID 采用固定深度的
`runs/{run-id}/hops/{hop-number}` 键；数值部分使用区域性无关的格式。
全部 1,024 跳都有不同的命令 ID，重试保留同一个消息跳 ID。
下一跳根据运行 ID 和跳编号构造其键，而不是延长父级
键链。预热调用使用自己的运行 ID。

类型化编码为每一跳准备有效载荷数组和发送方标头数组。
发件箱字典、处理程序上下文和准入操作会在各自工作进行期间保留托管
引用。有效载荷内容在整个重试过程中保持不变。
普通序列化保留原始字节，并在解码边界重建
托管数组。

应分别为有效载荷/标头分配及仍然存活的待处理编码字节，和
日志存储、序列化缓冲区及应用程序状态制定内存预算。测量
包含每跳的数组分配和 GC 开销；存活内存预算包含
状态、处理程序、准入和投递生命周期相互重叠所产生的占用。

在同一台机器上使用相同工作负载与保留设置比较不同修订版本。

<a id="run"></a>

## 运行

从仓库根目录执行：

```powershell
dotnet build test\Benchmarks\Benchmarks.csproj --configuration Release --framework net11.0
dotnet run --project test\Benchmarks\Benchmarks.csproj --configuration Release --framework net11.0 --no-build -- Messaging.Sequential --filter "*SequentialMessagingBenchmark*" --job Dry --buildTimeout 600 --noOverwrite
dotnet run --project test\Benchmarks\Benchmarks.csproj --configuration Release --framework net11.0 --no-build -- Messaging.Sequential --filter "*SequentialMessagingBenchmark*" --buildTimeout 600 --noOverwrite
```

构建超时涵盖 BDN 对基准测试项目完整
依赖关系图（包括 Dashboard 前端）的隔离重新构建。构建
和初始化完成后才开始测量。

开发阶段测量使用 `--job Short`。读取 BDN 的 `*-report-github.md`，
在相同机器和运行时下比较各 Grain 数量对应的行。`OperationsPerInvoke`
将 `Mean` 归一化为一条已确认消息的平均耗时：**每秒消息数 = 1 / 以秒为单位的
Mean**。将 `Mean` 乘以 1,024 即得到整条链的耗时。Dry 模式的输出用于验证
执行，不纳入性能结论。

添加 `--memory` 以测量每条已确认消息的托管内存分配。比较时使用
相同的 Grain 数量、消息链长度、作业预设和快照阈值。
保留状态的预算包括命令完成历史、应用程序状态，
以及两次快照之间有界的追加历史。

比较时保持调用长度固定：预热链也会产生
后续快照所捕获的保留状态。

记录测量对应的提交、运行时、硬件、存储配置及作业预设。
由于每次迭代的初始化和清理都有固定成本，默认
运行的总耗时长于报告的测量耗时。
