using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Runtime;
using Orleans.Serialization.Serializers;
using Orleans.Storage.Internal;

namespace Orleans.Storage
{
    /// <summary>
    /// 这是储存提供者在存储器的简单(Q0Q)执行中的一种简单的模拟 grain 。
    /// </summary>
    /// <remarks>
    /// 该储存提供者仅用于简单的模拟开发/单元测试设想。
    /// 这一类不应用于生产环境,
    ///  由于[按设计 无法提供任何抗御力
    ///  或长期持久性能力。
    /// </remarks>
    [DebuggerDisplay("MemoryStore:{" + nameof(name) + "}")]
    public partial class MemoryGrainStorage : IGrainStorage, IDisposable
    {
        private Lazy<IMemoryStorageGrain>[] storageGrains;
        private readonly ILogger logger;
        private readonly IActivatorProvider _activatorProvider;
        private readonly IGrainStorageSerializer storageSerializer;
        private readonly bool _overridesLegacyReadState;
        private readonly bool _overridesLegacyWriteState;
        private readonly bool _overridesLegacyClearState;

        /// <summary> 此存储提供者实例的名称。 </summary>
        private readonly string name;

        /// <summary>
        /// 初始化 <see cref="MemoryGrainStorage"/> 类的新实例。
        /// </summary>
        /// <param name="name">名称。</param>
        /// <param name="options">选项。</param>
        /// <param name="logger">日志器。</param>
        /// <param name="grainFactory">下 grain 工厂。</param>
        /// <param name="defaultGrainStorageSerializer">默认默认 grain 存储序列器。</param>
        /// <param name="activatorProvider">用于创建 grain 状态实例的提供者。</param>
        [SuppressMessage("Design", "CA1062:Validate arguments of public methods", Justification = "Microsoft.Extensions.DependencyInjection supplies the resolved options instance.")]
        public MemoryGrainStorage(
            string name,
            MemoryGrainStorageOptions options,
            ILogger<MemoryGrainStorage> logger,
            IGrainFactory grainFactory,
            IGrainStorageSerializer defaultGrainStorageSerializer,
            IActivatorProvider activatorProvider)
        {
            this.name = name;
            this.logger = logger;
            _activatorProvider = activatorProvider;
            this.storageSerializer = options.GrainStorageSerializer ?? defaultGrainStorageSerializer;

            LogDebugInit(name, options.NumStorageGrains);
            storageGrains = new Lazy<IMemoryStorageGrain>[options.NumStorageGrains];
            for (int i = 0; i < storageGrains.Length; i++)
            {
                int idx = i; // 避免修改关闭错误的抓取变量
                storageGrains[idx] = new Lazy<IMemoryStorageGrain>(() => grainFactory.GetGrain<IMemoryStorageGrain>(idx));
            }

            var runtimeType = GetType();
            _overridesLegacyReadState = OverridesLegacyMethod(runtimeType, nameof(ReadStateAsync));
            _overridesLegacyWriteState = OverridesLegacyMethod(runtimeType, nameof(WriteStateAsync));
            _overridesLegacyClearState = OverridesLegacyMethod(runtimeType, nameof(ClearStateAsync));
        }

        /// <inheritdoc/>
        public virtual Task ReadStateAsync<T>(string grainType, GrainId grainId, IGrainState<T> grainState)
        {
            ArgumentNullException.ThrowIfNull(grainType);
            ArgumentNullException.ThrowIfNull(grainState);
            return ReadStateCoreAsync(grainType, grainId, grainState, CancellationToken.None);
        }

        /// <inheritdoc/>
        public virtual async Task ReadStateAsync<T>(
            string grainType,
            GrainId grainId,
            IGrainState<T> grainState,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(grainType);
            ArgumentNullException.ThrowIfNull(grainState);
            if (_overridesLegacyReadState)
            {
                await ReadStateAsync(grainType, grainId, grainState).WaitAsync(cancellationToken);
                return;
            }

            await ReadStateCoreAsync(grainType, grainId, grainState, cancellationToken);
        }

        private async Task ReadStateCoreAsync<T>(
            string grainType,
            GrainId grainId,
            IGrainState<T> grainState,
            CancellationToken cancellationToken)
        {
            var key = MakeKey(grainType, grainId);

            LogTraceRead(key);
            IMemoryStorageGrain storageGrain = GetStorageGrain(key);
            var state = await storageGrain.ReadStateAsync<ReadOnlyMemory<byte>>(key, cancellationToken);
            if (state != null)
            {
                var loadedState = ConvertFromStorageFormat<T>(state.State);
                grainState.ETag = state.ETag;
                grainState.State = loadedState ?? CreateInstance<T>();
                grainState.RecordExists = loadedState != null;
            }
            else
            {
                grainState.ETag = null;
                grainState.State = CreateInstance<T>();
                grainState.RecordExists = false;
            }
        }

        /// <inheritdoc/>
        public virtual Task WriteStateAsync<T>(string grainType, GrainId grainId, IGrainState<T> grainState)
        {
            ArgumentNullException.ThrowIfNull(grainType);
            ArgumentNullException.ThrowIfNull(grainState);
            return WriteStateCoreAsync(grainType, grainId, grainState, CancellationToken.None);
        }

        /// <inheritdoc/>
        public virtual async Task WriteStateAsync<T>(
            string grainType,
            GrainId grainId,
            IGrainState<T> grainState,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(grainType);
            ArgumentNullException.ThrowIfNull(grainState);
            if (_overridesLegacyWriteState)
            {
                await WriteStateAsync(grainType, grainId, grainState).WaitAsync(cancellationToken);
                return;
            }

            await WriteStateCoreAsync(grainType, grainId, grainState, cancellationToken);
        }

        private async Task WriteStateCoreAsync<T>(
            string grainType,
            GrainId grainId,
            IGrainState<T> grainState,
            CancellationToken cancellationToken)
        {
            var key = MakeKey(grainType, grainId);
            LogTraceWrite(key, grainState.State, grainState.ETag);
            IMemoryStorageGrain storageGrain = GetStorageGrain(key);
            try
            {
                var data = ConvertToStorageFormat<T>(grainState.State);
                var binaryGrainState = new GrainState<ReadOnlyMemory<byte>>(data, grainState.ETag)
                {
                    RecordExists = grainState.RecordExists
                };
                grainState.ETag = await storageGrain.WriteStateAsync(key, binaryGrainState, cancellationToken);
                grainState.RecordExists = true;
            }
            catch (MemoryStorageEtagMismatchException e)
            {
                throw e.AsInconsistentStateException();
            }
        }

        /// <inheritdoc/>
        public virtual Task ClearStateAsync<T>(string grainType, GrainId grainId, IGrainState<T> grainState)
        {
            ArgumentNullException.ThrowIfNull(grainType);
            ArgumentNullException.ThrowIfNull(grainState);
            return ClearStateCoreAsync(grainType, grainId, grainState, CancellationToken.None);
        }

        /// <inheritdoc/>
        public virtual async Task ClearStateAsync<T>(
            string grainType,
            GrainId grainId,
            IGrainState<T> grainState,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(grainType);
            ArgumentNullException.ThrowIfNull(grainState);
            if (_overridesLegacyClearState)
            {
                await ClearStateAsync(grainType, grainId, grainState).WaitAsync(cancellationToken);
                return;
            }

            await ClearStateCoreAsync(grainType, grainId, grainState, cancellationToken);
        }

        private async Task ClearStateCoreAsync<T>(
            string grainType,
            GrainId grainId,
            IGrainState<T> grainState,
            CancellationToken cancellationToken)
        {
            var key = MakeKey(grainType, grainId);
            LogTraceDelete(key, grainState.ETag);
            IMemoryStorageGrain storageGrain = GetStorageGrain(key);
            try
            {
                await storageGrain.DeleteStateAsync<ReadOnlyMemory<byte>>(key, grainState.ETag, cancellationToken);
                grainState.ETag = null;
                grainState.RecordExists = false;
                grainState.State = CreateInstance<T>();
            }
            catch (MemoryStorageEtagMismatchException e)
            {
                throw e.AsInconsistentStateException();
            }
        }

        private static string MakeKey(string grainType, GrainId grainId) => $"{grainType}/{grainId}";

        private static bool OverridesLegacyMethod(Type runtimeType, string methodName)
        {
            foreach (var method in runtimeType.GetMethods())
            {
                if (method.Name == methodName
                    && method.IsGenericMethodDefinition
                    && method.GetParameters().Length == 3
                    && method.DeclaringType != typeof(MemoryGrainStorage)
                    && method.GetBaseDefinition().DeclaringType == typeof(MemoryGrainStorage))
                {
                    return true;
                }
            }

            return false;
        }

        private IMemoryStorageGrain GetStorageGrain(string id)
        {
            var idx = StableHash.ComputeHash(id) % (uint)storageGrains.Length;
            return storageGrains[idx].Value;
        }

        /// <inheritdoc/>
        public void Dispose() { }

        /// <summary>
        /// 从二进制数据进行脱序
        /// </summary>
        /// <param name="data">序列存储数据</param>
        internal T? ConvertFromStorageFormat<T>(ReadOnlyMemory<byte> data)
        {
            T? dataValue = default;
            try
            {
                dataValue = this.storageSerializer.Deserialize<T>(data);
            }
            catch (Exception exc)
            {
                var sb = new StringBuilder();
                if (data.ToArray().Length > 0)
                {
                    sb.AppendFormat("Unable to convert from storage format GrainStateEntity.Data={0}", data);
                }

                if (dataValue != null)
                {
                    sb.AppendFormat("Data Value={0} Type={1}", dataValue, dataValue.GetType());
                }
                LogError(sb, exc);
                throw new AggregateException(sb.ToString(), exc);
            }

            return dataValue;
        }

        /// <summary>
        /// 序列化为存储格式。
        /// </summary>
        /// <param name="grainState"> grain 国家数据将按序号</param>
        /// <remarks>
        /// 见:
        /// http://msdn.microsoft.com/en-us/library/system.web.script.serialization.javascriptserializer.aspx
        /// 更多 JSON 连环器
        /// </remarks>
        internal ReadOnlyMemory<byte> ConvertToStorageFormat<T>(T? grainState)
        {
            // 转换为二进制格式
            return this.storageSerializer.Serialize<T>(grainState);
        }

        private T CreateInstance<T>() => _activatorProvider.GetActivator<T>().Create();

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Init: Name={Name} NumStorageGrains={NumStorageGrains}"
        )]
        private partial void LogDebugInit(string name, int numStorageGrains);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "Read Keys={Keys}"
        )]
        private partial void LogTraceRead(string keys);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "Write Keys={Keys} Data={Data} Etag={Etag}"
        )]
        private partial void LogTraceWrite(string keys, object? data, string? etag);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "Delete Keys={Keys} Etag={Etag}"
        )]
        private partial void LogTraceDelete(string keys, string? etag);

        [LoggerMessage(
            Level = LogLevel.Error,
            Message = "{Message}"
        )]
        private partial void LogError(StringBuilder message, Exception exception);
    }

    /// <summary>
    /// 创建 MemoryGrainStorage工厂
    /// </summary>
    public static class MemoryGrainStorageFactory
    {
        /// <summary>
        /// 创建新的 <see cref="MemoryGrainStorage"/> 实例。
        /// </summary>
        /// <param name="services">服务。</param>
        /// <param name="name">名称。</param>
        /// <returns>储存。</returns>
        public static MemoryGrainStorage Create(IServiceProvider services, string name)
        {
            return ActivatorUtilities.CreateInstance<MemoryGrainStorage>(services,
                services.GetRequiredService<IOptionsMonitor<MemoryGrainStorageOptions>>().Get(name), name);
        }
    }
}
