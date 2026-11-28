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
    /// <see cref="GrainReference"/> 实例共有的属性，适用于具有相同 <see cref="GrainType"/> 和 <see cref="GrainInterfaceType"/> 的实例。
    /// </summary>
    public class GrainReferenceShared
    {
        /// <summary>
        /// 初始化 <see cref="GrainReferenceShared"/> 类的新实例。
        /// </summary>
        /// <param name="grainType">Grain 类型。</param>
        /// <param name="grainInterfaceType">Grain 接口类型。</param>
        /// <param name="interfaceVersion">Grain 接口版本。</param>
        /// <param name="runtime">Grain 引用运行时。</param>
        /// <param name="invokeMethodOptions">通用调用选项。</param>
        /// <param name="codecProvider">序列化编解码器提供程序。</param>
        /// <param name="copyContextPool">序列化复制上下文池。</param>
        /// <param name="serviceProvider">服务提供程序。</param>
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
        /// 获取 Grain 引用运行时。
        /// </summary>
        public IGrainReferenceRuntime Runtime { get; }

        /// <summary>
        /// 获取 Grain 类型。
        /// </summary>
        public GrainType GrainType { get; }

        /// <summary>
        /// 获取接口类型。
        /// </summary>
        public GrainInterfaceType InterfaceType { get; }

        /// <summary>
        /// 获取通用调用选项。
        /// </summary>
        public InvokeMethodOptions InvokeMethodOptions { get; }

        /// <summary>
        /// 获取序列化编解码器提供程序。
        /// </summary>
        public CodecProvider CodecProvider { get; }

        /// <summary>
        /// 获取序列化复制上下文池。
        /// </summary>
        public CopyContextPool CopyContextPool { get; }

        /// <summary>
        /// 获取服务提供程序。
        /// </summary>
        public IServiceProvider ServiceProvider { get; }

        /// <summary>
        /// 获取接口版本。
        /// </summary>
        public ushort InterfaceVersion { get; }
    }

    /// <summary>
    /// 提供对 <see cref="GrainReference"/> 及其派生类型进行序列化和反序列化的功能。
    /// </summary>
    [RegisterSerializer]
    internal class GrainReferenceCodec : GeneralizedReferenceTypeSurrogateCodec<IAddressable, GrainReferenceSurrogate>
    {
        private readonly IGrainFactory _grainFactory;

        /// <summary>
        /// 初始化 <see cref="GrainReferenceCodec"/> 类的新实例。
        /// </summary>
        /// <param name="grainFactory">Grain 工厂。</param>
        /// <param name="surrogateSerializer">此类所使用的代理类型的序列化器。</param>
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
    /// 为 Grain 引用类型提供专用复制器实例。
    /// </summary>
    internal class GrainReferenceCopierProvider : ISpecializableCopier
    {
        /// <inheritdoc/>
        // ! 使用这些参数调用 Activator.CreateInstance 不会返回 null。
        public IDeepCopier GetSpecializedCopier(Type type) => (IDeepCopier)Activator.CreateInstance(typeof(TypedGrainReferenceCopier<>).MakeGenericType(type))!;

        /// <inheritdoc/>
        public bool IsSupportedType(Type type) => typeof(IAddressable).IsAssignableFrom(type) && type.IsInterface;
    }

    /// <summary>
    /// 用于 Grain 引用实例的强类型复制器。
    /// </summary>
    /// <typeparam name="TInterface">Grain 接口类型。</typeparam>
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
    /// 为 Grain 引用类型提供专用编解码器实例。
    /// </summary>
    internal class GrainReferenceCodecProvider : ISpecializableCodec
    {
        private readonly IServiceProvider _serviceProvider;

        /// <summary>
        /// 初始化 <see cref="GrainReferenceCodecProvider"/> 类的新实例。
        /// </summary>
        /// <param name="serviceProvider">服务提供程序。</param>
        public GrainReferenceCodecProvider(IServiceProvider serviceProvider) => _serviceProvider = serviceProvider;

        /// <inheritdoc/>
        public IFieldCodec GetSpecializedCodec(Type type) => (IFieldCodec)ActivatorUtilities.GetServiceOrCreateInstance(_serviceProvider, typeof(TypedGrainReferenceCodec<>).MakeGenericType(type));

        /// <inheritdoc/>
        public bool IsSupportedType(Type type) => typeof(IAddressable).IsAssignableFrom(type);

        /// <summary>
        /// 抛出异常，指示不支持某个参数类型。
        /// </summary>
        /// <param name="observer">观察者。</param>
        internal static void ThrowGrainObserverInvalidException(IGrainObserver observer)
            => throw new NotSupportedException($"IGrainObserver parameters must be GrainReference or Grain and cannot be type {observer.GetType()}. Did you forget to CreateObjectReference?");
    }

    /// <summary>
    /// 用于 Grain 引用实例的强类型编解码器。
    /// </summary>
    /// <typeparam name="T">Grain 引用的接口类型。</typeparam>
    internal class TypedGrainReferenceCodec<T> : GeneralizedReferenceTypeSurrogateCodec<T, GrainReferenceSurrogate>
        where T : class, IAddressable
    {
        private readonly IGrainFactory _grainFactory;

        /// <summary>
        /// 初始化 <see cref="TypedGrainReferenceCodec{T}"/> 类的新实例。
        /// </summary>
        /// <param name="grainFactory">Grain 工厂。</param>
        /// <param name="surrogateSerializer">代理类型序列化器。</param>
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
            // 先检查通常为 false 的条件，再执行开销更大的接口检查。
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
    /// 序列化时用于表示 <see cref="GrainReference"/> 实现的代理类型。
    /// </summary>
    [GenerateSerializer]
    internal struct GrainReferenceSurrogate
    {
        /// <summary>
        /// 获取或设置 Grain ID。
        /// </summary>
        [Id(0)]
        public GrainId GrainId;

        /// <summary>
        /// 获取或设置 Grain 接口类型。
        /// </summary>
        [Id(1)]
        public GrainInterfaceType GrainInterfaceType;
    }

    /// <summary>
    /// 这是所有 Grain 引用的基类。
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
        /// 给定类型的所有 Grain 引用共享的功能。
        /// </summary>
        [NonSerialized]
        private readonly GrainReferenceShared _shared;

        /// <summary>
        /// 底层 Grain ID 的键。
        /// </summary>
        [NonSerialized]
        private readonly IdSpan _key;

        /// <summary>
        /// 获取给定类型的所有 Grain 引用共享的功能。
        /// </summary>
        internal GrainReferenceShared Shared => _shared ?? throw new GrainReferenceNotBoundException(this);

        /// <summary>
        /// 获取 Grain 引用运行时。
        /// </summary>
        internal IGrainReferenceRuntime Runtime => Shared.Runtime;

        /// <summary>
        /// 获取 Grain ID。
        /// </summary>
        public GrainId GrainId => GrainId.Create(_shared.GrainType, _key);

        /// <summary>
        /// 获取接口类型。
        /// </summary>
        public GrainInterfaceType InterfaceType => _shared.InterfaceType;

        /// <summary>
        /// 获取序列化复制上下文池。
        /// </summary>
        protected CopyContextPool CopyContextPool => _shared.CopyContextPool;

        /// <summary>
        /// 获取序列化编解码器提供程序。
        /// </summary>
        protected CodecProvider CodecProvider => _shared.CodecProvider;

        /// <summary>初始化 <see cref="GrainReference"/> 类的新实例。</summary>
        /// <param name="shared">
        /// 给定类型的所有 Grain 引用共享的功能。
        /// </param>
        /// <param name="key">
        /// Grain ID 的键部分。
        /// </param>
        protected GrainReference(GrainReferenceShared shared, IdSpan key)
        {
            _shared = shared;
            _key = key;
        }

        /// <summary>
        /// 创建新的 <see cref="GrainReference"/> 实例，用于指定的 <paramref name="grainId"/>。
        /// </summary>
        internal static GrainReference FromGrainId(GrainReferenceShared shared, GrainId grainId) => new(shared, grainId.Key);

        /// <summary>
        /// 创建一个实现指定 Grain 接口的新 Grain 引用。
        /// </summary>
        /// <typeparam name="TGrainInterface">
        /// Grain 接口类型。
        /// </typeparam>
        /// <returns>实现指定接口类型的新 Grain 引用。</returns>
        public virtual TGrainInterface Cast<TGrainInterface>()
            where TGrainInterface : IAddressable
            => (TGrainInterface)_shared.Runtime.Cast(this, typeof(TGrainInterface));

        /// <summary>
        /// 检查此引用是否与另一个对象相等。
        /// 如果两个 Grain 引用指向同一个 Grain，则这两个引用相等。
        /// </summary>
        /// <param name="obj">要与此引用进行相等性比较的对象。</param>
        /// <returns><c>true</c> 表示该对象与此引用相等。</returns>
        public override bool Equals(object? obj)
        {
            return Equals(obj as GrainReference);
        }

        /// <inheritdoc />
        public bool Equals(GrainReference? other) => other is not null && this.GrainId.Equals(other.GrainId);

        /// <inheritdoc />
        public override int GetHashCode() => this.GrainId.GetHashCode();

        /// <summary>
        /// 获取此 Grain 引用的均匀哈希码。
        /// </summary>
        /// <returns>
        /// 均匀哈希码。
        /// </returns>
        public uint GetUniformHashCode()
        {
            // GrainId 已包含泛型实参对应的哈希类型码。
            return GrainId.GetUniformHashCode();
        }

        /// <summary>
        /// 比较两个引用是否相等。
        /// 如果两个 Grain 引用指向同一个 Grain，则这两个引用相等。
        /// </summary>
        /// <param name="reference1">要比较的第一个 Grain 引用。</param>
        /// <param name="reference2">要比较的第二个 Grain 引用。</param>
        /// <returns><c>true</c> 表示两个 Grain 引用指向同一 Grain（依据 Grain 标识符）。</returns>
        public static bool operator ==(GrainReference? reference1, GrainReference? reference2)
        {
            if (reference1 is null) return reference2 is null;

            return reference1.Equals(reference2);
        }

        /// <summary>
        /// 比较两个引用是否不相等。
        /// 如果两个 Grain 引用指向同一个 Grain，则这两个引用相等。
        /// </summary>
        /// <param name="reference1">要比较的第一个 Grain 引用。</param>
        /// <param name="reference2">要比较的第二个 Grain 引用。</param>
        /// <returns><c>false</c> 表示两个 Grain 引用解析到同一 Grain（依据 Grain 标识符）。</returns>
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
        /// 获取或创建一个可调用请求实例。
        /// </summary>
        /// <typeparam name="TInvokable">可调用请求的类型。</typeparam>
        /// <returns>可调用请求实例。</returns>
        protected TInvokable GetInvokable<TInvokable>() => ActivatorUtilities.GetServiceOrCreateInstance<TInvokable>(Shared.ServiceProvider);

        /// <summary>
        /// 调用指定的方法。
        /// </summary>
        /// <typeparam name="T">底层方法的返回类型。</typeparam>
        /// <param name="methodDescription">方法描述。</param>
        /// <returns>调用结果。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="methodDescription"/> 为 <see langword="null"/>。</exception>
        protected ValueTask<T?> InvokeAsync<T>(IRequest methodDescription)
        {
            ArgumentNullException.ThrowIfNull(methodDescription);
            return this.Runtime.InvokeMethodAsync<T>(this, methodDescription, methodDescription.Options);
        }

        /// <summary>
        /// 调用指定的方法。
        /// </summary>
        /// <param name="methodDescription">方法描述。</param>
        /// <returns>表示该操作的 <see cref="ValueTask"/>。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="methodDescription"/> 为 <see langword="null"/>。</exception>
        protected ValueTask InvokeAsync(IRequest methodDescription)
        {
            ArgumentNullException.ThrowIfNull(methodDescription);
            return this.Runtime.InvokeMethodAsync(this, methodDescription, methodDescription.Options);
        }

        /// <summary>
        /// 调用指定的方法。
        /// </summary>
        /// <param name="methodDescription">方法描述。</param>
        /// <exception cref="ArgumentNullException"><paramref name="methodDescription"/> 为 <see langword="null"/>。</exception>
        protected void Invoke(IRequest methodDescription)
        {
            ArgumentNullException.ThrowIfNull(methodDescription);
            this.Runtime.InvokeMethod(this, methodDescription, methodDescription.Options);
        }
    }

    /// <summary>
    /// 表示调用 Grain 上某个方法的请求。
    /// </summary>
    public interface IRequest : IInvokable
    {
        /// <summary>
        /// 获取调用选项。
        /// </summary>
        InvokeMethodOptions Options { get; }

        /// <summary>
        /// 合并提供的调用选项。
        /// </summary>
        /// <param name="options">
        /// 选项。
        /// </param>
        void AddInvokeMethodOptions(InvokeMethodOptions options);

        /// <summary>
        /// 返回请求的字符串表示形式。
        /// </summary>
        /// <returns>请求的字符串表示形式。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="request"/> 为 <see langword="null"/>。</exception>
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
        /// 返回请求的字符串表示形式。
        /// </summary>
        /// <returns>请求的字符串表示形式。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="request"/> 为 <see langword="null"/>。</exception>
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
    /// 方法请求所使用的基类型。
    /// </summary>
    [SuppressReferenceTracking]
    [SerializerTransparent]
    public abstract class RequestBase : IRequest
    {
        /// <summary>
        /// 获取调用选项。
        /// </summary>
        [field: NonSerialized]
        public InvokeMethodOptions Options { get; protected set; }

        /// <inheritdoc/>
        public virtual int GetArgumentCount() => 0;

        /// <summary>
        /// 合并提供的调用选项。
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
        /// 获取一个值，指示此请求的参数中是否可能包含 <see cref="GrainCancellationToken"/>。
        /// </summary>
        /// <remarks>
        /// 当值为 <see langword="false"/> 时，运行时不会检查参数中是否存在 <see cref="GrainCancellationToken"/> 实例，
        /// 从而避免通过 <see cref="GetArgument(int)"/> 对值类型参数执行装箱操作。
        /// 默认值为 <see langword="true"/>，以便未重写此属性的请求始终接受检查。
        /// </remarks>
        public virtual bool MayHaveGrainCancellationToken => true;
    }

    /// <summary>
    /// 返回 <see cref="ValueTask"/> 的方法所对应请求的基类。
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
        /// 针对目标执行该请求。
        /// </summary>
        /// <returns>表示调用操作的值任务。</returns>
        protected abstract ValueTask InvokeInner();
    }

    /// <summary>
    /// 返回 <see cref="ValueTask{TResult}"/> 的方法所对应请求的基类。
    /// </summary>
    /// <typeparam name="TResult">
    /// 底层结果类型。
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
        /// 针对目标执行该请求。
        /// </summary>
        /// <returns>调用结果。</returns>
        protected abstract ValueTask<TResult> InvokeInner();
    }

    /// <summary>
    /// 返回 <see cref="Task{TResult}"/> 的方法所对应请求的基类。
    /// </summary>
    /// <typeparam name="TResult">
    /// 底层结果类型。
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
        /// 针对目标执行该请求。
        /// </summary>
        /// <returns>调用结果。</returns>
        protected abstract Task<TResult> InvokeInner();
    }

    /// <summary>
    /// 返回 <see cref="ValueTask"/> 的方法所对应请求的基类。
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
        /// 针对目标执行该请求。
        /// </summary>
        /// <returns>调用结果。</returns>
        protected abstract Task InvokeInner();
    }

    /// <summary>
    /// 返回 void 的方法所对应请求的基类。
    /// </summary>
    [SerializerTransparent]
    public abstract class VoidRequest : RequestBase
    {
        /// <summary>
        /// 初始化 <see cref="VoidRequest"/> 类的新实例。
        /// </summary>
        protected VoidRequest()
        {
            // 所有返回 void 的请求本质上都是单向请求。
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
        /// 针对目标执行该请求。
        /// </summary>
        protected abstract void InvokeInner();
    }
}
