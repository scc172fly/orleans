<a id="sequential-messaging-throughput"></a>

# 序列通信传输流量

`SequentialMessagingBenchmark` 通过 **2、4或8, 传递一个信息链
non-reentrant grains 在一个在过程内 silo**。每个处理者都使用一份日记
反增量,分步下一个信封,同步调用 `Complete()` 。
日记确认释放出下一跳的意向

每个职业都履行 **1 024个交付和确认的处理器步骤**。
观察每个不同跳跃的实际成功确认。
司机等待所有1 024份确认书;清理核实准确的总数
-grain 商业效果计数。测量路径包括信封构造
以及普通应用程序的序列化、时间安排、收件箱接收、处理员执行、日记
发送、接收器-当地指令重复和完成观察员。
启动、激活、环形配置、暖气、验证查询和关闭。

基准利用生产信息发布和可持久就业实施
运行框架
长期提供商的延迟,
跨silo 联网,同时独立链路是独立的单独比较
轴。

主动启动泵的启动后,确认意向和完成远程交付
立即恢复和重新试用最后期限:
仅在以下时间才醒起来 grain 泵的一秒重校延迟内没有
如此繁忙的连锁链在无所事事的到大忙人过渡期间 完成一个“持久工作”作业
而不是每个信息一个。 在模拟工作存储中,这些操作成本低廉,所以
对照持久性工作储存比较,以衡量成本。
挥发性存储默认快手限制: **100个附件或1 MiB**,以
。 在比较构建时记录存储设置 。

迭代设置重建聚集,温暖整个链条。
包含一个测量的引用, 保持日记和重复历史
基准使用实时和正常泵。
迭代重复;没有投票或人工驱动泵。

<a id="opaque-encoding-and-memory"></a>

<a id="typed-subjects-command-identity-and-memory"></a>

## 输入对象、命令身份和内存

使用密钥解码器的发件人解码 `SequentialMessage`
`MessageType<SequentialMessage>` 具有 `benchmarks.sequential-hop.v1`.
其注册登记将 grain 作为州储存,并援引了静态同步
。在业务前输入发件箱 `Send` 编码并分期进行下一跳
每个信封都包含一个指令编号 编码有效载荷,主体
和可选发件人头。
接收者; 收到的信息来自接收方 grain。
普通箱/发箱交付
并观察每一件实际确认。

每个职业都指定一次运行的ID。
`runs/{run-id}/hops/{hop-number}` 键; 数字组件使用变量格式。
所有的1 024个跳跃都有不同的命令身份, 重试保留相同的跳动身份。
下一跳跳从运行和跳而不是扩展父键构建它的密钥
。 热身的援引使用自己的运行身份识别码。

输入编码为每次跳动准备有效载荷和发件人信头阵列。
管理发箱字典、处理人背景和接收操作
工作时的引用。
普通序列保留原字节并重建
在解码边界处管理数组。

预算有效载荷/头目分配额,待用已编码的字节与
日记存储、序列缓冲器和应用状态。
包括每霍阵阵阵阵阵阵阵分配和GC成本;
状态、处理器、入场和交付寿命。

将在同一台机器上的修订与工作量和保留设置相匹配进行比较。

<a id="run"></a>

## 运行运行

从仓库根 :

```powershell
dotnet build test\Benchmarks\Benchmarks.csproj --configuration Release --framework net11.0
dotnet run --project test\Benchmarks\Benchmarks.csproj --configuration Release --framework net11.0 --no-build -- Messaging.Sequential --filter "*SequentialMessagingBenchmark*" --job Dry --buildTimeout 600 --noOverwrite
dotnet run --project test\Benchmarks\Benchmarks.csproj --configuration Release --framework net11.0 --no-build -- Messaging.Sequential --filter "*SequentialMessagingBenchmark*" --buildTimeout 600 --noOverwrite
```

建设超时覆盖BDN孤立重建基准项目完成后的建筑超时
包括 Dashboard 前端的软性图, 包括 Dashboard 前端。 度量从构建后开始
并设置完成。

使用 `--job Short` 阅读 BDN 的 `*-report-github.md`
并比较同一机器/运行时的 grain-计数行。 `OperationsPerInvoke`
将 `Mean` 与一个确认的消息( **信件/第二=1/平均值)对齐
秒数秒**乘法 `Mean` 链长1 024x1 024。干输出验证
并且不执行,不列入业绩结论。

添加 `--memory` 以衡量每个已确认信息的管理下分配额。比较
相同的 grain 计数、链长、工作预设和快速阈值。
保留的国家预算中包括指挥完成历史、应用状态、
和被绑定的附加历史 在相片之间。

保持固定的工时长度,以进行比较:温暖链有助于
保留状态,以事后的图片记录。

记录承诺、运行时间、硬件、存储配置和工作预设
由于设置和清理每个迭代都有固定成本,默认
运行所需时间比报告的测量时间长。
