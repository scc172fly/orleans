# Orleans.Persistence.TestKit

供 Orleans `IGrainStorage` 供应商使用的仓库内部、框架中性测试基础设施。该项目为 Orleans-自有供应商测试套件提供可重复使用的非同步存储合规测试。

<a id="features"></a>

## 功能

- **全面测试覆盖率**:读、写和清除操作的测试
- **货币计算测试**:验证同时操作下的正确行为
- **ETag一致性**:确保适当的乐观货币同值控制
- **简单一体化**:简易基础班,用于快速测试设置
- **框架中立**:与 xUnit、N 单位、MSTest和其他 .NET 测试框架结合

<a id="availability"></a>

## 可用性

有意推迟公布。 Orleans 测试提供商项目使用来自源头的测试工具包,使用私人项目参考:

```xml
<ProjectReference Include="$(SourceRoot)src\Orleans.Persistence.TestKit\Orleans.Persistence.TestKit.csproj" PrivateAssets="all" />
```

<a id="quick-start"></a>

## 快速启动

运行者的方法返回 `Task` ,并通过丢弃例外报告不合规。应用框架的测试属性来取代称为相应基准方法的功能。

固定装置暴露了 `InitializeAsync()` 和 `DisposeAsync()` 生命周期方法。将它们与得到您测试框架支持的固定生命周期联系起来。

<a id="xunit-example"></a>

### xUnit 示例

<a id="step-1-create-a-test-fixture"></a>

#### 步骤1:创建测试定置

从 `GrainStorageTestFixture`继承遗产,执行 xUnit的 `IAsyncLifetime`,并配置您的存储提供者:

```csharp
using Orleans.Persistence.TestKit;
using Orleans.Hosting;
using Xunit;

public class MyStorageTestFixture : GrainStorageTestFixture, IAsyncLifetime
{
    protected override string StorageProviderName => "MyStorage";

    protected override void ConfigureSilo(ISiloBuilder siloBuilder)
    {
        // Configure your custom storage provider
        siloBuilder.AddMyCustomGrainStorage("MyStorage", options =>
        {
            // Configure options
        });
    }
}
```

NUnit、MSTest和其他框架将 `InitializeAsync()` 和 `DisposeAsync()` 连接到相应的设置和断裂钩。 运行者本身不需要框架适配器。

<a id="step-2-create-test-class"></a>

#### 步骤2:创建测试类

从 `GrainStorageTestRunner` 继承遗产,并进行测试:

```csharp
using Orleans.Persistence.TestKit;
using Xunit;

public class MyStorageTests : GrainStorageTestRunner, IClassFixture<MyStorageTestFixture>
{
    public MyStorageTests(MyStorageTestFixture fixture) 
        : base(fixture.Storage)
    {
    }

    [Fact]
    public override Task PersistenceStorage_WriteReadIdCyrillic() => base.PersistenceStorage_WriteReadIdCyrillic();

    [Fact]
    public override Task PersistenceStorage_WriteDuplicateFailsWithInconsistentStateException() => base.PersistenceStorage_WriteDuplicateFailsWithInconsistentStateException();

    [Fact]
    public override Task PersistenceStorage_WriteInconsistentFailsWithInconsistentStateException() => base.PersistenceStorage_WriteInconsistentFailsWithInconsistentStateException();

    [Fact]
    public override Task PersistenceStorage_WriteReadWriteReadStatesInParallel() => base.PersistenceStorage_WriteReadWriteReadStatesInParallel();
}
```

<a id="available-tests"></a>

## 可用测试

`GrainStorageTestRunner` 基级提供以下测试方法:

<a id="basic-operations"></a>

### 基本业务

- **`PersistenceStorage_WriteReadIdCyrillic()`**:测试基本写作和读取操作
- **`PersistenceStorage_ReadNonExistentState()`**: 测试读取不存在状态返回 RecordExists=假货
- **`PersistenceStorage_ReadNonExistentStateHasNonNullState()`**:经确认不存在的国家财产在读作后并非无效
- **`PersistenceStorage_WriteClearWrite()`**:测试写、清晰和写周期,以确保在清理后能够重新撰写状态。
- **`PersistenceStorage_WriteClearRead()`**:测试完整的写清读周期,核查是否在清楚后正确初始化状态
- **`PersistenceStorage_WriteReadClearReadCycle()`**:测试完成读写清除读取周期,以核实国家过渡情况

<a id="key-type-tests"></a>

### 按键类型测试

- **`PersistenceStorage_WriteRead_StringKey()`**:用字符串 grain 键测试存储操作
- **`PersistenceStorage_WriteRead_IntegerKey()`**:以整数制的试验储存操作 grain 键

<a id="cleardelete-operation-tests"></a>

### 清除/删除操作测试

- **`PersistenceStorage_ClearBeforeWrite()`**:在任何写入工作正确之前拨打清除的测试
- **`PersistenceStorage_ClearStateDoesNotNullifyState()`**:证明国家财产在明确运作后从未无效
- **`PersistenceStorage_ClearUpdatesETag()`**:测试ETag在清除操作后更改ETag
- **`PersistenceStorage_ReadAfterClear()`**:清除后测试读取状态
- **`PersistenceStorage_MultipleClearOperations()`**:测试多个连续的清晰操作为无能

<a id="consistency-tests"></a>

### 一致性试验

- **`PersistenceStorage_WriteDuplicateFailsWithInconsistentStateException()`**:证明试图插入重复的数据投弃 `InconsistentStateException`
- **`PersistenceStorage_WriteInconsistentFailsWithInconsistentStateException()`**:证明以不正确的ETag投出 `InconsistentStateException`的文字
- **`PersistenceStorage_ETagChangesOnWrite()`**:测试ETag是否正确更新连续写作
- **`PersistenceStorage_WriteWithSameValuesUpdatesETag()`**:以相同值更新状态的测试仍然更新ETag
- **`PersistenceStorage_ClearInconsistentFailsWithInconsistentStateException()`**:证明使用已老化的ETag清除失败,没有删除最新状态
- **`PersistenceStorage_StateNamesUseIndependentRecords()`**:证明国家名称能识别同一国家的独立记录 grain ID 身份

<a id="concurrency-tests"></a>

### 货币货币计算测试

- **`PersistenceStorage_WriteReadWriteReadStatesInParallel()`**:测试多个多功能的平行写和读操作 grains

<a id="generated-model-based-tests"></a>

## 生成模型测试

`GrainStorageModelBasedTestRunner` 通过生成读写和清晰操作序列,并对照行为模型检查供应商,补充直接 `GrainStorageTestRunner` 测试。

```csharp
[Fact]
public Task GrainStorage_ModelBasedGeneratedConformance()
{
    var runner = new GrainStorageModelBasedTestRunner(
        Storage,
        new GrainStorageModelBasedConformanceOptions
        {
            ProviderName = "MyStorage",
            MaxDepth = 4,
            MaxSequenceLength = 4
        });

    return runner.RunGeneratedConformanceTests();
}
```

对于默认值, 较短的 `new GrainStorageModelBasedTestRunner(fixture.Storage, "MyStorage")` 超载就足够了。 可选的 `Action<string>` 构建器参数可以接收故障细节 。

<a id="model-based-options"></a>

### 示范备选办法

- **`ProviderName`**:指定了供应商,并用于创建独有的默认密钥前缀。
- **`GrainType`**: Grain 型号名称传递到 `IGrainStorage` 操作。
- **`KeyPrefix`**:生成 grain 密钥可选固定的前缀;如果省略,将产生一个独有的前缀。
- **`MaxDepth`**:最大模型生成的跨度深度。
- **`MaxSequenceLength`**:每个生成序列中最大操作次数。
运行者总是重复写作, stale- ETag 写作和清除。 它发现提供商是否使用单独的测试键删除或保留已清理的记录,同时对两种表述都执行相同的可观测状态和同货币行为。

<a id="testing-approach"></a>

## 测试方法

本测试包提供 **直接 IGrainStorage 测试**,与 grain的测试不同:

- **直接储存测试** (本工具包):直接测试 `IGrainStorage` 接口,无需 grain 执行
- **Grain- 以** (e.g)., `GrainPersistenceTestsRunner`:通过实际 grain 操作进行测试储存

两种办法都具有价值:
- 直接储存测试独立验证存储供应商实施
- Grain- 基于测试的检验验证了包括 grain 生命周期在内的全部持久性管道

建议使用这一测试包:
- 测试自定制储存供应商
- 在部署到生产前核查存储行为
- 开发储存供应商期间快速迭代
- 测试边缘案例和错误处理

<a id="advanced-usage"></a>

## 高级使用

<a id="custom-test-state"></a>

### 自定义测试状态

当测试包提供默认 `TestState1` 类时,您可以在自定义测试中使用自己的状态等级:

```csharp
public class MyCustomStateTests : GrainStorageTestRunner, IClassFixture<MyStorageTestFixture>
{
    public MyCustomStateTests(MyStorageTestFixture fixture) : base(fixture.Storage)
    {
    }

    [Fact]
    public async Task TestCustomState()
    {
        var grainId = GrainId.Create("my-grain", Guid.NewGuid().ToString());
        var grainState = new GrainState<MyCustomState> 
        { 
            State = new MyCustomState { Property = "value" } 
        };

        await Store_WriteRead("MyGrainType", grainId, grainState);
    }
}
```

<a id="custom-test-cluster-configuration"></a>

### 自定义测试集集配置

您可以通过覆盖 `ConfigureTestCluster`来自定义测试组配置:

```csharp
public class MyStorageTestFixture : GrainStorageTestFixture, IAsyncLifetime
{
    protected override string StorageProviderName => "MyStorage";

    protected override void ConfigureSilo(ISiloBuilder siloBuilder)
    {
        siloBuilder.AddMyCustomGrainStorage("MyStorage");
    }

    protected override void ConfigureTestCluster(InProcessTestClusterBuilder builder)
    {
        builder.Options.InitialSilosCount = 1;
    }
}
```

<a id="helper-methods"></a>

## 帮助方法

类 `GrainStorageTestRunner` 提供受保护的辅助方法,您可在测试中使用:

- **`GetTestReferenceAndState(long grainId, string? version)`**:创建测试 grain ID, 带有整数键的状态
- **`GetTestReferenceAndState(string grainId, string? version)`**:创建测试 grain 使用字符串密钥来显示身份识别符号
- **`Store_WriteRead<T>(string grainTypeName, GrainId grainId, GrainState<T> grainState)`**: 写作和读取状态,验证正确性
- **`Store_WriteClearRead<T>(string grainTypeName, GrainId grainId, GrainState<T> grainState)`**: 写、 清、 读

<a id="example-testing-memory-storage"></a>

## 实例:测试存储内存存储

```csharp
using Orleans.Persistence.TestKit;
using Orleans.Hosting;
using Xunit;

// Fixture
public class MemoryStorageTestFixture : GrainStorageTestFixture, IAsyncLifetime
{
    protected override string StorageProviderName => "MemoryStore";

    protected override void ConfigureSilo(ISiloBuilder siloBuilder)
    {
        siloBuilder.AddMemoryGrainStorage("MemoryStore");
    }
}

// Tests
public class MemoryStorageTests : GrainStorageTestRunner, IClassFixture<MemoryStorageTestFixture>
{
    public MemoryStorageTests(MemoryStorageTestFixture fixture) : base(fixture.Storage)
    {
    }

    [Fact]
    public override Task PersistenceStorage_WriteReadIdCyrillic() => base.PersistenceStorage_WriteReadIdCyrillic();

    [Fact]
    public override Task PersistenceStorage_WriteDuplicateFailsWithInconsistentStateException() => base.PersistenceStorage_WriteDuplicateFailsWithInconsistentStateException();

    [Fact]
    public override Task PersistenceStorage_WriteInconsistentFailsWithInconsistentStateException() => base.PersistenceStorage_WriteInconsistentFailsWithInconsistentStateException();

    [Fact]
    public override Task PersistenceStorage_WriteReadWriteReadStatesInParallel() => base.PersistenceStorage_WriteReadWriteReadStatesInParallel();
}
```

<a id="requirements"></a>

## 要求

- .NET 10或更晚
- Orleans 10. 0 或以后
- 具有非同步测试支持的 .NET 测试框架

<a id="contributing"></a>

## 参与贡献

该项目的源代码位于 [Orleans 仓库](https://github.com/dotnet/orleans)。

<a id="license"></a>

## 许可证

该项目根据麻省理工学院许可证获得许可,详情见 [许可证](https://github.com/dotnet/orleans/blob/main/LICENSE) 文件。
