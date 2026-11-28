<a id="journaling-with-azure-blob-json"></a>

# 使用 Azure Blob JSON 的日志持久化

本示例演示如何将 Orleans 持久化 Grain 状态作为 JSON 事件记录在
Azure Blob Storage 中。它演示了持久化字典、列表、队列、集合、值、
任务完成源及常规持久化状态，然后停用并
重新激活 Grain，以验证恢复结果。

宿主的停止令牌会贯穿 Grain 调用、日志写入以及 Azure
存储操作。优雅关闭使用配置的宿主关闭截止时间。

`JournaledSampleGrain` 继承 `Grain` 并注入 `IDurableStateManager`。
其构造时的字段初始化表达式使用以下方法声明七个命名状态组件：
`GetOrAddDictionary`、`GetOrAddList`、`GetOrAddQueue`、`GetOrAddSet`、
`GetOrAddValue`、`GetOrAddPersistentState` 和 `GetOrAddTaskCompletionSource`。
标准管理器在与 Grain 绑定的构造过程中、依赖解析返回之前，
将自身注册到激活生命周期中。Orleans 在 `SetupState` 阶段恢复 Grain 状态，
早于 `OnActivateAsync` 及 Grain 方法的执行。等待一次
`WriteStateAsync` 即可确认全部七个组件中的待提交变更。

应在构造期间或同步激活设置期间、初始化之前声明新的状态组件；
后续 `GetOrAdd` 调用解析已经存在的名称。
键控注入仍是获得同一命名对象的等效方式，
`DurableGrain` 仍是便于使用的基类。本示例保留了原有的
状态名称，以便仍能读取采用键控注入的版本写入的日志。

<a id="run-the-sample"></a>

## 运行示例

安装 .NET 11 SDK、Aspire CLI 及与 Docker 兼容的容器运行时。
在此目录中运行：

```powershell
aspire run --project JournalingAzureBlobJson.AppHost
```

应用程序写入场景数据、验证恢复后的状态，并输出 Azure Storage 模拟器中存储的
原始 JSON Lines 日志。

管理器 API 需要包含这些 API 的 Journaling 包。在此
仓库中，`samples\Build-Samples.ps1` 使用当前源码构建的包执行验证。
示例保留 NuGet 引用及自身的中央包版本管理文件，
因此这些包发布后可不经修改直接复制到仓库之外使用。
