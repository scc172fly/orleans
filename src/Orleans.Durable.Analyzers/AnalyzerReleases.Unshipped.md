; Unshipped analyzer release
; https://github.com/dotnet/roslyn/blob/main/src/RoslynAnalyzers/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
ORLEANS0200 | Usage | Warning | DurableTaskReplayAnalyzer, grain 状态,以重播放重运行的代码访问
ORLEANS0201 | Usage | Warning | DurableTaskReplayAnalyzer,在重播重运行的代码中产生的非确定值
ORLEANS0202 | Usage | Warning | DurableTaskReplayAnalyzer,外部等待重播重运行的代码
ORLEANS0203 | Usage | Warning | DurableTaskReplayAnalyzer, grain 状态在外部等待前更改
ORLEANS0204 | Usage | Warning | DurableTaskReplayAnalyzer, DurableTask.RunInline 效果会转让或更改一个已捕获变量
ORLEANS0205 | Usage | Error | DurableTaskDefinitionAnalyzer,持久性任务定义从未等待、计划或张贴
ORLEANS0206 | Usage | Warning | DurableTaskDefinitionAnalyzer,单用耐久任务定义不止一次
ORLEANS0207 | Usage | Error | DurableTaskReplayAnalyzer,耐久操作名称中含有“$”
ORLEANS0208 | Usage | Warning | DurableTaskReplayAnalyzer,耐久作业名称在另一个呼叫站重复
ORLEANS0209 | Usage | Warning | DurableTaskReplayAnalyzer,耐久任务机构运行排程器的工作
ORLEANS0210 | Usage | Warning | DurableTaskReplayAnalyzer,任务组合器运行耐久工作
ORLEANS0211 | Usage | Warning | DurableTaskReplayAnalyzer,重播重运行的代码无法耐久延迟
ORLEANS0212 | Usage | Warning | DurableTaskReplayAnalyzer,无 IsTaskCancellationRequested 过滤器的注销处理器
ORLEANS0213 | Usage | Warning | DurableTaskReplayAnalyzer,在一个持久任务机构内为无效代码
ORLEANS0214 | Usage | Info | DurableTaskReplayAnalyzer,重复等待一个信号名称
ORLEANS0215 | Usage | Info | DurableTaskReplayAnalyzer,重播重运行的代码记录
ORLEANS0216 | Usage | Warning | DurableTaskDefinitionAnalyzer、在 grains 和持久任务机构之外张贴的耐久任务
ORLEANS0217 | Usage | Warning | DurableTaskCleanupAnalyzer,任务取消清理通过已经排出 CancellationToken
ORLEANS0218 | Usage | Warning | DurableTaskVersionAnalyzer,用非恒定名称或范围进行版本检查
ORLEANS0219 | Usage | Info | DurableTaskDefinitionAnalyzer,处理一个没有任何观察的被排入儿童
ORLEANS0220 | Usage | Info | DurableTaskIdentityAnalyzer,无名称的持久操作,其生成的身份识别特征可转移
ORLEANS0221 | Usage | Info | DurableTaskDelegateAnalyzer, DurableTask.Run 步,步运行同步工作
ORLEANS0222 | Usage | Error | DurableTaskDelegateAnalyzer,自动失效的代表传递给 DurableTask.Run 或 DurableTask.RunInline
ORLEANS0223 | Usage | Warning | DurableTaskStateMutationAnalyzer, grain 状态值已更改
ORLEANS0225 | Usage | Error | DurableTaskReplayAnalyzer,持久任务机构调查一项预定的任务
ORLEANS0226 | Usage | Error | DurableTaskReplayAnalyzer,耐久任务机构继续不使用其排程器 ConfigureAwait
ORLEANS0227 | Usage | Error | DurableTaskReplayAnalyzer长期运作 DurableTask.RunInline 效果
ORLEANS0228 | Usage | Error | DurableTaskDefinitionAnalyzer、一步的分离起始点、一个同步 DurableTask 方法或组合器
