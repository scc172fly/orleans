# Orleans.Persistence.TestKit

面向 Orleans `IGrainStorage` 提供程序、供仓库内部使用且不依赖特定测试框架的测试基础设施。该项目为 Orleans 自有提供程序的测试套件提供可复用的异步存储契约一致性测试。

<a id="features"></a>

## 功能

- **全面测试覆盖**：测试读取、写入和清除操作
- **并发测试**：验证并发操作下的正确行为
- **ETag 一致性**：确保乐观并发控制正确工作
- **易于集成**：通过简单的基类快速建立测试
- **测试框架无关**：可与 xUnit、NUnit、MSTest 和其他 .NET 测试框架集成

<a id="availability"></a>

## 可用性

暂时有意不发布该包。Orleans 提供程序测试项目通过私有项目引用直接使用 TestKit 源码：

```xml
<ProjectReference Include="$(SourceRoot)src\Orleans.Persistence.TestKit\Orleans.Persistence.TestKit.csproj" PrivateAssets="all" />
```

<a id="quick-start"></a>

## 快速入门

运行器方法返回 `Task`，并通过抛出异常报告契约一致性失败。对调用相应基类方法的重写方法应用所用测试框架的测试特性。

测试夹具公开 `InitializeAsync()` 和 `DisposeAsync()` 生命周期方法。将它们接入测试框架支持的夹具生命周期。

<a id="xunit-example"></a>

### xUnit 示例

<a id="step-1-create-a-test-fixture"></a>

#### 第一步：创建测试夹具

继承 `GrainStorageTestFixture`，实现 xUnit 的 `IAsyncLifetime`，并配置存储提供程序：

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

对于 NUnit、MSTest 和其他框架，将 `InitializeAsync()` 和 `DisposeAsync()` 接入相应的初始化和清理挂钩。运行器本身不需要测试框架适配器。

<a id="step-2-create-test-class"></a>

#### 第二步：创建测试类

继承 `GrainStorageTestRunner` 并实现测试：

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

`GrainStorageTestRunner` 基类提供以下测试方法：

<a id="basic-operations"></a>

### 基本操作

- **`PersistenceStorage_WriteReadIdCyrillic()`**：测试基本写入和读取操作
- **`PersistenceStorage_ReadNonExistentState()`**：测试读取不存在的状态是否返回 RecordExists=false
- **`PersistenceStorage_ReadNonExistentStateHasNonNullState()`**：验证读取不存在的状态后 State 属性不为 null
- **`PersistenceStorage_WriteClearWrite()`**：测试写入、清除、再次写入的循环，确保清除后能够重新写入状态
- **`PersistenceStorage_WriteClearRead()`**：测试完整的写入、清除、读取循环，并验证清除后状态正确初始化
- **`PersistenceStorage_WriteReadClearReadCycle()`**：测试完整的写入、读取、清除、读取循环，以验证状态转换

<a id="key-type-tests"></a>

### 键类型测试

- **`PersistenceStorage_WriteRead_StringKey()`**：测试基于字符串 Grain 键的存储操作
- **`PersistenceStorage_WriteRead_IntegerKey()`**：测试基于整数 Grain 键的存储操作

<a id="cleardelete-operation-tests"></a>

### 清除/删除操作测试

- **`PersistenceStorage_ClearBeforeWrite()`**：测试在任何写入之前调用 Clear 能否正确工作
- **`PersistenceStorage_ClearStateDoesNotNullifyState()`**：验证 Clear 操作后 State 属性始终不为 null
- **`PersistenceStorage_ClearUpdatesETag()`**：测试 Clear 操作后 ETag 是否发生变化
- **`PersistenceStorage_ReadAfterClear()`**：测试清除状态后的读取操作
- **`PersistenceStorage_MultipleClearOperations()`**：测试多次连续清除操作的幂等性

<a id="consistency-tests"></a>

### 一致性测试

- **`PersistenceStorage_WriteDuplicateFailsWithInconsistentStateException()`**：验证尝试插入重复数据时会抛出 `InconsistentStateException`
- **`PersistenceStorage_WriteInconsistentFailsWithInconsistentStateException()`**：验证使用错误 ETag 写入时会抛出 `InconsistentStateException`
- **`PersistenceStorage_ETagChangesOnWrite()`**：测试连续写入时 ETag 能否正确更新
- **`PersistenceStorage_WriteWithSameValuesUpdatesETag()`**：测试使用相同值更新状态时 ETag 是否仍会更新
- **`PersistenceStorage_ClearInconsistentFailsWithInconsistentStateException()`**：验证使用过期 ETag 清除会失败，且不会删除最新状态
- **`PersistenceStorage_StateNamesUseIndependentRecords()`**：验证同一 Grain ID 下不同状态名称标识独立的记录

<a id="concurrency-tests"></a>

### 并发测试

- **`PersistenceStorage_WriteReadWriteReadStatesInParallel()`**：测试对多个 Grain 的并行写入和读取操作

<a id="generated-model-based-tests"></a>

## 生成式基于模型的测试

`GrainStorageModelBasedTestRunner` 通过生成读取、写入和清除操作序列，并依据行为模型检查提供程序，来补充直接执行的 `GrainStorageTestRunner` 测试。

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

使用默认设置时，较简短的 `new GrainStorageModelBasedTestRunner(fixture.Storage, "MyStorage")` 重载即可。可选的 `Action<string>` 构造函数参数可以接收失败详情。

<a id="model-based-options"></a>

### 基于模型的测试选项

- **`ProviderName`**：标识提供程序，用于创建唯一的默认键前缀。
- **`GrainType`**：传递给 `IGrainStorage` 操作的 Grain 类型名称。
- **`KeyPrefix`**：生成 Grain 键时使用的可选固定前缀；省略时生成唯一前缀。
- **`MaxDepth`**：生成模型遍历的最大深度。
- **`MaxSequenceLength`**：每个生成序列中的最大操作数。
运行器始终验证重复写入、使用过期 ETag 写入和清除的行为。它使用隔离的测试键判断提供程序是删除还是保留已清除记录，并对这两种表示方式强制执行相同的可观察状态和并发行为契约。

<a id="testing-approach"></a>

## 测试方法

此 TestKit 提供**直接 IGrainStorage 测试**，不同于基于 Grain 的测试：

- **直接存储测试**（本 TestKit）：直接测试 `IGrainStorage` 接口，无需 Grain 实现
- **基于 Grain 的测试**（例如 `GrainPersistenceTestsRunner`）：通过实际 Grain 操作测试存储

两种方法都有价值：
- 直接存储测试隔离验证存储提供程序的实现
- 基于 Grain 的测试验证包含 Grain 生命周期在内的完整持久化流程

建议在以下场景使用此 TestKit：
- 测试自定义存储提供程序
- 在部署到生产环境前验证存储行为
- 存储提供程序开发期间快速迭代
- 测试边界情况及错误处理

<a id="advanced-usage"></a>

## 高级用法

<a id="custom-test-state"></a>

### 自定义测试状态

尽管 TestKit 提供默认的 `TestState1` 类，也可以在自定义测试中使用自己的状态类：

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

### 自定义测试集群配置

可以通过重写 `ConfigureTestCluster` 自定义测试集群配置：

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

## 辅助方法

`GrainStorageTestRunner` 类提供可在测试中使用的受保护辅助方法：

- **`GetTestReferenceAndState(long grainId, string? version)`**：创建使用整数键的测试 Grain ID 和状态
- **`GetTestReferenceAndState(string grainId, string? version)`**：创建使用字符串键的测试 Grain ID 和状态
- **`Store_WriteRead<T>(string grainTypeName, GrainId grainId, GrainState<T> grainState)`**：写入并读取状态，验证正确性
- **`Store_WriteClearRead<T>(string grainTypeName, GrainId grainId, GrainState<T> grainState)`**：写入、清除并读取状态

<a id="example-testing-memory-storage"></a>

## 示例：测试内存存储

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

- .NET 10 或更高版本
- Orleans 10.0 或更高版本
- 支持异步测试的 .NET 测试框架

<a id="contributing"></a>

## 贡献

本项目源码位于 [Orleans 仓库](https://github.com/dotnet/orleans)。

<a id="license"></a>

## 许可证

本项目采用 MIT 许可证，详情参见 [LICENSE](https://github.com/dotnet/orleans/blob/main/LICENSE) 文件。
