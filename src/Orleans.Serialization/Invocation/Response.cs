using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Orleans.Serialization.Activators;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Cloning;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.GeneratedCodeHelpers;
using Orleans.Serialization.Serializers;
using Orleans.Serialization.WireProtocol;

namespace Orleans.Serialization.Invocation
{
    /// <summary>
    /// 表示方法调用的结果。
    /// </summary>
    [SerializerTransparent]
    public abstract class Response : IDisposable
    {
        /// <summary>
        /// 创建表示异常的新响应。
        /// </summary>
        /// <param name="exception">异常。</param>
        /// <returns>新的响应。</returns>
        public static Response FromException(Exception exception) => new ExceptionResponse { Exception = exception };

        /// <summary>
        /// 创建一个已使用给定值完成的新响应对象。
        /// </summary>
        /// <typeparam name="TResult">底层结果类型。</typeparam>
        /// <param name="value">值。</param>
        /// <returns>新的响应。</returns>
        public static Response FromResult<TResult>(TResult value)
        {
            var result = ResponsePool.Get<TResult>();
            result.TypedResult = value;
            return result;
        }

        /// <summary>
        /// 获取已完成的响应。
        /// </summary>
        public static Response Completed => CompletedResponse.Instance;

        /// <summary>
        /// 获取或设置无类型的结果值。
        /// </summary>
        public abstract object? Result { get; set; }

        /// <summary>
        /// 当响应表示简单的强类型结果时，获取结果类型。
        /// </summary>
        /// <returns>结果类型；当响应不表示简单的强类型结果时为 <see langword="null"/>。</returns>
        public virtual Type? GetSimpleResultType() => null;

        /// <summary>
        /// 获取或设置此响应表示的异常。
        /// </summary>
        public abstract Exception? Exception { get; set; }

        /// <summary>
        /// 以指定类型获取结果。
        /// </summary>
        /// <typeparam name="T">请求的结果类型。</typeparam>
        /// <returns>结果。</returns>
        [return: System.Diagnostics.CodeAnalysis.MaybeNull]
        public abstract T GetResult<T>();

        /// <inheritdoc />
        public abstract void Dispose();

        /// <inheritdoc />
        public override string ToString() => Exception is { } ex ? ex.ToString() : Result?.ToString() ?? "[null]";
    }

    /// <summary>
    /// 表示已完成的 <see cref="Response"/>。
    /// </summary>
    [GenerateSerializer, Immutable, UseActivator, SuppressReferenceTracking]
    public sealed class CompletedResponse : Response
    {
        /// <summary>
        /// 获取此类的单例实例。
        /// </summary>
        public static CompletedResponse Instance { get; } = new CompletedResponse();

        /// <inheritdoc/>
        public override object? Result { get => null; set => throw new InvalidOperationException($"Type {nameof(CompletedResponse)} is read-only"); }

        /// <inheritdoc/>
        public override Exception? Exception { get => null; set => throw new InvalidOperationException($"Type {nameof(CompletedResponse)} is read-only"); }

        /// <inheritdoc/>
        [return: System.Diagnostics.CodeAnalysis.MaybeNull]
        public override T GetResult<T>() => default;

        /// <inheritdoc/>
        public override void Dispose() { }

        /// <inheritdoc/>
        public override string ToString() => "[Completed]";
    }

    /// <summary>
    /// <see cref="CompletedResponse"/> 的激活器。
    /// </summary>
    [RegisterActivator]
    internal sealed class CompletedResponseActivator : IActivator<CompletedResponse>
    {
        /// <inheritdoc/>
        public CompletedResponse Create() => CompletedResponse.Instance;
    }

    /// <summary>
    /// 表示异常的 <see cref="Response"/>，即以失败结束的承诺对象（Promise）。
    /// </summary>
    [GenerateSerializer, Immutable]
    public sealed class ExceptionResponse : Response
    {
        /// <inheritdoc/>
        public override object? Result
        {
            get
            {
                ExceptionDispatchInfo.Capture(Exception!).Throw();
                return null;
            }

            set => throw new InvalidOperationException($"Cannot set result property on response of type {nameof(ExceptionResponse)}");
        }

        /// <inheritdoc/>
        [Id(0)]
        public override Exception? Exception { get; set; }

        /// <inheritdoc/>
        [return: System.Diagnostics.CodeAnalysis.MaybeNull]
        public override T GetResult<T>()
        {
            ExceptionDispatchInfo.Capture(Exception!).Throw();
            return default;
        }

        /// <inheritdoc/>
        public override void Dispose() { }

        /// <inheritdoc/>
        public override string ToString() => Exception?.ToString() ?? "[null]";
    }

    /// <summary>
    /// 表示强类型值的 <see cref="Response"/>。
    /// </summary>
    /// <typeparam name="TResult">底层结果类型。</typeparam>
    [UseActivator, SuppressReferenceTracking]
    public sealed class Response<TResult> : Response
    {
        [Id(0)]
        private TResult? _result;

        /// <summary>
        /// 获取或设置强类型的结果值。
        /// </summary>
        public TResult? TypedResult { get => _result; set => _result = value; }

        /// <summary>
        /// 获取与响应关联的异常。
        /// </summary>
        /// <remarks>
        /// 强类型响应不包含异常。设置此属性会抛出 <see cref="InvalidOperationException"/>。
        /// </remarks>
        public override Exception? Exception
        {
            get => null;
            set => throw new InvalidOperationException($"Cannot set {nameof(Exception)} property for type {nameof(Response<TResult>)}");
        }

        /// <inheritdoc/>
        public override object? Result
        {
            get => _result;
            set => _result = (TResult?)value;
        }

        /// <inheritdoc/>
        public override Type GetSimpleResultType() => typeof(TResult);

        /// <inheritdoc/>
        public override T GetResult<T>()
        {
            if (typeof(TResult).IsValueType && typeof(T).IsValueType && typeof(T) == typeof(TResult))
                return Unsafe.As<TResult, T>(ref _result!);

            return (T)(object)_result!;
        }

        /// <inheritdoc/>
        public override void Dispose()
        {
            _result = default;
            ResponsePool.Return(this);
        }

        /// <inheritdoc/>
        public override string ToString() => _result?.ToString() ?? "[null]";
    }

    /// <summary>
    /// 支持 <see cref="Response{TResult}"/> 值的原始序列化。
    /// </summary>
    public abstract class ResponseCodec
    {
        /// <summary>
        /// 写入不带外层字段头的响应。
        /// </summary>
        /// <typeparam name="TBufferWriter">缓冲区写入器类型。</typeparam>
        /// <param name="writer">写入器。</param>
        /// <param name="value">要写入的响应。</param>
        public abstract void WriteRaw<TBufferWriter>(ref Writer<TBufferWriter> writer, object value) where TBufferWriter : IBufferWriter<byte>;

        /// <summary>
        /// 读取不带外层字段头的响应。
        /// </summary>
        /// <typeparam name="TInput">读取器的输入类型。</typeparam>
        /// <param name="reader">读取器。</param>
        /// <param name="field">响应内容的字段头。</param>
        /// <returns>响应。</returns>
        public abstract object ReadRaw<TInput>(ref Reader<TInput> reader, scoped ref Field field);
    }

    [RegisterSerializer]
    internal sealed class PooledResponseCodec<TResult> : ResponseCodec, IFieldCodec<Response<TResult>>
    {
        private readonly Type _codecFieldType = typeof(Response<TResult>);
        private readonly Type _resultType = typeof(TResult);
        private readonly IFieldCodec<TResult> _codec;

        public PooledResponseCodec(ICodecProvider codecProvider)
            => _codec = OrleansGeneratedCodeHelper.GetService<IFieldCodec<TResult>>(this, codecProvider);

        public void WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta, [System.Diagnostics.CodeAnalysis.AllowNull] Type expectedType, [System.Diagnostics.CodeAnalysis.AllowNull] Response<TResult> value) where TBufferWriter : IBufferWriter<byte>
        {
            if (value is null)
            {
                ReferenceCodec.WriteNullReference(ref writer, fieldIdDelta);
                return;
            }

            ReferenceCodec.MarkValueField(writer.Session);
            writer.WriteStartObject(fieldIdDelta, expectedType, _codecFieldType);
            if (value.TypedResult is not null)
                _codec.WriteField(ref writer, 0, _resultType, value.TypedResult);
            writer.WriteEndObject();
        }

        [return: System.Diagnostics.CodeAnalysis.MaybeNull]
        public Response<TResult> ReadValue<TInput>(ref Reader<TInput> reader, Field field)
        {
            if (field.IsReference)
                return ReferenceCodec.ReadReference<Response<TResult>, TInput>(ref reader, field);

            field.EnsureWireTypeTagDelimited();
            ReferenceCodec.MarkValueField(reader.Session);
            var result = ResponsePool.Get<TResult>();
            reader.ReadFieldHeader(ref field);
            if (!field.IsEndBaseOrEndObject)
            {
                result.TypedResult = _codec.ReadValue(ref reader, field);
                reader.ReadFieldHeader(ref field);
                reader.ConsumeEndBaseOrEndObject(ref field);
            }
            return result;
        }

        public override void WriteRaw<TBufferWriter>(ref Writer<TBufferWriter> writer, object value)
        {
            writer.WriteStartObject(0, null!, _resultType);
            var holder = (Response<TResult>)value;
            if (holder.TypedResult is not null)
                _codec.WriteField(ref writer, 0, _resultType, holder.TypedResult);
            writer.WriteEndObject();
        }

        public override object ReadRaw<TInput>(ref Reader<TInput> reader, scoped ref Field field)
        {
            field.EnsureWireTypeTagDelimited();
            var result = ResponsePool.Get<TResult>();
            reader.ReadFieldHeader(ref field);
            if (!field.IsEndBaseOrEndObject)
            {
                result.TypedResult = _codec.ReadValue(ref reader, field);
                reader.ReadFieldHeader(ref field);
                reader.ConsumeEndBaseOrEndObject(ref field);
            }
            return result;
        }
    }

    [RegisterCopier]
    internal sealed class PooledResponseCopier<TResult> : IDeepCopier<Response<TResult>>
    {
        private readonly IDeepCopier<TResult> _copier;

        public PooledResponseCopier(ICodecProvider codecProvider)
            => _copier = OrleansGeneratedCodeHelper.GetService<IDeepCopier<TResult>>(this, codecProvider);

        [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(input))]
        public Response<TResult>? DeepCopy(Response<TResult>? input, CopyContext context)
        {
            if (input is null)
                return null;

            var result = ResponsePool.Get<TResult>();
            result.TypedResult = _copier.DeepCopy(input.TypedResult!, context);
            return result;
        }
    }

    [RegisterActivator]
    internal sealed class PooledResponseActivator<TResult> : IActivator<Response<TResult>>
    {
        public Response<TResult> Create() => ResponsePool.Get<TResult>();
    }

    /// <summary>
    /// 用于处理调用响应的扩展方法。
    /// </summary>
    public static class ResponseExtensions
    {
        /// <summary>
        /// 如果响应包含异常，则抛出该异常。
        /// </summary>
        /// <param name="response">要检查的响应。</param>
        public static void ThrowIfExceptionResponse(this Response response)
        {
            if (response.Exception is { } exception)
            {
                ExceptionDispatchInfo.Capture(exception).Throw();
            }
        }
    }
}
