<a id="messaging-stock-reservations"></a>

# 通信:保留库存

一个自足 .NET 11 控制台主机运行一个本地主机 Orleans silo 和两个
grains. **命令** 在申请中提交已打印的保留
命令 ID; **库存** grain 发送了确定性标牌接受或拒绝。
东道方演习接受、库存不足和数量无效,每个指挥
处理过一次。
活性杂志和记忆中的工作使示范性刊物保持了自足。

<a id="run"></a>

## 运行运行

**与本地包装的 Orleans 源构建,直到这些API公布为止。**
申报版本遵循存储器样本的核定一揽子基准。
存储库从本地的种子中取代当前源包。

从仓库根 :

```powershell
pwsh .\samples\Build-Samples.ps1 -SkipExternalAssets
# Or run Validate-Samples.ps1 to perform policy checks and the complete build.
dotnet run --project .\samples\Messaging\Messaging.csproj --configuration Release --no-build
```

`Build-Samples.ps1` 将当前 Orleans 源填入独有版本和出入证
`OrleansSamplePackageVersion` 和 `OrleansSamplePackageSource` 样本
。样本中的 NuGet 参考文献用与
应用项目。

<a id="copying-the-sample-out"></a>

### 将样本复制出来

`Messaging/` 是整个[重印单位,包括其本身的
`Directory.Packages.props`一旦这些年度价格指数公布,将调整其 Orleans
版本到该释放和运行的软件包:

```powershell
dotnet run --project .\Messaging.csproj --configuration Release
```

在此之前,独立建筑有明确的未公布包件例外。
今天要测试一个副本, 使用第一个包 Orleans
`Build-Samples.ps1`,然后复制样本文件夹并替换 **仅替换副本**
中央中央 Orleans 本地种子生成版本的软件包版本 。
利用当地供养和正常的公共依赖性供养恢复/建设:

```powershell
dotnet restore .\Messaging.csproj --source <absolute-local-package-feed> --source https://api.nuget.org/v3/index.json
dotnet run --project .\Messaging.csproj --configuration Release --no-restore
```

使用稳定版本
服务器/ Sdk 和 Alpha 永久版本。 所有中继 Orleans
依赖性必须来自同一源构建的同一源代码。 存储库的 `samples/Directory.Build.targets` 超文本不包含
将 `OrleansSamplePackageVersion` 单传到副本上。
将不替换其已宣布的版本。

<a id="how-it-works"></a>

## 如何运作

1. 主机初始化了 `trail-shoes`的十个单位, 然后通过
   单顺序的发件箱,在独立的ID下: `orders/order-1042/reserve-stock`
   9个单位 `reserve-too-much` ,零个单位 `reserve-invalid-quantity` 。
2. `StockMessages` 绑定 `ReserveStock`、 `Restock`和抽象
   `ReservationOutcome` 将修改主题;两个 grains 都输入。键入发件箱
   `Send` 和 `SendReply` 对记录进行序列排序,并打入 `inbox.Register` 处理器
   接受解码。
3. 命令的`OrderGrain.ReserveAsync` 级,等待 `WriteStateAsync`;
   发件箱提供它 一旦写作是耐久的, 并重试在原始
   绝对交付预算。
4. 股盘处理程序计算本地计算结果, 然后发送回复
   `SendReply`,更新清单,并同步调用 `context.Complete()`
   复答、清册和收件箱填入完成承诺在一个日记中
   无效数量和短缺是商业拒绝:它们答复并
   状态未改变。 非正 `Restock` 没有回复协议, 所以它的处理者
   调来电话 `context.Fail(reason)`.
5. 回复的ID是命令 ID `result` 孩子。 该命令的处理器记录
   其日记收款和完成账项的结果, `WaitForOutcomeAsync`
   返回收到时间一旦持久,则返回。
   请求, 以便回复达到命令值, 而主机等待它 。
6. 库存 grain的收件箱保留每个完成命令的代号并接受
   `SentAt`. 重新交付,例如重新处理其答复已失败者,则收到
   `Duplicate` 记录保存时,应保留其预定的时间。
   记录删除后,一个未跟踪的旧发件人收到 `Expired` 在
   处理器运行。

成功运行的指纹 :

```text
ACKNOWLEDGED: orders/order-1042/reserve-stock/result, outcome=ReservationAccepted
ACKNOWLEDGED: orders/order-1042/reserve-too-much/result, outcome=ReservationRejected (InsufficientStock)
ACKNOWLEDGED: orders/order-1042/reserve-invalid-quantity/result, outcome=ReservationRejected (InvalidQuantity)
VERIFIED: remaining stock=8, reservations=1, processed requests=3.
```

东道主核对每个相关结果,并核实库存 grain 处理完毕
失败检查或两分钟的截止时间
使用非零退出代码的进程失败。只有一次本地主机样本
默认 silo 和网关端口应同时运行。
<a id="storage-and-production-boundaries"></a>

## 储存和生产边界

`UseInMemoryDurableJobs` 和 `AddVolatileJournalStorage` 保持此演示
他们的确认是用真实的日记、信息泵
和持久工作。 **停止这一进程会抛弃它的状况。** 配置固性
将整个进程重新开始恢复的日记账和工作供应商进行循环。
Orleans- 以 `orleans-binary` 日记格式的可序列记录。

接收器日记的入场层只预付,包括全天候
完成的记录可在
已经配置 `DeduplicationWindow` 从他们接受的发送时间 。
完成的重复确认保留
完成事实; 呼叫者获得业务结果
最初的可持久答复或质询真正的商业状态。
承诺框意图在其配置的重新试政策中提供该回复。
`Expired` 结果或发送者重新研究用尽的假期,其先前接受的时间不详。
批准新发送之前,先用原指令编号调和商业状态。
需要崩溃可恢复的终端动作的应用
`IOutboxMessageSource` 和成果的阶段以及自定后续行动
中 `OnRetired`中的电文,以原子法删除导出条目。

在生产订单服务中,房客的所有权来自信任的入侵,
授权命令 ID、主题和回复目的地,并定义保留
保存可查询的保留
使用外部效果
其供应商的应急与和解协议中 具有同样的卡尼卡身份
