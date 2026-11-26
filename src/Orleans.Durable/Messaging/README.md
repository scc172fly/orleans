<a id="microsoft-orleans-messaging"></a>

# Microsoft Orleans 通信

`Microsoft.Orleans.Durable` 中提供 grain- 范围宽耐久信箱和发件箱中的通信
以 Orleans 日记和持久工作为基础。 A grain 通过播放发送信息
发件箱中的发件框;该信息与 grain其他日记状态保持了持续状态,并且
发送到接收器的收件箱, 接收器至少处理一次并复制
发送到 grain 本身的信息会跳过发件箱: 它自己的信箱
在同一日记中接受它。

<a id="setup"></a>

## 设置设置设置设置

配置日记和持久工作存储,然后调用 `AddMessaging` 排在《公约》 silo
构建器或服务收藏。可选 `DurableMessagingOptions` 回调配置
能力、批量尺寸、批量大小、累进、重复和保留无字母;选项
启动时验证。 注册为 `AddMessageType<T>(subject)`的输入对象 。

A grain 通过解决 `IInbox`接收信息,并通过解决 `IOutbox`发出信息,
建筑或启动装置期间,通常通过建造者注射,所以每个
使用 grain日记本回收。解析 `IOutbox` 也启用了收件箱,该收件箱
接受 grain给它自己发来的邮件。交付品通过 grain到达收件箱
信息扩展发件。 拒绝不列颠工人 grains ; 重新输入和中继
grains 得到支持, grain的代码负责在调用时正确无误
中途休息。

<a id="messages"></a>

## 消息

`Envelope` 包含:

- `MessageId`:一个应用程序供应 `HierarchicalKey` 包裹在 `MessageId`, 时
  最大 1 024 UTF-8 字节。 它在接收器中指定一个命令 。
- `Payload`:不透明字节;空有效载荷有效。
- `Subject`:选择已键入的可选正则名称,最多为256 UTF-8字节,最多可选
  处理器
- `Headers`:可选命名的字节数组,带有无空白键和非核值。
- `DeliverAt`:在持久工作时钟上可选择的时间。接收者接受
  立即处理该信息, 一旦时钟到达 `DeliverAt`, 即进行处理。 消息
  与 `DeliverAt` 计数 `MaxCapacity` 从接受到完成。

`OutboxMessage` 对一个信封与接收器配对。 `InboxMessage` 随同带
接收器的逻辑发送时间 `SentAt` 。保持已公布的有效载荷
和页眉数组不变。

`MessageId` 以 `HierarchicalKey`创建 `new MessageId(key)` 或
`MessageId.Create(segments)`,然后通过 `Key`读取键。 `HierarchicalKey` 折叠
两者的平等与散列 比较了这条路
或以空格。使用 `HierarchicalKey.Create`、 `CreateChildKey`和 `Append` 来构建密钥,
和 `GetParent`, `IsParentOf`, `IsChildOf`, `IsAncestorOf`和段数计算器
来导航它们

<a id="handle-messages"></a>

## 处理信件

`IInbox.Register(IInboxHandler)` 添加一个处理器。 每一条消息都转到第一个
登记在册的处理器 `CanHandle(InboxMessage)` 返回 `true`选择的文字为
仅信件;所选处理器在 `HandleAsync`中完成。手动程序可以是
在任何时候登记;在施工或启动期间登记,使
可用以获取回信 。

`InboxExtensions.Register` 绑定了 `MessageType<T>` ,但需采用已打字的方法:

```csharp
inbox.Register(reserve, HandleReserve);
inbox.Register(reserve, this, static (request, grain, context) => grain.HandleReserve(request, context));
```

超载接受同步或非同步代表,不论有无 `TArg`
静态、无分配代表的论据。

处理器以 `IInboxHandlerContext.Complete()` 或
`Fail(reason)`:

- 信息清除和完成记录`Complete()` 的阶段。
- `Fail(reason)` 将信件移到收件箱中, 记录其身份为
  已处理。
- 处理者的例外,或返回时不打电话,结果不明。
  企图改变的改动 grain正在撰写 grain
  撤销这些装置并从日记上收回后,就将其抛弃。
- 启动后恢复,但未记录尝试结果,通过
  这样的失败或崩溃,对每个相应消息都要求尝试一次。
  以指数反向回溯重试,直到第一次反转后 `InboxRetryTimeout` (6小时)
  失败, 然后转到死信。 一个优雅的关闭日记账簿
  信件待发时停止停止, 所以不收费。 坠机可以收取消息
  一次撞车最多要一个 单收费
  从来没有写过一个信息,但信的回音还没有开始
- `Fail(reason)` 之后 `Complete()`,或 `Fail` 具有不同理由的
  grain. `Complete()` 之后, `Fail(reason)` 保留了空白信。

收件箱维持每批的结果、业务变化和在
以本地值进行非同步和可谬误的工作
然后发送,应用状态修改并在一个同步块中完成,无等待
第一次修改后。
<a id="send-messages"></a>

## 发送信息

`IOutbox.Send(OutboxMessage)` 阶段信件。 `OutboxExtensions` 添加已输入的助手 :

- `Send(type, messageId, receiver, body, deliverAt)` 编码和第一阶段
  信件, 可选排程; 批量超载阶段数个 。
- 填`SendReply(type, context, destination, body)` 的答
  收到电文 ID `result` 孩子, 并发送到目的地
  请求书中载有该请求。

`MessageType<T>.Create` 和 `OutboxMessage.ToInboxMessage(sentAt)` 为直接目的创建信息
发送等同内容(目的地、主题、有效载荷和
`DeliverAt`)与原始电文的ID连接未完成;内容不同
丢弃 `InvalidOperationException`。

当收件人是发送 grain时, `Send` 将电文的接受阶段从
应用 grain的“ Q0Q” 身份规则, 使用该“ Q0Q” 自己的箱而不是发件箱, 应用了该信箱的身份规则: a
具有同等内容或保留完成记录的身份识别证件
是一个重复的,而一个具有不同内容的待处理身份
`InvalidOperationException`. 接受情况在调用者的下一期日记中一直存在
以其他阶段状态原子写入, 并且该收件箱处理此信息
写完该写完成后。 由于 `Send` 同步, grain的收件箱承认
不论电文 `MaxCapacity` 和处理人登记;
自发电文在完成之前仍占据其他发送者的能力,
处理等待处理者。

<a id="journal-payloads-once"></a>

## 一次日志有效有效载荷

当有效载荷已经生活在 grain自己日记状态、发件箱源或
收件箱汇槽保持该状态,作为唯一的日记副本。
或启动装置, 在日记恢复前。

- `IOutbox.Register(IOutboxMessageSource)` 添加一个源,
  `IOutbox.Send(messageId)` 级中一个带有电文识别和发送信息的参考条目
  状态。第一个来源的 `TryGetMessage` 返回信息拥有的信息;
  发件人打电话 `TryGetMessage` 验证发送,每次发货尝试之前,
  源返回相同的 `OutboxMessage` ,而它提供消息。
  以上交付规则中, `TryGetMessage` 返回 `false` ,用于电文
  来源信息撤回了待定消息
  `IOutbox.Withdraw(messageId)`,在同一日记中播放
  有效载荷:发件箱不再尝试,也不呼叫 `OnRetired`
  已经在飞行中尝试中的尝试仍然可以发送它。 当消息退出时, 发件箱
  调来电话 `OnRetired` 和 A 相 `OutboxMessageRetirement`: `Delivered` (接收人)
  回答 `Accepted` 或 `Duplicate`, `Rejected` ,(接收器的汇水槽拒绝接受该汇,
  汇的 `Reason` 和 `Code`)或 `DeadLettered` (以死字命名的发箱)
  其 `Reason`,在分期清除时,源的变更导致
  发件箱存有它自己的实现副本
  使源头能够释放有效载荷 `OnRetired`发送后, a
  发送、返回无效消息的源,例如使用不同身份的无效信件,或
  不再提供它的信息 旧字母,单从同一日记
  写作 `DeadLettered` 退休; grain 处理其其他
  。该信件中只有信息代号,其中空载荷和
  默认默认接收器, 其原因记录失败。 回收的条目
  源代码所提供的来源以相同方式用死字母编号,而没有所有人通知。发送
  正在等待引用的再次合并; 身份识别码作为普通信息或
  没有任何来源的,没有给养; `InvalidOperationException`,以及例外或
  `Send` 发布时源代码的无效信息,没有进行任何活动。
  发送 grain 时发送的源消息会丢弃 `ArgumentException`。 A
  恢复的引用在未注册源时失败激活 。
  当前激活中的`OnRetired` 运行一次当前激活中的每一条目。 如果激活失败
  在退休书写承诺前,恢复的回收将恢复待填条目和
  来源源的先前日记状态,并退出回收条目时援引
  与
  退休; 在重复电话中, 产生效果, 而不是在写临时能力。
- `IInbox.Register(IInboxMessageSink)` 添加一个汇清除量。在审定之后和
  收件箱提供每一信息,包括自同意和预定的
  返回 `TryAccept` 的第一个汇的 `true`
  报告 `DeliveryResult` 个案:
  - `Accepted`:水槽将信息放在自己状态下。
    只处理同一写作中的已处理记录,一旦返回 `Accepted`
    durable.
  - `Duplicate`:水槽已经持有此信件。
  - `Backpressured`:汇的容量是吸收能力,发送方重试。
    信件失败 `Send` 。
  - `DeadLettered`:水槽拒绝电文,有原因和可选
    定义的应用程序 `Code`,例如 `orders/duplicate-order`等
    消息源在 `Rejected` 结束结果中收到这两项信息。处理记录会保留
    原因和代码。因此，在记录保留期间收到重复消息时（例如首次响应
    丢失后发生重试），会返回相同的拒绝结果。这是唯一的永久拒绝。

  汇的例外或其他任何情况,结果不明:收件箱阶段
  送货没有结果,所以发件人用后卸回的发件箱重新处理
  直到 `OutboxRetryTimeout`,而自发的电文失败了 `Send`.
  由Sink所有的信息从不占用容量,也没有信箱重试生命周期;
  下个 命令 $ `DeliverAt`。 拒绝的消息会跟随处理器路径一次
  `DeliverAt` 传阅。在汇流运行前保存的完成记录已变质;
  永久的入场底数过期了 一段未追踪的信件是旧发件时间
- `IInbox.RegisterWakeup(getWakeupDelay)` 添加一个组件的回调,例如
  中具有耐久性的工作 grain日记数,它返回多快
  已停用 grain 必须醒过来恢复这项工作, 或在无工作未决时 `null` 。
  每一期杂志在等待信息提醒时间前,
  将它储存起来,而觉醒者又复活了; grain 直到工作结束。
  部件恢复启动工作。

<a id="delivery-and-processing"></a>

## 交付和处理

只有在发送者日记写后才能发送分阶段发送的讯息
收到的信息箱信息仅在接收器日记之后处理
完成写作, 且在永久工作时段( Q0Q) 完成后, 给您发一封 `DeliverAt`的邮件
立即发送和接受排程信件,并
发送书写中接受 grain 发送给自己的排定信件;每个
保存在接收器日记到到期时。 每个接收器返回一个
`DeliveryResult` 案例:

- `Accepted`:信件在收件箱中是耐久的,或者,对于信息来说是收件箱汇
  被接受,在汇的状态 连同处理的记录。
- `Duplicate`:电文识别码待处理或有保留处理的记录。
  发送信息,但汇的拒绝者将接收该信息。
- `Backpressured`: 收件箱持有 `MaxCapacity` 待发信件。 收件箱接收
  其电文 grain 无论向自己发送,而不论 `MaxCapacity`。
- `HandlerNotFound`:接收者没有注册的操作员。
- `DeadLettered`:一个收件箱槽可下潜地拒绝该信息,并有原因和理由
  可选 `Code`。
- `Expired`:电文是在接收器的解码窗口前发送的,
  接收器没有结果,这种尝试没有产生电文效果;
  早先的尝试本来可以被接受,因此交付结果不详。

`Accepted`, `Duplicate`,和 `DeadLettered` 删除发件箱条目。 `Expired`
发送者保留了死亡信,并退出条目,其先前的结果不详。
任何其他结果都计划以指数反弹进行耐久的重试,
发箱保持了飞行中的 `OutboxBatchSize` 尝试,所以是一个慢接收器
后压共享发送的绝对重试预算 。
发箱日志 `HandlerNotFound` 是一个错误。其他失败,包括
断电时无答不通的电话 断电时断断电
`OutboxRetryTimeout` 发送后(6小时),会发生故障。
工作持续时间后,请在后退。未注明交货订单。

每个启动程序都在其调度器上运行一个信息泵,处理箱批次
发送发件箱信息。 一次 grain 的持久工作会唤醒泵, 而两者中之一
或注册的催醒回召提示工作 。
信件到期, 所以每个提醒进程都完全可以完成。 启动
一旦工作持续,即进行适当的工作;该工作包括一次失败的激活,因此
已经到期的工作在恢复后大约一秒钟内恢复。
激活失败时, 将后退永久后卸, 而每次激活都重新武装它
恢复状态后,所以投入的工作总是会唤醒人们。

<a id="state"></a>

## 状态状态状态状态

信箱和发件箱是事件源
`orleans.messaging.inbox` 和 `orleans.messaging.outbox`,并与 JSON 和
Orleans 二进制日记日记格式,它们与 grain其他日记保持了连续
状态。删除 grain状态后,既开始空位,又继续工作。

`DeduplicationWindow`之后,填全的邮件识别号有资格被清除
所接受的受 `SentAt`承认的近照和收回回收
一旦入院楼层覆盖其发送时间,合格记录即符合资格。
每个队列保留到 `DeadLetterRetentionPeriod` 和 `MaxRetainedDeadLetters` 。
字母时间戳和保留在 grain `TimeProvider`之后。

<a id="diagnostics"></a>

## 诊断

`IMessagingDiagnostics` 暴露 `InboxDeadLetters` 和 `OutboxDeadLetters`,每个
记录的信息,理由,尝试数数和死字时间。
`RemoveInboxDeadLetter` 和 `RemoveOutboxDeadLetter` 删除记录,可久
grain的下一期日记写入 。

下 `Microsoft.Orleans` 用于接收、处理、发送和
以 grain 类型和状态标记的已发邮件;用于处理和
交付期;以及 `orleans-messaging-inbox-depth` 和
`orleans-messaging-outbox-depth` 待定工作的上下调计数器。 消息 a grain
向自己发送其收件箱所送发的和收到的编号,而永不捐款
发送框, 发送或深度。 引用会按发送时的编号发送引用; 一个引用条目
来源未提供已发信件数 `source_error`。
收件箱汇清除量单位接受收到 `accepted` 和处理 `success`的计数;一个
汇的拒绝计为收到和处理的 `dead_lettered`。

`MessagingEvents`, `Orleans.Messaging` 诊断听器,报告每个过渡阶段
同步激活, 从而让它发生: `InboxMessageReceiving` 在收件箱前
发送电文的各阶段, `OutboxRetryScheduled` 在重订一次交付失败时,
和电文失效时的 `OutboxMessageRetired` 。每个事件都带有激活
`IGrainContext`. 试验将事件作为障碍,并用投掷物来注入交货过失
从 a 中 `InboxMessageReceiving` 观察者,它失败了尝试 像一个汇例外。

`DurableMessagingOptions` 配置容量、批量大小、回溯性、重复性、
和死字保留。

<a id="deduplication-window"></a>

## 调解窗口

寄送的发件日记 `SentAt` 时间; 每次运送尝试和恢复
`OutboxRetryTimeout`,默认6小时后,将交付尝试捆绑起来
在那日之后,包括接受者受后退。

收件箱保留每个处理记录7天, `DeduplicationWindow`
默认值, 在被接受 `SentAt`后, 然后使其有资格被删除。 在删除记录之前,
收件箱日记簿上的一个入场数:其时钟减去 `DeduplicationWindow`,
有待处理和已处理的记录首先确定已知结果。
信息框中不再有 `SentAt` 位于或低于该层的音轨
在任何汇或处理器运行之前`Expired` 。回收会保留地板。
收件箱在收发单时间后再也不会接受已发货 `SentAt`
`DeduplicationWindow`. 不接受在[备查]上方发送未记录电文的这种发送
允许进入的楼层 `ArgumentOutOfRangeException`,无任何动作,所以发件人
在其重试预算范围内重新重新检查它。
接收器时钟后后退后和未跟踪的结果
仍然收到 `Expired`. A grain给它自己的邮件
使用它的时钟,或者在后钟跳过之后第一次在地板上方。
将待处理的留言留待处理,直至处理完毕的记录符合资格
以在接收器钟窗的两倍内或仅于地板后移走
当自我发送后 后向跳跃。

提前时钟跳可以提前结束未完成的工作;后向跳可以推迟
清除并延长重试。 日记楼层保存已过期的删除信件
直接呼叫者在重复使用命令 `SentAt` 时,对重试重复使用。
`OutboxRetryTimeout` 必须短于 `DeduplicationWindow`, 所以重试
接收方保留窗口的预算将结束。

<a id="recoverable-terminal-outcomes"></a>

## 可回收的终端结果

申请如过期或被拒,需长期对过期或被拒的申请作出答复
一种 `IOutboxMessageSource` ,并排成一个通过 `IOutbox.Send(messageId)`发送的。
在 `OnRetired`中,阶段申请的最终结果和任何待处理的后续行动
以日记状态工作,同时释放源头的有效载荷。
以发件箱退出原子弹,然后在坠机后与 grain 一起收回。
阶段 一个自用信息箱的 信息在回回回回中 恢复后续跟进
信息泵的持久警醒和处理器路径 恢复后。

`Delivered` 系指接收人长期接受该电文或承认其为
重复。 `Rejected` 携带接收者的持久拒绝。 `DeadLettered`
表示发送盒停止了发送, 接收器的早期结果未知,
使用 `Expired`对不确定性结果进行调和
启动新发送前应用程序的应急程序协议。
用于同一逻辑发送的重写原始 `SentAt` 。

使用自有软件
申请给予的寿命为结果和后续工作。
<a id="learn-more"></a>

## 学习更多

见 [信息通讯指南](https://dotnet.github.io/orleans/docs/grains/messaging/)
所汇编的例子见于《联合国国际仲裁裁决汇编》,
[文档辅助片段](../../docs/site/src/content/docs/snippets/compiled/Grains/).
