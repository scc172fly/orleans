using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Accordant;
using Orleans.Runtime;
using Orleans.Storage;

namespace Orleans.Persistence.TestKit;

/// <summary>
/// 配置针对 <see cref="IGrainStorage"/> 实现生成的基于模型的测试。
/// </summary>
public sealed class GrainStorageModelBasedConformanceOptions
{
    /// <summary>
    /// 获取或设置用于标识生成测试数据的存储提供程序名称。
    /// </summary>
    public string ProviderName { get; set; } = "Storage";

    /// <summary>
    /// 获取或设置传入存储操作的 Grain 类型名称。
    /// </summary>
    public string GrainType { get; set; } = "Orleans.Persistence.TestKit.ModelBasedStorageConformanceGrain";

    /// <summary>
    /// 获取或设置生成 Grain 键时使用的可选固定前缀。
    /// </summary>
    public string? KeyPrefix { get; set; }

    /// <summary>
    /// 获取或设置生成操作序列时探索的最大深度。
    /// </summary>
    public int MaxDepth { get; set; } = 4;

    /// <summary>
    /// 获取或设置生成测试序列中的最大操作数。
    /// </summary>
    public int MaxSequenceLength { get; set; } = 4;

}

/// <summary>
/// 生成存储操作序列，并根据行为模型验证 <see cref="IGrainStorage"/> 实现。
/// </summary>
public sealed class GrainStorageModelBasedTestRunner
{
    private readonly IGrainStorage storage;
    private readonly GrainStorageModelBasedConformanceOptions options;
    private readonly Action<string>? output;

    /// <summary>
    /// 初始化 <see cref="GrainStorageModelBasedTestRunner"/> 类的新实例。
    /// </summary>
    /// <param name="storage">要测试的存储提供程序。</param>
    /// <param name="providerName">用于标识生成测试数据的存储提供程序名称。</param>
    /// <param name="output">接收失败详细信息的可选回调。</param>
    public GrainStorageModelBasedTestRunner(IGrainStorage storage, string providerName, Action<string>? output = null)
        : this(storage, new GrainStorageModelBasedConformanceOptions { ProviderName = providerName }, output)
    {
    }

    /// <summary>
    /// 初始化 <see cref="GrainStorageModelBasedTestRunner"/> 类的新实例。
    /// </summary>
    /// <param name="storage">要测试的存储提供程序。</param>
    /// <param name="options">生成式测试配置。</param>
    /// <param name="output">接收失败详细信息的可选回调。</param>
    public GrainStorageModelBasedTestRunner(IGrainStorage storage, GrainStorageModelBasedConformanceOptions options, Action<string>? output = null)
    {
        this.storage = storage ?? throw new ArgumentNullException(nameof(storage));
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.output = output;
    }

    /// <summary>
    /// 生成并执行存储操作序列。
    /// </summary>
    /// <returns>表示异步测试运行的任务。</returns>
    /// <exception cref="InvalidOperationException">一个或多个生成的测试用例失败。</exception>
    public Task RunGeneratedConformanceTests() =>
        RunGeneratedConformanceTests(CancellationToken.None);

    /// <summary>
    /// 生成并执行存储操作序列。
    /// </summary>
    /// <param name="cancellationToken">用于取消存储操作的令牌。</param>
    /// <returns>表示异步测试运行的任务。</returns>
    /// <exception cref="InvalidOperationException">一个或多个生成的测试用例失败。</exception>
    public async Task RunGeneratedConformanceTests(CancellationToken cancellationToken)
    {
        var results = await GrainStorageModelBasedConformance.RunGeneratedTests(
            storage,
            options,
            output,
            cancellationToken);
        var failures = results.Where(result => !result.Success).Select(GrainStorageModelBasedConformance.BuildFailureMessage).ToList();
        if (failures.Count > 0)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, failures));
        }
    }
}

internal static class GrainStorageModelBasedConformance
{
    public static Task<IList<TestCaseExecutionResult>> RunGeneratedTests(
        IGrainStorage storage,
        string storageName,
        Action<string>? output = null) =>
        RunGeneratedTests(storage, storageName, output, CancellationToken.None);

    public static async Task<IList<TestCaseExecutionResult>> RunGeneratedTests(
        IGrainStorage storage,
        string storageName,
        Action<string>? output,
        CancellationToken cancellationToken)
    {
        return await RunGeneratedTests(
            storage,
            new GrainStorageModelBasedConformanceOptions { ProviderName = storageName },
            output,
            cancellationToken);
    }

    public static Task<IList<TestCaseExecutionResult>> RunGeneratedTests(
        IGrainStorage storage,
        GrainStorageModelBasedConformanceOptions options,
        Action<string>? output = null) =>
        RunGeneratedTests(storage, options, output, CancellationToken.None);

    public static async Task<IList<TestCaseExecutionResult>> RunGeneratedTests(
        IGrainStorage storage,
        GrainStorageModelBasedConformanceOptions options,
        Action<string>? output,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        var runId = options.KeyPrefix ?? $"{SanitizeStorageName(options.ProviderName)}-{Guid.NewGuid():N}";
        var deleteStateOnClear = await DetectDeleteStateOnClear(
            storage,
            options.GrainType,
            $"{runId}-clear-behavior-{Guid.NewGuid():N}",
            cancellationToken);
        var spec = new GrainStorageBehavioralSpec(deleteStateOnClear);
        var initialState = new GrainStorageBehavioralModelState();
        var inputSet = spec.CreateInputSet();
        var testCases = spec.GenerateTests(
            initialState,
            inputSet,
            new TestGenerationOptions
            {
                MaxDepth = options.MaxDepth,
                SequentialTestCaseAlgorithm = SequentialTestCaseAlgorithms.CreateTransitionCoverage(maxSequenceLength: options.MaxSequenceLength),
                ShouldApply = (input, state) => GrainStorageBehavioralSpec.CanApply((StorageRequest)input.Request, (GrainStorageBehavioralModelState)state, deleteStateOnClear)
            });

        var context = spec.CreateTestingContext();
        context.RequestPrinter = request => request?.ToString() ?? "<null>";
        context.ResponsePrinter = response => response?.ToString() ?? "<null>";

        var testIndex = 0;
        var results = await spec.RunTests(
            context,
            initialState,
            testCases,
            new TestExecutionOptions
            {
                StopOnFirstFailure = true,
                BeforeEach = info =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    info.Context.Register(new GrainStorageExecutionContext(
                        storage,
                        options.GrainType,
                        $"{runId}-{testIndex++:D4}",
                        cancellationToken));
                },
                AfterEach = info =>
                {
                    if (!info.Success)
                    {
                        output?.Invoke(info.FailureMessage);
                    }
                }
            });
        cancellationToken.ThrowIfCancellationRequested();
        return results;
    }

    private static async Task<bool> DetectDeleteStateOnClear(
        IGrainStorage storage,
        string grainType,
        string key,
        CancellationToken cancellationToken)
    {
        var grainState = new GrainState<TestState1> { State = CreateState(StorageValue.One) };
        await storage.WriteStateAsync(
            grainType,
            GrainId.Create(grainType, key),
            grainState,
            cancellationToken);
        await storage.ClearStateAsync(
            grainType,
            GrainId.Create(grainType, key),
            grainState,
            cancellationToken);
        return grainState.ETag is null;
    }

    public static string BuildFailureMessage(TestCaseExecutionResult result)
    {
        var builder = new StringBuilder("Model-based grain storage conformance test failed.");
        if (!string.IsNullOrWhiteSpace(result.LastFailureMessage))
        {
            builder.Append(' ').Append(result.LastFailureMessage);
        }

        if (!string.IsNullOrWhiteSpace(result.LogFilePath))
        {
            builder.Append(" Log: ").Append(result.LogFilePath);
        }

        return builder.ToString();
    }

    private static string SanitizeStorageName(string storageName)
    {
        if (string.IsNullOrWhiteSpace(storageName))
        {
            return "Storage";
        }

        return new string(storageName.Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
    }

    private sealed class GrainStorageBehavioralSpec : Spec<GrainStorageBehavioralModelState>
    {
        public readonly ReadOperation Read;
        public readonly WriteOperation Write;
        public readonly ClearOperation Clear;

        public GrainStorageBehavioralSpec(bool deleteStateOnClear)
        {
            Read = new ReadOperation();
            Write = new WriteOperation();
            Clear = new ClearOperation(deleteStateOnClear);
            Add(Read);
            Add(Write);
            Add(Clear);
        }

        public InputSet CreateInputSet()
        {
            return new InputSet
            {
                Read.With(new StorageRequest(StorageKey.Primary), "Read primary"),
                Read.With(new StorageRequest(StorageKey.Secondary), "Read secondary"),
                Write.With(new StorageRequest(StorageKey.Primary, StorageValue.One, ETagMode.Null), "Write primary value 1 with null ETag"),
                Write.With(new StorageRequest(StorageKey.Primary, StorageValue.Two, ETagMode.Current), "Write primary value 2 with current ETag"),
                Write.With(new StorageRequest(StorageKey.Secondary, StorageValue.One, ETagMode.Null), "Write secondary value 1 with null ETag"),
                Clear.With(new StorageRequest(StorageKey.Primary, etagMode: ETagMode.Current), "Clear primary with current ETag"),
                Write.With(new StorageRequest(StorageKey.Primary, StorageValue.Two, ETagMode.Null), "Write duplicate primary with null ETag"),
                Write.With(new StorageRequest(StorageKey.Primary, StorageValue.One, ETagMode.Stale), "Write primary with stale ETag"),
                Clear.With(new StorageRequest(StorageKey.Primary, etagMode: ETagMode.Stale), "Clear primary with stale ETag")
            };
        }

        public static bool CanApply(StorageRequest? request, GrainStorageBehavioralModelState state, bool deleteStateOnClear)
        {
            if (request == null)
            {
                return false;
            }

            var hasRecord = state.Records.TryGetValue(request.Key, out var record);
            return request.ETagMode switch
            {
                ETagMode.Null => true,
                ETagMode.Current => hasRecord && record is not null && ChangesState(request, record, deleteStateOnClear),
                ETagMode.Stale => hasRecord && record is not null && !string.IsNullOrEmpty(record.PreviousETag),
                _ => false
            };

            static bool ChangesState(StorageRequest request, GrainStorageBehavioralRecord record, bool deleteStateOnClear)
            {
                return request.Value == StorageValue.None
                    ? record.RecordExists || deleteStateOnClear
                    : request.Value != record.Value || !record.RecordExists;
            }
        }
    }

    private sealed class ReadOperation : Operation<StorageRequest, StorageOperationResult, GrainStorageBehavioralModelState>
    {
        public ReadOperation() : base("Read")
        {
        }

        public override ExpectedOutcomes Apply(StorageRequest request, GrainStorageBehavioralModelState state)
        {
            state.Records.TryGetValue(request.Key, out var record);
            return Expect.That(result => ValidateReadResult(request, record, result)).SameState();
        }

        public override Task<StorageOperationResult> ExecuteAsync(TestingContext context, StorageRequest request)
        {
            return context.Get<GrainStorageExecutionContext>().ReadAsync(request);
        }
    }

    private sealed class WriteOperation : Operation<StorageRequest, StorageOperationResult, GrainStorageBehavioralModelState>
    {
        public WriteOperation() : base("Write")
        {
        }

        public override ExpectedOutcomes Apply(StorageRequest request, GrainStorageBehavioralModelState state)
        {
            var hasRecord = state.Records.TryGetValue(request.Key, out var record);
            if ((request.ETagMode == ETagMode.Null && hasRecord) || request.ETagMode == ETagMode.Stale)
            {
                return Expect.That(
                        result => ValidateInconsistentStateResult(result, request))
                    .SameState();
            }

            return Expect.That(
                    result => ValidateWriteResult(request, record, result))
                .ThenState(
                    (result, nextState) =>
                    {
                        nextState.Records[request.Key] = new GrainStorageBehavioralRecord
                        {
                            Value = request.Value,
                            ETag = result.ETag,
                            PreviousETag = record?.ETag,
                            RecordExists = true
                        };
                    },
                    () => StorageOperationResult.Success(NewMockETag(), true, request.Value));
        }

        public override Task<StorageOperationResult> ExecuteAsync(TestingContext context, StorageRequest request)
        {
            return context.Get<GrainStorageExecutionContext>().WriteAsync(request);
        }
    }

    private sealed class ClearOperation : Operation<StorageRequest, StorageOperationResult, GrainStorageBehavioralModelState>
    {
        private readonly bool deleteStateOnClear;

        public ClearOperation(bool deleteStateOnClear) : base("Clear")
        {
            this.deleteStateOnClear = deleteStateOnClear;
        }

        public override ExpectedOutcomes Apply(StorageRequest request, GrainStorageBehavioralModelState state)
        {
            if (request.ETagMode == ETagMode.Stale)
            {
                return Expect.That(
                        result => ValidateInconsistentStateResult(result, request))
                    .SameState();
            }

            var record = state.Records[request.Key];
            return ClearedState(record);

            ExpectedOutcomes ClearedState(GrainStorageBehavioralRecord record)
            {
                var expectation = Expect.That(result => ValidateClearResult(result, record, deleteStateOnClear));
                if (deleteStateOnClear)
                {
                    return expectation.ThenState(nextState => nextState.Records.Remove(request.Key));
                }

                return expectation.ThenState(
                    (result, nextState) =>
                    {
                        nextState.Records[request.Key] = new GrainStorageBehavioralRecord
                        {
                            ETag = result.ETag,
                            PreviousETag = record.ETag,
                            Value = StorageValue.None,
                            RecordExists = false
                        };
                    },
                    () => StorageOperationResult.Success(NewMockETag(), false, StorageValue.None));
            }
        }

        public override Task<StorageOperationResult> ExecuteAsync(TestingContext context, StorageRequest request)
        {
            return context.Get<GrainStorageExecutionContext>().ClearAsync(request);
        }
    }

    private sealed class GrainStorageExecutionContext
    {
        private readonly Dictionary<string, GrainStorageBehavioralRecord> records = new();
        private readonly IGrainStorage storage;
        private readonly string grainType;
        private readonly string keyPrefix;
        private readonly CancellationToken cancellationToken;

        public GrainStorageExecutionContext(
            IGrainStorage storage,
            string grainType,
            string keyPrefix,
            CancellationToken cancellationToken)
        {
            this.storage = storage;
            this.grainType = grainType;
            this.keyPrefix = keyPrefix;
            this.cancellationToken = cancellationToken;
        }

        public async Task<StorageOperationResult> ReadAsync(StorageRequest request)
        {
            var grainState = new GrainState<TestState1> { State = new TestState1() };
            try
            {
                await storage.ReadStateAsync(
                    grainType,
                    ToGrainId(request.Key),
                    grainState,
                    cancellationToken);
                if (grainState.RecordExists)
                {
                    records[request.Key] = new GrainStorageBehavioralRecord
                    {
                        Value = ToStorageValue(grainState.State),
                        ETag = grainState.ETag,
                        PreviousETag = records.TryGetValue(request.Key, out var existing) ? existing.PreviousETag : null,
                        RecordExists = true
                    };
                }
                else if (!string.IsNullOrEmpty(grainState.ETag))
                {
                    records[request.Key] = new GrainStorageBehavioralRecord
                    {
                        Value = StorageValue.None,
                        ETag = grainState.ETag,
                        PreviousETag = records.TryGetValue(request.Key, out var existing) ? existing.PreviousETag : null,
                        RecordExists = false
                    };
                }

                return StorageOperationResult.Success(grainState.ETag, grainState.RecordExists, ToStorageValue(grainState.State));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                return StorageOperationResult.Failure(exception, grainState.ETag, grainState.RecordExists, ToStorageValue(grainState.State));
            }
        }

        public async Task<StorageOperationResult> WriteAsync(StorageRequest request)
        {
            var priorRecord = records.TryGetValue(request.Key, out var existing) ? existing : null;
            var grainState = new GrainState<TestState1>
            {
                State = CreateState(request.Value),
                ETag = ResolveETag(request),
                RecordExists = priorRecord?.RecordExists ?? false
            };

            try
            {
                await storage.WriteStateAsync(
                    grainType,
                    ToGrainId(request.Key),
                    grainState,
                    cancellationToken);
                records[request.Key] = new GrainStorageBehavioralRecord
                {
                    Value = request.Value,
                    ETag = grainState.ETag,
                    PreviousETag = priorRecord?.ETag,
                    RecordExists = true
                };

                return StorageOperationResult.Success(grainState.ETag, grainState.RecordExists, ToStorageValue(grainState.State));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                return StorageOperationResult.Failure(exception, grainState.ETag, grainState.RecordExists, ToStorageValue(grainState.State));
            }
        }

        public async Task<StorageOperationResult> ClearAsync(StorageRequest request)
        {
            var grainState = new GrainState<TestState1>
            {
                State = new TestState1(),
                ETag = ResolveETag(request),
                RecordExists = records.TryGetValue(request.Key, out var currentRecord) && currentRecord.RecordExists
            };

            try
            {
                var priorRecord = records.TryGetValue(request.Key, out var existing) ? existing : null;
                await storage.ClearStateAsync(
                    grainType,
                    ToGrainId(request.Key),
                    grainState,
                    cancellationToken);
                if (string.IsNullOrEmpty(grainState.ETag))
                {
                    records.Remove(request.Key);
                }
                else
                {
                    records[request.Key] = new GrainStorageBehavioralRecord
                    {
                        Value = StorageValue.None,
                        ETag = grainState.ETag,
                        PreviousETag = priorRecord?.ETag,
                        RecordExists = grainState.RecordExists
                    };
                }

                return StorageOperationResult.Success(grainState.ETag, grainState.RecordExists, ToStorageValue(grainState.State));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                return StorageOperationResult.Failure(exception, grainState.ETag, grainState.RecordExists, ToStorageValue(grainState.State));
            }
        }

        private GrainId ToGrainId(string key) => GrainId.Create(grainType, $"{keyPrefix}-{key}");

        private string? ResolveETag(StorageRequest request)
        {
            if (!records.TryGetValue(request.Key, out var record))
            {
                return request.ETagMode == ETagMode.Null ? null : $"{keyPrefix}-missing-etag";
            }

            return request.ETagMode switch
            {
                ETagMode.Null => null,
                ETagMode.Current => record.ETag,
                ETagMode.Stale => record.PreviousETag,
                _ => throw new InvalidOperationException($"Unsupported ETag mode: {request.ETagMode}")
            };
        }
    }

    private sealed class StorageRequest
    {
        public StorageRequest(string key, int value = StorageValue.None, ETagMode etagMode = ETagMode.Null)
        {
            Key = key;
            Value = value;
            ETagMode = etagMode;
        }

        public string Key { get; }

        public int Value { get; }

        public ETagMode ETagMode { get; }

        public override string ToString() => $"{Key}, value={Value}, etag={ETagMode}";
    }

    private sealed class StorageOperationResult
    {
        private StorageOperationResult(bool succeeded, Type? exceptionType, string? etag, bool recordExists, int value)
        {
            Succeeded = succeeded;
            ExceptionType = exceptionType;
            ETag = etag;
            RecordExists = recordExists;
            Value = value;
        }

        public bool Succeeded { get; }

        public Type? ExceptionType { get; }

        public string? ETag { get; }

        public bool RecordExists { get; }

        public int Value { get; }

        public static StorageOperationResult Success(string? etag, bool recordExists, int value)
        {
            return new StorageOperationResult(true, null, etag, recordExists, value);
        }

        public static StorageOperationResult Failure(Exception exception, string? etag, bool recordExists, int value)
        {
            return new StorageOperationResult(false, GetExceptionType(exception), etag, recordExists, value);
        }

        public override string ToString()
        {
            return Succeeded
                ? $"success etag={ETag ?? "<null>"} exists={RecordExists} value={Value}"
                : $"failure exception={ExceptionType?.FullName ?? "<null>"} etag={ETag ?? "<null>"} exists={RecordExists} value={Value}";
        }
    }

    private static Type GetExceptionType(Exception exception)
    {
        return exception is AggregateException { InnerExceptions.Count: 1 } aggregateException
            ? aggregateException.InnerExceptions[0].GetType()
            : exception.GetType();
    }

    private enum ETagMode
    {
        Null,
        Current,
        Stale
    }

    private static class StorageKey
    {
        public const string Primary = "primary";
        public const string Secondary = "secondary";
    }

    private static class StorageValue
    {
        public const int None = 0;
        public const int One = 1;
        public const int Two = 2;
    }

    private static ValidationResult ValidateReadResult(StorageRequest request, GrainStorageBehavioralRecord? record, StorageOperationResult result)
    {
        if (!result.Succeeded)
        {
            return ValidationResult.Invalid($"Read failed with {result.ExceptionType}");
        }

        if (record is null)
        {
            return result.RecordExists || result.ETag is not null || result.Value != StorageValue.None
                ? ValidationResult.Invalid($"Read missing {request.Key} returned etag={result.ETag}, exists={result.RecordExists}, value={result.Value}")
                : ValidationResult.Valid();
        }

        if (!record.RecordExists)
        {
            return !result.RecordExists && result.ETag == record.ETag && result.Value == StorageValue.None
                ? ValidationResult.Valid()
                : ValidationResult.Invalid($"Read cleared {request.Key} returned etag={result.ETag}, exists={result.RecordExists}, value={result.Value}; expected etag={record.ETag}, exists=false, value={StorageValue.None}");
        }

        return result.RecordExists && result.ETag == record.ETag && result.Value == record.Value
            ? ValidationResult.Valid()
            : ValidationResult.Invalid($"Read {request.Key} returned etag={result.ETag}, exists={result.RecordExists}, value={result.Value}; expected etag={record.ETag}, exists=true, value={record.Value}");
    }

    private static ValidationResult ValidateWriteResult(
        StorageRequest request,
        GrainStorageBehavioralRecord? previousRecord,
        StorageOperationResult result)
    {
        if (!result.Succeeded)
        {
            return ValidationResult.Invalid($"Write failed with {result.ExceptionType}");
        }

        if (!result.RecordExists)
        {
            return ValidationResult.Invalid("Successful write did not mark the record as existing.");
        }

        if (string.IsNullOrEmpty(result.ETag))
        {
            return ValidationResult.Invalid("Successful write did not return a non-empty ETag.");
        }

        if (previousRecord is not null && result.ETag == previousRecord.ETag)
        {
            return ValidationResult.Invalid($"Successful write reused ETag {result.ETag}.");
        }

        return result.Value == request.Value
            ? ValidationResult.Valid()
            : ValidationResult.Invalid($"Write returned value {result.Value}; expected {request.Value}.");
    }

    private static ValidationResult ValidateClearResult(StorageOperationResult result, GrainStorageBehavioralRecord previousRecord, bool deleteStateOnClear)
    {
        if (!result.Succeeded)
        {
            return ValidationResult.Invalid($"Clear failed with {result.ExceptionType}");
        }

        if (result.RecordExists || result.Value != StorageValue.None)
        {
            return ValidationResult.Invalid($"Clear returned etag={result.ETag}, exists={result.RecordExists}, value={result.Value}; expected exists=false and value={StorageValue.None}.");
        }

        if (deleteStateOnClear)
        {
            return result.ETag is null
                ? ValidationResult.Valid()
                : ValidationResult.Invalid($"Clear returned etag={result.ETag}; expected null because the provider deletes state on clear.");
        }

        if (string.IsNullOrEmpty(result.ETag))
        {
            return ValidationResult.Invalid("Clear returned a null or empty ETag; expected a retained cleared-state ETag.");
        }

        return result.ETag != previousRecord.ETag
            ? ValidationResult.Valid()
            : ValidationResult.Invalid($"Clear reused ETag {result.ETag}.");
    }

    private static ValidationResult ValidateInconsistentStateResult(StorageOperationResult result, StorageRequest request)
    {
        if (result.Succeeded)
        {
            return ValidationResult.Invalid($"{request.ETagMode} ETag operation succeeded unexpectedly.");
        }

        return result.ExceptionType is not null && typeof(InconsistentStateException).IsAssignableFrom(result.ExceptionType)
            ? ValidationResult.Valid()
            : ValidationResult.Invalid($"{request.ETagMode} ETag operation failed with {result.ExceptionType?.FullName ?? "<null>"}; expected {typeof(InconsistentStateException).FullName}.");
    }

    private static TestState1 CreateState(int value)
    {
        return value == StorageValue.None
            ? new TestState1()
            : new TestState1 { A = $"value-{value}", B = value, C = value * 100L };
    }

    private static int ToStorageValue(TestState1? state)
    {
        return state?.B ?? StorageValue.None;
    }

    private static string NewMockETag() => Guid.NewGuid().ToString("N");
}

[State]
internal partial class GrainStorageBehavioralModelState : State
{
    public Dictionary<string, GrainStorageBehavioralRecord> Records { get; set; } = new();
}

[State]
internal partial class GrainStorageBehavioralRecord : State
{
    public string? ETag { get; set; }

    public string? PreviousETag { get; set; }

    public int Value { get; set; }

    public bool RecordExists { get; set; }
}
