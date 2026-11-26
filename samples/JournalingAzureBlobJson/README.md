<a id="journaling-with-azure-blob-json"></a>

# 与 Azure 布线 JSON

本样本显示 Orleans 耐 grain 国家以 JSON 事件记为
Azure 使用耐久词典、列表、队列、组装、值,
任务完成源,以及经常的持久性状态,然后是停止使用和
重新启动 grain ,以核实回收情况。

东道主通过调用 grain 、日记写和 Azure停止象征性流
宽大关闭使用已配置的主机关闭截止时间 。

`JournaledSampleGrain` 来自 `Grain` 和投射 `IDurableStateManager`。
其建筑师字段初始化器宣布使用7个命名的州组件
`GetOrAddDictionary`, `GetOrAddList`, `GetOrAddQueue`, `GetOrAddSet`,
`GetOrAddValue`、 `GetOrAddPersistentState`和 `GetOrAddTaskCompletionSource`。
标准管理员在 grain-受约束期间注册参加激活生命周期
Orleans 恢复 grain状态
`SetupState`,在 `OnActivateAsync` 和 grain 方法运行之前。
`WriteStateAsync` 确认所有七个构成部分的变动尚待实现。

之前,在施工或同步启动安装期间宣布新的状态部件
初始化; 后 `GetOrAdd` 调用解决现有名称。
注射键盘仍然是获得同一命名物体的同等方法,
和 `DurableGrain` 仍然是方便基级。
表示名称,所以用键入注射版本撰写的期刊仍然可以读取。

<a id="run-the-sample"></a>

## 运行样本

安装 .NET 11 SDK、 占星 CLI和与多克兼容的容器运行时间。
从此目录运行 :

```powershell
aspire run --project JournalingAzureBlobJson.AppHost
```

申请程序写了一种假想,验证了回收状态,并打印了原始
JSON 存储的线日记 Azure 存储模拟器

管理器 API 需要包含这些API的日记包。在此
存储库, `samples\Build-Samples.ps1` 对照从
当前的源。 样本保留 NuGet 参考文献和它自己的中央包
文档, 以便这些软件包发布后可以原样复制出来 。
