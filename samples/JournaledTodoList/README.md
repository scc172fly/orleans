<a id="journaled-todo-list"></a>

# 日志化待办事项列表

本示例是由 Aspire 托管的 Blazor Web 应用，使用 Orleans 日志一致性提供程序
演示持久化的日志化 Grain 状态。应用运行三个
Orleans Silo，并使用模拟的 Azure Table Storage 实现集群成员资格，
使用 Azure Blob Storage 存储 Grain 状态。

<a id="run-the-sample"></a>

## 运行示例

安装 .NET 10 SDK、Aspire CLI 和兼容 Docker 的容器运行时。
在当前目录运行：

```powershell
aspire run --project JournaledTodoList.AppHost
```

打开 Aspire 仪表板中显示的 `webapp` 终结点。添加、完成及删除
待办事项，以体验日志化待办事项列表 Grain。
