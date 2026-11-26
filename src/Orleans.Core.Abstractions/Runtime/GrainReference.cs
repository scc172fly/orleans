using System;
using System.Reflection;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Orleans.Serialization.Cloning;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.Invocation;
using Orleans.Serialization.Serializers;
using Microsoft.Extensions.DependencyInjection;
using Orleans.CodeGeneration;
using System.Text;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading;

namespace Orleans.Runtime
{
    /// <summary>
    /// 共同属性为 <see cref="GrainReference"/> 同一 <see cref="GrainType"/> 和 <see cref="GrainInterfaceType"/>.
    /// </summary>
    public class GrainReferenceShared
    {
        /// <summary>
        /// 初始化 <see cref="GrainReferenceShared"/> 类的新实例。
        /// </summary>
        /// <param name="grainType"> grain 类型。</param>
        /// <param name="grainInterfaceType"> grain 接口类型。</param>
        /// <param name="interfaceVersion"> grain 接口版本。</param>
        /// <param name="runtime">参考 grain 时间。</param>
        /// <param name="invokeMethodOptions">通用的援引选项。</param>
        /// <param name="codecProvider">序列编码提供商</param>
        /// <param name="copyContextPool">序列复制背景池。</param>
        /// <param name="serviceProvider">服务供应商。</param>
        public GrainReferenceShared(
            GrainType grainType,
            GrainInterfaceType grainInterfaceType,
            ushort interfaceVersion,
            IGrainReferenceRuntime runtime,
            InvokeMethodOptions invokeMethodOptions,
            CodecProvider codecProvider,
            CopyContextPool copyContextPool,
            IServiceProvider serviceProvider)
        {
            this.GrainType = grainType;
            this.InterfaceType = grainInterfaceType;
            this.Runtime = runtime;
            this.InvokeMethodOptions = invokeMethodOptions;
            this.CodecProvider = codecProvider;
            this.CopyContextPool = copyContextPool;
            this.ServiceProvider = serviceProvider;
            this.InterfaceVersion = interfaceVersion;
        }

        /// <summary>
        /// 获取 grain 引用运行时间。
        /// </summary>
        public IGrainReferenceRuntime Runtime { get; }

        /// <summary>
        /// 获取 grain 类型。
        /// </summary>
        public GrainType GrainType { get; }

        /// <summary>
        /// 获得接口类型。
        /// </summary>
        public GrainInterfaceType InterfaceType { get; }

        /// <summary>
        /// 获得通用的援引选项。
        /// </summary>
        public InvokeMethodOptions InvokeMethodOptions { get; }

        /// <summary>
        /// 获得序列编码提供商。
        /// </summary>
        public CodecProvider CodecProvider { get; }

        /// <summary>
        /// 获得序列复制背景库。
        /// </summary>
        public CopyContextPool CopyContextPool { get; }

        /// <summary>
        /// 得到了服务供应商。
        /// </summary>
        public IServiceProvider ServiceProvider { get; }

        /// <summary>
        /// 获取接口版本。
        /// </summary>
        public ushort InterfaceVersion { get; }
    }

    /// <summary>
    /// 序列化和取消 <see cref="GrainReference"/> 和衍生类型功能。
    /// </summary>
    [RegisterSerializer]
    internal class GrainReferenceCodec : GeneralizedReferenceTypeSurrogateCodec<IAddressable, GrainReferenceSurrogate>
    {
        private readonly IGrainFactory _grainFactory;

        /// <summary>
        /// 初始化 <see cref="GrainReferenceCodec"/> 类的新实例。
        /// </summary>
        /// <param name="grainFactory">下 grain 工厂。</param>
        /// <param name="surrogateSerializer">本类使用的代用类型序列器的序列器。</param>
        public GrainReferenceCodec(IGrainFactory grainFactory, IValueSerializer<GrainReferenceSurrogate> surrogateSerializer)
            : base(surrogateSerializer)
        {
            _grainFactory = grainFactory;
        }

        /// <inheritdoc/>
        public override IAddressable ConvertFromSurrogate(ref GrainReferenceSurrogate surrogate)
        {
            return _grainFactory.GetGrain(surrogate.GrainId, surrogate.GrainInterfaceType);
        }

        /// <inheritdoc/>
        public override void ConvertToSurrogate(IAddressable value, ref GrainReferenceSurrogate surrogate)
        {
            var refValue = value.AsReference();
            surrogate.GrainId = refValue.GrainId;
            surrogate.GrainInterfaceType = refValue.InterfaceType;
        }
    }

    [RegisterCopier]
    internal sealed class GrainReferenceCopier : ShallowCopier<GrainReference>, IDerivedTypeCopier { }

    /// <summary>
    /// 提供 grain 参考类型的专门复印机实例。
    /// </summary>
    internal class GrainReferenceCopierProvider : ISpecializableCopier
    {
        /// <inheritdoc/>
        // ! Activator.CreateInstance 不会因这些论点而返回无效。
        public IDeepCopier GetSpecializedCopier(Type type) => (IDeepCopier)Activator.CreateInstance(typeof(TypedGrainReferenceCopier<>).MakeGenericType(type))!;

        /// <inheritdoc/>
        public bool IsSupportedType(Type type) => typeof(IAddressable).IsAssignableFrom(type) && type.IsInterface;
    }

    /// <summary>
    /// grain 参考实例的强型复印机。
    /// </summary>
    /// <typeparam name="TInterface"> grain 接口类型。</typeparam>
    internal class TypedGrainReferenceCopier<TInterface> : IDeepCopier<TInterface>
    {
        /// <inheritdoc/>
        [return: NotNullIfNotNull(nameof(input))]
        public TInterface? DeepCopy(TInterface? input, CopyContext context)
        {
            if (input is null) return input;
            if (input is GrainReference) return input;
            if (input is IGrainObserver observer)
            {
                GrainReferenceCodecProvider.ThrowGrainObserverInvalidException(observer);
            }

            var addressable = (IAddressable)input;
            var grainReference = addressable.AsReference();
            return (TInterface)grainReference.Runtime.Cast(addressable, typeof(TInterface));
        }
    }

    /// <summary>
    /// 为 grain 参考类型提供专门的编码实例。
    /// </summary>
    internal class GrainReferenceCodecProvider : ISpecializableCodec
    {
        private readonly IServiceProvider _serviceProvider;

        /// <summary>
        /// 初始化 <see cref="GrainReferenceCodecProvider"/> 类的新实例。
        /// </summary>
        /// <param name="serviceProvider">服务供应商。</param>
        public GrainReferenceCodecProvider(IServiceProvider serviceProvider) => _serviceProvider = serviceProvider;

        /// <inheritdoc/>
        public IFieldCodec GetSpecializedCodec(Type type) => (IFieldCodec)ActivatorUtilities.GetServiceOrCreateInstance(_serviceProvider, typeof(TypedGrainReferenceCodec<>).MakeGenericType(type));

        /// <inheritdoc/>
        public bool IsSupportedType(Type type) => typeof(IAddressable).IsAssignableFrom(type);

        /// <summary>
        /// 丢弃一个例外, 显示参数类型不支持 。
        /// </summary>
        /// <param name="observer">观察员</param>
        internal static void ThrowGrainObserverInvalidException(IGrainObserver observer)
            => throw new NotSupportedException($"IGrainObserver parameters must be GrainReference or Grain and cannot be type {observer.GetType()}. Did you forget to CreateObjectReference?");
    }

    /// <summary>
    /// grain 参考实例的强型编码器。
    /// </summary>
    /// <typeparam name="T">下 grain 参考界面类型。</typeparam>
    internal class TypedGrainReferenceCodec<T> : GeneralizedReferenceTypeSurrogateCodec<T, GrainReferenceSurrogate>
        where T : class, IAddressable
    {
        private readonly IGrainFactory _grainFactory;

        /// <summary>
        /// 初始化 <see cref="TypedGrainReferenceCodec{T}"/> 类的新实例。
        /// </summary>
        /// <param name="grainFactory">下 grain 工厂。</param>
        /// <param name="surrogateSerializer">代孕序列器</param>
        public TypedGrainReferenceCodec(IGrainFactory grainFactory, IValueSerializer<GrainReferenceSurrogate> surrogateSerializer) : base(surrogateSerializer)
        {
            _grainFactory = grainFactory;
        }

        /// <inheritdoc/>
        public override T ConvertFromSurrogate(ref GrainReferenceSurrogate surrogate)
        {
            return (T)_grainFactory.GetGrain(surrogate.GrainId, surrogate.GrainInterfaceType);
        }

        /// <inheritdoc/>
        public override void ConvertToSurrogate(T value, ref GrainReferenceSurrogate surrogate)
        {
            // 在进行费用更高的接口检查前先检查典型的错误案件是否是假的
            if (value is not GrainReference refValue)
            {
                if (value is IGrainObserver observer)
                {
                    GrainReferenceCodecProvider.ThrowGrainObserverInvalidException(observer);
                }

                refValue = (GrainReference)(object)value.AsReference<T>();
            }

            surrogate.GrainId = refValue.GrainId;
            surrogate.GrainInterfaceType = refValue.InterfaceType;
        }
    }

    /// <summary>
    /// 代表 <see cref="GrainReference"/> 履行序列化的替代物。
    /// </summary>
    [GenerateSerializer]
    internal struct GrainReferenceSurrogate
    {
        /// <summary>
        /// 获取或设置 grain id。
        /// </summary>
        [Id(0)]
        public GrainId GrainId;

        /// <summary>
        /// 获取或设置 grain 接口类型。
        /// </summary>
        [Id(1)]
        public GrainInterfaceType GrainInterfaceType;
    }

    /// <summary>
    /// 这是所有 grain 引用的基准类。
    /// </summary>
    [Alias("GrainRef")]
    [DefaultInvokableBaseType(typeof(ValueTask<>), typeof(Request<>))]
    [DefaultInvokableBaseType(typeof(ValueTask), typeof(Request))]
    [DefaultInvokableBaseType(typeof(Task<>), typeof(TaskRequest<>))]
    [DefaultInvokableBaseType(typeof(Task), typeof(TaskRequest))]
    [DefaultInvokableBaseType(typeof(void), typeof(VoidRequest))]
    [DefaultInvokableBaseType(typeof(IAsyncEnumerable<>), typeof(AsyncEnumerableRequest<>))]
    public class GrainReference : IAddressable, IEquatable<GrainReference>, ISpanFormattable
    {
        /// <summary>
        /// 下 grain 由所有人共享的参考功能 grain 指定类型的引用。
        /// </summary>
        [NonSerialized]
        private readonly GrainReferenceShared _shared;

        /// <summary>
        /// 底部 grain id 密钥。
        /// </summary>
        [NonSerialized]
        private readonly IdSpan _key;

        /// <summary>
        /// 得到 grain 由所有人共享的参考功能 grain 指定类型的引用。
        /// </summary>
        internal GrainReferenceShared Shared => _shared ?? throw new GrainReferenceNotBoundException(this);

        /// <summary>
        /// 获取 grain 引用运行时间。
        /// </summary>
        internal IGrainReferenceRuntime Runtime => Shared.Runtime;

        /// <summary>
        /// 获取 grain id。
        /// </summary>
        public GrainId GrainId => GrainId.Create(_shared.GrainType, _key);

        /// <summary>
        /// 获得接口类型。
        /// </summary>
        public GrainInterfaceType InterfaceType => _shared.InterfaceType;

        /// <summary>
        /// 获得序列复制背景库。
        /// </summary>
        protected CopyContextPool CopyContextPool => _shared.CopyContextPool;

        /// <summary>
        /// 获得序列编码提供商。
        /// </summary>
        protected CodecProvider CodecProvider => _shared.CodecProvider;

        /// <summary>初始化一个 <see cref="GrainReference"/> 类的新实例。</summary>
        /// <param name="shared">
        /// 下 grain 由所有人共享的参考功能 grain 指定类型的引用。
        /// </param>
        /// <param name="key">
        /// grain id 的密钥部分。
        /// </param>
        protected GrainReference(GrainReferenceShared shared, IdSpan key)
        {
            _shared = shared;
            _key = key;
        }

        /// <summary>
        /// 创建新 <see cref="GrainReference"/> 指定 <paramref name="grainId"/>.
        /// </summary>
        internal static GrainReference FromGrainId(GrainReferenceShared shared, GrainId grainId) => new(shared, grainId.Key);

        /// <summary>
        /// 创建新的 grain 引用,以实施指定的 grain 接口。
        /// </summary>
        /// <typeparam name="TGrainInterface">
        /// grain 接口类型。
        /// </typeparam>
        /// <returns>一个新的 grain 参考,用于执行指定的接口类型。</returns>
        public virtual TGrainInterface Cast<TGrainInterface>()
            where TGrainInterface : IAddressable
            => (TGrainInterface)_shared.Runtime.Cast(this, typeof(TGrainInterface));

        /// <summary>
        /// 测试此引用是否等于其它对象。
        /// 两 grain 参考文献如果都指同一种 grain,则相等。
        /// </summary>
        /// <param name="obj">对照此参考测试平等性的目标。</param>
        /// <returns><c>true</c> 。</returns>
        public override bool Equals(object? obj)
        {
            return Equals(obj as GrainReference);
        }

        /// <inheritdoc />
        public bool Equals(GrainReference? other) => other is not null && this.GrainId.Equals(other.GrainId);

        /// <inheritdoc />
        public override int GetHashCode() => this.GrainId.GetHashCode();

        /// <summary>
        /// 获取此 grain 引用的统一散列代码。
        /// </summary>
        /// <returns>
        /// 统一的大麻代号
        /// </returns>
        public uint GetUniformHashCode()
        {
            // GrainId 已经包括通用参数的散列类型代码。
            return GrainId.GetUniformHashCode();
        }

        /// <summary>
        /// 将平等的两个参考文献进行比较。
        /// 两 grain 参考文献如果都指同一种 grain,则相等。
        /// </summary>
        /// <param name="reference1">第一次 grain 引用比较。</param>
        /// <param name="reference2">第二届 grain 比较引用。</param>
        /// <returns><c>true</c> 如果两者兼有, grain 参考引用 grain (按 grain identifier).</returns>
        public static bool operator ==(GrainReference? reference1, GrainReference? reference2)
        {
            if (reference1 is null) return reference2 is null;

            return reference1.Equals(reference2);
        }

        /// <summary>
        /// 将不平等的两处参考比对。
        /// 两 grain 参考文献如果都指同一种 grain,则相等。
        /// </summary>
        /// <param name="reference1">第一次 grain 引用比较。</param>
        /// <param name="reference2">第二届 grain 比较引用。</param>
        /// <returns><c>false</c> 如果两者兼有, grain 解决到相同的引用 grain (按 grain identifier).</returns>
        public static bool operator !=(GrainReference? reference1, GrainReference? reference2)
        {
            if (reference1 is null) return !(reference2 is null);

            return !reference1.Equals(reference2);
        }

        /// <summary>
        /// 获取接口版本。
        /// </summary>
        public ushort InterfaceVersion => Shared.InterfaceVersion;

        /// <summary>
        /// 获取接口名称。
        /// </summary>
        public virtual string InterfaceName => InterfaceType.ToString();

        /// <inheritdoc/>
        public sealed override string ToString() => $"GrainReference:{GrainId}:{InterfaceType}";

        string IFormattable.ToString(string? format, IFormatProvider? formatProvider) => ToString();

        bool ISpanFormattable.TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
            => destination.TryWrite($"GrainReference:{GrainId}:{InterfaceType}", out charsWritten);

        /// <summary>
        /// 获取或创建可引用的请求实例。
        /// </summary>
        /// <typeparam name="TInvokable">可置疑的请求类型。</typeparam>
        /// <returns>一个可援引的请求实例。</returns>
        protected TInvokable GetInvokable<TInvokable>() => ActivatorUtilities.GetServiceOrCreateInstance<TInvokable>(Shared.ServiceProvider);

        /// <summary>
        /// 启用提供的方法。
        /// </summary>
        /// <typeparam name="T">基本方法返回类型。</typeparam>
        /// <param name="methodDescription">方法说明。</param>
        /// <returns>援引的结果。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="methodDescription"/> 是 <see langword="null"/>.</exception>
        protected ValueTask<T?> InvokeAsync<T>(IRequest methodDescription)
        {
            ArgumentNullException.ThrowIfNull(methodDescription);
            return this.Runtime.InvokeMethodAsync<T>(this, methodDescription, methodDescription.Options);
        }

        /// <summary>
        /// 启用提供的方法。
        /// </summary>
        /// <param name="methodDescription">方法说明。</param>
        /// <returns>A <see cref="ValueTask"/> 代表操作。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="methodDescription"/> 是 <see langword="null"/>.</exception>
        protected ValueTask InvokeAsync(IRequest methodDescription)
        {
            ArgumentNullException.ThrowIfNull(methodDescription);
            return this.Runtime.InvokeMethodAsync(this, methodDescription, methodDescription.Options);
        }

        /// <summary>
        /// 启用提供的方法。
        /// </summary>
        /// <param name="methodDescription">方法说明。</param>
        /// <exception cref="ArgumentNullException"><paramref name="methodDescription"/> 是 <see langword="null"/>.</exception>
        protected void Invoke(IRequest methodDescription)
        {
            ArgumentNullException.ThrowIfNull(methodDescription);
            this.Runtime.InvokeMethod(this, methodDescription, methodDescription.Options);
        }
    }

    /// <summary>
    /// 代表对 grain.采用某一方法的请求。
    /// </summary>
    public interface IRequest : IInvokable
    {
        /// <summary>
        /// 获得援引选项。
        /// </summary>
        InvokeMethodOptions Options { get; }

        /// <summary>
        /// 纳入了所提供的调用选项。
        /// </summary>
        /// <param name="options">
        /// 选项。
        /// </param>
        void AddInvokeMethodOptions(InvokeMethodOptions options);

        /// <summary>
        /// 返回请求的字符串表示。
        /// </summary>
        /// <returns>请求的字符串表示。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="request"/> 是 <see langword="null"/>.</exception>
        public static string ToString(IRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            var result = new StringBuilder();
            result.Append(request.GetInterfaceName());
            if (request.GetTarget() is { } target)
            {
                result.Append("[(");
                result.Append(request.GetInterfaceName());
                result.Append(')');
                result.Append(target.ToString());
                result.Append(']');
            }

            result.Append('.');
            result.Append(request.GetMethodName());
            result.Append('(');
            var argTypes = request.GetMethod()?.GetParameters();
            if (argTypes is not null)
            {
                var argumentCount = argTypes.Length;
                for (var n = 0; n < argumentCount; n++)
                {
                    if (n > 0)
                    {
                        result.Append(", ");
                    }

                    result.Append(argTypes[n].ParameterType);
                }
            }
            else
            {
                var argumentCount = request.GetArgumentCount();
                for (var n = 0; n < argumentCount; n++)
                {
                    if (n > 0)
                    {
                        result.Append(", ");
                    }

                    result.Append(request.GetArgument(n)?.GetType()?.ToString() ?? "null");
                }
            }

            result.Append(')');
            return result.ToString();
        }

        /// <summary>
        /// 返回请求的字符串表示。
        /// </summary>
        /// <returns>请求的字符串表示。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="request"/> 是 <see langword="null"/>.</exception>
        public static string ToMethodCallString(IRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            var result = new StringBuilder();
            result.Append(request.GetInterfaceName());
            result.Append('.');
            result.Append(request.GetMethodName());
            result.Append('(');
            var argumentCount = request.GetArgumentCount();
            for (var n = 0; n < argumentCount; n++)
            {
                if (n > 0)
                {
                    result.Append(", ");
                }

                result.Append(request.GetArgument(n));
            }

            result.Append(')');
            return result.ToString();
        }
    }

    /// <summary>
    /// 用于方法请求的基础类型。
    /// </summary>
    [SuppressReferenceTracking]
    [SerializerTransparent]
    public abstract class RequestBase : IRequest
    {
        /// <summary>
        /// 获得援引选项。
        /// </summary>
        [field: NonSerialized]
        public InvokeMethodOptions Options { get; protected set; }

        /// <inheritdoc/>
        public virtual int GetArgumentCount() => 0;

        /// <summary>
        /// 纳入了所提供的调用选项。
        /// </summary>
        /// <param name="options">
        /// 选项。
        /// </param>
        public void AddInvokeMethodOptions(InvokeMethodOptions options)
        {
            Options |= options;
        }

        /// <inheritdoc/>
        public abstract ValueTask<Response> Invoke();

        /// <inheritdoc/>
        public abstract object GetTarget();

        /// <inheritdoc/>
        public abstract void SetTarget(ITargetHolder holder);

        /// <inheritdoc/>
        public virtual object GetArgument(int index) => throw new ArgumentOutOfRangeException(message: "The request has zero arguments", null);

        /// <inheritdoc/>
        public virtual void SetArgument(int index, object value) => throw new ArgumentOutOfRangeException(message: "The request has zero arguments", null);

        /// <inheritdoc/>
        public abstract void Dispose();

        /// <inheritdoc/>
        public abstract string GetMethodName();

        /// <inheritdoc/>
        public abstract string GetInterfaceName();

        /// <inheritdoc/>
        public abstract string GetActivityName();

        /// <inheritdoc/>
        public abstract Type GetInterfaceType();

        /// <inheritdoc/>
        public abstract MethodInfo GetMethod();

        /// <inheritdoc/>
        public override string ToString() => IRequest.ToString(this);

        /// <inheritdoc/>
        public virtual TimeSpan? GetDefaultResponseTimeout() => null;

        /// <inheritdoc/>
        public virtual bool TryCancel() => false;

        /// <inheritdoc/>
        public virtual CancellationToken GetCancellationToken() => default;

        /// <inheritdoc/>
        public virtual bool IsCancellable => false;

        /// <summary>
        /// 获得一个数值,以表明关于这一请求的任何论点是否为 <see cref="GrainCancellationToken"/>。
        /// </summary>
        /// <remarks>
        /// 当 <see langword="false"/>时,运行时间不检查 <see cref="GrainCancellationToken"/> 实例的参数,
        /// ,通过 <see cref="GetArgument(int)"/>避免框框价值类型参数。
        /// 默认值为 <see langword="true"/> ,这样总是会检查不高于此属性的请求。
        /// </remarks>
        public virtual bool MayHaveGrainCancellationToken => true;
    }

    /// <summary>
    /// 返回方法请求的基础类别 <see cref="ValueTask"/>。
    /// </summary>
    [SerializerTransparent]
    public abstract class Request : RequestBase
    {
        /// <inheritdoc/>
        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Performance",
            "CA1849:Call async methods when in an async method",
            Justification = "GetResult is called only after IsCompleted and must consume a completed ValueTask source without allocating.")]
        public sealed override ValueTask<Response> Invoke()
        {
            try
            {
                var resultTask = InvokeInner();
                if (resultTask.IsCompleted)
                {
                    resultTask.GetAwaiter().GetResult();
                    return new ValueTask<Response>(Response.Completed);
                }

                return CompleteInvokeAsync(resultTask);
            }
            catch (Exception exception)
            {
                return new ValueTask<Response>(Response.FromException(exception));
            }
        }

        private static async ValueTask<Response> CompleteInvokeAsync(ValueTask resultTask)
        {
            try
            {
                await resultTask;
                return Response.Completed;
            }
            catch (Exception exception)
            {
                return Response.FromException(exception);
            }
        }

        /// <summary>
        /// 向目标发出请求
        /// </summary>
        /// <returns>代表援引的值任务。</returns>
        protected abstract ValueTask InvokeInner();
    }

    /// <summary>
    /// 返回方法请求的基础类别 <see cref="ValueTask{TResult}"/>。
    /// </summary>
    /// <typeparam name="TResult">
    /// 基本结果类型。
    /// </typeparam>
    [SerializerTransparent]
    public abstract class Request<TResult> : RequestBase
    {
        /// <inheritdoc/>
        public sealed override ValueTask<Response> Invoke()
        {
            try
            {
                var resultTask = InvokeInner();
                if (resultTask.IsCompleted)
                {
                    return new ValueTask<Response>(Response.FromResult(resultTask.Result));
                }

                return CompleteInvokeAsync(resultTask);
            }
            catch (Exception exception)
            {
                return new ValueTask<Response>(Response.FromException(exception));
            }
        }

        private static async ValueTask<Response> CompleteInvokeAsync(ValueTask<TResult> resultTask)
        {
            try
            {
                var result = await resultTask;
                return Response.FromResult(result);
            }
            catch (Exception exception)
            {
                return Response.FromException(exception);
            }
        }

        /// <summary>
        /// 向目标发出请求
        /// </summary>
        /// <returns>调用结果。</returns>
        protected abstract ValueTask<TResult> InvokeInner();
    }

    /// <summary>
    /// 返回方法请求的基础类别 <see cref="Task{TResult}"/>。
    /// </summary>
    /// <typeparam name="TResult">
    /// 基本结果类型。
    /// </typeparam>
    [SerializerTransparent]
    public abstract class TaskRequest<TResult> : RequestBase
    {
        /// <inheritdoc/>
        public sealed override ValueTask<Response> Invoke()
        {
            try
            {
                var resultTask = InvokeInner();
                if (resultTask.IsCompleted)
                {
                    return new ValueTask<Response>(Response.FromResult(resultTask.GetAwaiter().GetResult()));
                }

                return CompleteInvokeAsync(resultTask);
            }
            catch (Exception exception)
            {
                return new ValueTask<Response>(Response.FromException(exception));
            }
        }

        private static async ValueTask<Response> CompleteInvokeAsync(Task<TResult> resultTask)
        {
            try
            {
                var result = await resultTask;
                return Response.FromResult(result);
            }
            catch (Exception exception)
            {
                return Response.FromException(exception);
            }
        }

        /// <summary>
        /// 向目标发出请求
        /// </summary>
        /// <returns>调用结果。</returns>
        protected abstract Task<TResult> InvokeInner();
    }

    /// <summary>
    /// 返回方法请求的基础类别 <see cref="ValueTask"/>。
    /// </summary>
    [SerializerTransparent]
    public abstract class TaskRequest : RequestBase
    {
        /// <inheritdoc/>
        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Performance",
            "CA1849:Call async methods when in an async method",
            Justification = "GetResult is called only after IsCompleted, so it observes completion without blocking or allocating.")]
        public sealed override ValueTask<Response> Invoke()
        {
            try
            {
                var resultTask = InvokeInner();
                if (resultTask.IsCompleted)
                {
                    resultTask.GetAwaiter().GetResult();
                    return new ValueTask<Response>(Response.Completed);
                }

                return CompleteInvokeAsync(resultTask);
            }
            catch (Exception exception)
            {
                return new ValueTask<Response>(Response.FromException(exception));
            }
        }

        private static async ValueTask<Response> CompleteInvokeAsync(Task resultTask)
        {
            try
            {
                await resultTask;
                return Response.Completed;
            }
            catch (Exception exception)
            {
                return Response.FromException(exception);
            }
        }

        /// <summary>
        /// 向目标发出请求
        /// </summary>
        /// <returns>调用结果。</returns>
        protected abstract Task InvokeInner();
    }

    /// <summary>
    /// 要求无效返回方法的基级。
    /// </summary>
    [SerializerTransparent]
    public abstract class VoidRequest : RequestBase
    {
        /// <summary>
        /// 初始化 <see cref="VoidRequest"/> 类的新实例。
        /// </summary>
        protected VoidRequest()
        {
            // 所有无效的请求都是单向的。
            Options = InvokeMethodOptions.OneWay;
        }

        /// <inheritdoc/>
        public sealed override ValueTask<Response> Invoke()
        {
            try
            {
                InvokeInner();
                return new ValueTask<Response>(Response.Completed);
            }
            catch (Exception exception)
            {
                return new ValueTask<Response>(Response.FromException(exception));
            }
        }

        /// <summary>
        /// 向目标发出请求
        /// </summary>
        protected abstract void InvokeInner();
    }
}
