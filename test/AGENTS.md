<a id="orleans-testing-guidance"></a>

# Orleans 测试指导

这些规则可依次适用于 `test/`之下的测试。
执行原则也适用。

<a id="test-design"></a>

## 测试设计

- 以模型为基础的测试比较执行行为与整个业务合同
  序列,包括相关的货币和故障。
- 必须以财产为基础的失败必须可以复制,同时有效缩小。
- 保持快照确定性并集中重点;检查预期的基准变化
  语义学

<a id="reliability"></a>

## 可靠性

- 包括钟、团群、表层学
  储存和背景任务。
- 给予一名管弦乐队主事独有的假时间提升所有权;工人要求
  或等待进展。
- 使用 `FakeTimeProvider` 用于 grain- 时速,按键 `TimeProviderNames` 用于
  子系统时钟,以及 `TimeProviderTestingExtensions.UseTimeProviderForBackgroundAreas`
  用于不相关的实时工作(`src/Orleans.TestingHost/TimeProviderTestingExtensions.cs`)。
  时间- 时间表同步化时, 请跟随 `TrackingFakeTimeProvider`
  `test/Orleans.DefaultCluster.Tests/FakeTimeSilo.cs`.
- 以明确的阶段障碍取代睡眠和投票:等待所有权和准备状态、武装观察员或同步原始人、采取一项行动或提前一个时间,然后等待完成。
- 使用有控制时间和同步性的确切说法;解决非确定性问题
  而不是扩大预期范围。
- 观察 `DiagnosticListener` 和 `DiagnosticEventCollector`的生命周期过渡
  `src/Orleans.TestingHost/Diagnostics/DiagnosticEventCollector.cs`; 见
  `test/Orleans.GrainDirectory.Tests/GrainDirectory/GrainDirectoryLeaseTests.cs`.
  用于在工艺中同步化,使用 `InProcessTestCluster.TryGetGrainContext`
  酌情; 维护生产行为。
- 手臂,在引发事件前,先成形等待。 `IEnumerable<Task>` 序列,因此在动作发生之前就存在订阅。
- 不仅单独测试,而且按相关测试顺序强调完整的共享固定等级或套件。 当运行时的行为可能不同时,测试每个支持的目标框架。
- 缺少的过渡或事件诊断超时。 包括阶段、 实体编号、 实体编号、
  预期成绩/实际状态、所有权和武装时间表。
- 验证补丁本身。 不要依赖不相关的 CIN 从不含更改的分支运行 。
