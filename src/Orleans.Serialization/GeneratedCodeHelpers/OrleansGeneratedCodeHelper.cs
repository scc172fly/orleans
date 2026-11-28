using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization.Activators;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Cloning;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.Serializers;
using Orleans.Serialization.WireProtocol;

namespace Orleans.Serialization.GeneratedCodeHelpers
{
    /// <summary>
    /// 供生成代码使用的实用工具。
    /// </summary>
    public static class OrleansGeneratedCodeHelper
    {
        private static readonly ThreadLocal<RecursiveServiceResolutionState> ResolutionState = new ThreadLocal<RecursiveServiceResolutionState>(() => new RecursiveServiceResolutionState());

        internal static void EnterServiceResolution(ICodecProvider provider, CodecProvider.ConstructionScope scope)
            => ResolutionState.Value!.Enter(null!, provider, scope);
        internal static void ExitServiceResolution() => ResolutionState.Value!.Exit();
        internal static CodecProvider.ConstructionScope? GetConstructionScope(ICodecProvider provider)
            => ResolutionState.Value!.GetConstructionScope(provider);

        private sealed class RecursiveServiceResolutionState
        {
            private int _depth;

            public List<(object Caller, ICodecProvider? Provider)> Callers { get; } = new();
            private readonly List<(ICodecProvider? Provider, int CallerStart, CodecProvider.ConstructionScope? Scope)> _active = new();
            public ICodecProvider? Provider => _active.Count > 0 ? _active[^1].Provider : null;

            public CodecProvider.ConstructionScope? GetConstructionScope(ICodecProvider provider)
            {
                foreach (var frame in _active)
                {
                    if (frame.Scope is not null && ReferenceEquals(frame.Provider, provider)) return frame.Scope;
                }
                return null;
            }

            public void Enter(object caller, ICodecProvider? provider = null, CodecProvider.ConstructionScope? scope = null)
            {
                ++_depth;
                var owner = provider ?? Provider;
                var callerOwner = Provider ?? owner;
                var callerStart = ReferenceEquals(owner, Provider) ? -1 : Callers.Count;
                _active.Add((owner, callerStart, scope));
                if (caller is not null)
                {
                    Callers.Add((caller, callerOwner));
                }
            }

            public void Exit()
            {
                var callerStart = _active[^1].CallerStart;
                _active.RemoveAt(_active.Count - 1);
                if (callerStart >= 0)
                {
                    Callers.RemoveRange(callerStart, Callers.Count - callerStart);
                }
                if (--_depth <= 0)
                {
                    Callers.Clear();
                }
            }

        }

        /// <summary>
        /// 解析请求的服务并解除其包装。
        /// </summary>
        /// <remarks>
        /// 已注册的序列化实现由编解码器提供程序缓存。
        /// 其他服务保留其配置的依赖注入生命周期。
        /// </remarks>
        /// <typeparam name="TService">服务类型。</typeparam>
        /// <param name="caller">调用方。</param>
        /// <param name="codecProvider">编解码器提供程序。</param>
        /// <returns>解除包装后的服务。</returns>
        public static TService GetService<
#if NET5_0_OR_GREATER
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TService>(
#else
            TService>(
#endif
            object caller,
            ICodecProvider codecProvider)
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(codecProvider);

            var state = ResolutionState.Value!;

            try
            {
                state.Enter(caller, codecProvider);


                foreach (var c in state.Callers)
                {
                    if (ReferenceEquals(c.Provider, codecProvider) && c.Caller is TService s && !(c.Caller is IServiceHolder<TService>))
                    {
                        return s;
                    }
                }

                var val = codecProvider is CodecProvider provider
                    ? (TService)provider.GetService(typeof(TService))
                    : ActivatorUtilities.GetServiceOrCreateInstance<TService>(codecProvider.Services);
                while (val is IServiceHolder<TService> wrapping)
                {
                    val = wrapping.Value;
                }

                return val;
            }
            catch (Exception exception)
            {
                if (codecProvider is CodecProvider provider) provider.RecordConstructionFailure(exception);
                throw;
            }
            finally
            {
                state.Exit();
            }
        }

        /// <summary>
        /// 如果给定服务已被包装，则解除其包装。
        /// </summary>
        /// <typeparam name="TService">服务类型。</typeparam>
        /// <param name="caller">调用方。</param>
        /// <param name="service">服务。</param>
        /// <returns>解除包装后的服务。</returns>
        public static TService UnwrapService<TService>(object caller, TService service)
        {
            var state = ResolutionState.Value!;
            var callerProvider = state.Provider;
            var serviceProvider = service is IServiceHolder<TService> holder ? holder.Provider as ICodecProvider : null;

            try
            {
                state.Enter(caller, serviceProvider);

                foreach (var c in state.Callers)
                {
                    if (ReferenceEquals(c.Provider, state.Provider) && c.Caller is TService s and not IServiceHolder<TService>)
                    {
                        return s;
                    }
                }

                var result = Unwrap(service);
                return result;
            }
            catch (Exception exception)
            {
                if ((callerProvider ?? state.Provider) is CodecProvider provider) provider.RecordConstructionFailure(exception);
                throw;
            }
            finally
            {
                state.Exit();
            }

            static TService Unwrap(TService val)
            {
                while (val is IServiceHolder<TService> wrapping)
                {
                    val = wrapping.Value;
                }

                return val;
            }
        }

        internal static object? TryGetService(Type serviceType, ICodecProvider codecProvider)
        {
            var state = ResolutionState.Value!;
            foreach (var c in state.Callers)
            {
                if (ReferenceEquals(c.Provider, codecProvider) && c.Caller is not IServiceHolder<object>
                    && serviceType.IsInstanceOfType(c.Caller))
                {
                    return c.Caller;
                }
            }

            return null;
        }

        /// <summary>
        /// 如果对象不支持浅拷贝，则返回给定的复制器。
        /// </summary>
        public static IDeepCopier<T>? GetOptionalCopier<T>(IDeepCopier<T> copier) => copier is IOptionalDeepCopier o && o.IsShallowCopyable() ? null : copier;

        internal static bool IsShallowCopyable([NotNullWhen(false)] IDeepCopier? copier)
            => copier is null || copier is IOptionalDeepCopier optional && optional.IsShallowCopyable();

        internal static bool IsShallowCopyable<TCopier>(ref int cache, TCopier copier, Func<TCopier, bool> calculate)
        {
            var result = Volatile.Read(ref cache);
            if (result == 0)
            {
                result = calculate(copier) ? 1 : 2;
                Volatile.Write(ref cache, result);
            }

            return result == 1;
        }

        /// <summary>        
        /// 生成代码的辅助方法，用于抛出 <see cref="ArgumentOutOfRangeException"/>。
        /// </summary>                
        public static object InvokableThrowArgumentOutOfRange(int index, int maxArgs)
            => throw new ArgumentOutOfRangeException(message: $"The argument index value {index} must be between 0 and {maxArgs}", null);

        /// <summary>
        /// 预期内容为空（仅包含一个类型为 <see cref="ExtendedWireType.EndBaseFields"/> 或 <see cref="ExtendedWireType.EndTagDelimited"/> 的字段头），
        /// 但也会消耗任何非预期字段。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ConsumeEndBaseOrEndObject<TInput>(this ref Reader<TInput> reader)
        {
            Unsafe.SkipInit(out Field field);
            reader.ReadFieldHeader(ref field);
            reader.ConsumeEndBaseOrEndObject(ref field);
        }

        /// <summary>
        /// 预期内容为空（仅包含一个类型为 <see cref="ExtendedWireType.EndBaseFields"/> 或 <see cref="ExtendedWireType.EndTagDelimited"/> 的字段头），
        /// 但也会消耗任何非预期字段。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ConsumeEndBaseOrEndObject<TInput>(this ref Reader<TInput> reader, scoped ref Field field)
        {
            if (!field.IsEndBaseOrEndObject)
                ConsumeUnexpectedContent(ref reader, ref field);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ConsumeUnexpectedContent<TInput>(this ref Reader<TInput> reader, scoped ref Field field)
        {
            do
            {
                reader.ConsumeUnknownField(ref field);
                reader.ReadFieldHeader(ref field);
            } while (!field.IsEndBaseOrEndObject);
        }

        /// <summary>
        /// 序列化非预期值。
        /// </summary>
        /// <typeparam name="TBufferWriter">缓冲区写入器类型。</typeparam>
        /// <param name="writer">写入器。</param>
        /// <param name="fieldIdDelta">字段 ID 差值。</param>
        /// <param name="expectedType">预期类型。</param>
        /// <param name="value">值。</param>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void SerializeUnexpectedType<TBufferWriter>(this ref Writer<TBufferWriter> writer, uint fieldIdDelta, [System.Diagnostics.CodeAnalysis.AllowNull] Type expectedType, object value) where TBufferWriter : IBufferWriter<byte>
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(value);

            var specificSerializer = writer.Session.CodecProvider.GetCodec(value.GetType());
            specificSerializer.WriteField(ref writer, fieldIdDelta, expectedType, value);
        }

        /// <summary>
        /// 反序列化非预期值。
        /// </summary>
        /// <typeparam name="TInput">读取器的输入类型。</typeparam>
        /// <typeparam name="TField">要反序列化的字段值的类型。</typeparam>
        /// <param name="reader">读取器。</param>
        /// <param name="field">字段。</param>
        /// <returns>值。</returns>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static TField DeserializeUnexpectedType<TInput, TField>(this ref Reader<TInput> reader, scoped ref Field field) where TField : class
        {
            var specificSerializer = reader.Session.CodecProvider.GetCodec(field.FieldType!);
            return (TField)specificSerializer.ReadValue(ref reader, field)!;
        }

        /// <summary>
        /// 写入带标记联合字段的字段头。
        /// </summary>
        /// <remarks>
        /// 带标记联合字段是一种最多包含一个内部字段的标记分隔字段。该内部字段的标识符即为联合分支标识符。
        /// 联合字段占用一个引用标识符，这使其引用标识符与跳过此字段的读取器保持一致。
        /// </remarks>
        /// <typeparam name="TBufferWriter">缓冲区写入器类型。</typeparam>
        /// <param name="writer">写入器。</param>
        /// <param name="fieldIdDelta">字段 ID 差值。</param>
        /// <param name="expectedType">预期类型。</param>
        /// <param name="unionType">联合类型或封闭基类型。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteStartUnion<TBufferWriter>(this ref Writer<TBufferWriter> writer, uint fieldIdDelta, Type? expectedType, Type unionType) where TBufferWriter : IBufferWriter<byte>
        {
            ReferenceCodec.MarkValueField(writer.Session);
            writer.WriteStartObject(fieldIdDelta, expectedType, unionType);
        }

        /// <summary>
        /// 抛出异常，表示序列化的带标记联合值包含未知的分支标识符。
        /// </summary>
        /// <typeparam name="TField">联合类型或封闭基类型。</typeparam>
        /// <param name="caseId">读取到的分支标识符。</param>
        /// <returns>此方法不会正常返回。</returns>
        [DoesNotReturn]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static TField ThrowUnknownUnionCase<TField>(uint caseId) => throw new UnknownUnionCaseException(typeof(TField), caseId);

        /// <summary>
        /// 抛出异常，表示某个值不属于联合类型或封闭类型层次结构中的任何分支。
        /// </summary>
        /// <typeparam name="TField">联合类型或封闭基类型。</typeparam>
        /// <param name="value">值。</param>
        [DoesNotReturn]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void ThrowUnsupportedUnionValue<TField>(object value)
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(value);
            throw new SerializerException($"A value of type {value.GetType()} is not a case of {typeof(TField)}.");
        }

        /// <summary>
        /// 获取与给定值匹配的 <see cref="MethodInfo"/>。
        /// </summary>
        /// <param name="interfaceType">接口类型。</param>
        /// <param name="methodName">方法名称。</param>
        /// <param name="methodTypeParameters">方法的泛型参数。</param>
        /// <param name="parameterTypes">参数类型。</param>
        /// <returns>对应的 <see cref="MethodInfo"/>。</returns>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static MethodInfo? GetMethodInfoOrDefault(
            Type? interfaceType,
            string methodName,
            Type[]? methodTypeParameters,
            Type[]? parameterTypes)
            => GetMethodInfoOrDefaultCore(interfaceType, methodName, methodTypeParameters, parameterTypes);

#if NET5_0_OR_GREATER
        [UnconditionalSuppressMessage(
            "Trimming",
            "IL2070",
            Justification = "Generated manifests register grain interfaces through TypeManifestOptions.AddInterface, which preserves public and non-public methods and inherited interfaces. Recursive Type.GetInterfaces() traversal cannot propagate that annotation.")]
#endif
        private static MethodInfo? GetMethodInfoOrDefaultCore(
            Type? interfaceType,
            string methodName,
            Type[]? methodTypeParameters,
            Type[]? parameterTypes)
        {
            if (interfaceType is null)
            {
                return null;
            }

            foreach (var method in interfaceType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                var current = method;
                if (current.Name != methodName)
                {
                    continue;
                }

                if (current.ContainsGenericParameters != methodTypeParameters is { Length: > 0 })
                {
                    continue;
                }

                if (methodTypeParameters is { Length: > 0 })
                {
                    if (methodTypeParameters.Length != current.GetGenericArguments().Length)
                    {
                        continue;
                    }

                    current = current.MakeGenericMethod(methodTypeParameters);
                }

                var parameters = current.GetParameters();
                if (parameters.Length != (parameterTypes?.Length ?? 0))
                {
                    continue;
                }

                var isMatch = true;
                for (int i = 0; i < parameters.Length; i++)
                {
                    if (!parameters[i].ParameterType.Equals(parameterTypes![i]))
                    {
                        isMatch = false;
                        break;
                    }
                }

                if (!isMatch)
                {
                    continue;
                }

                return current;
            }

            foreach (var implemented in interfaceType.GetInterfaces())
            {
                if (GetMethodInfoOrDefaultCore(implemented, methodName, methodTypeParameters, parameterTypes) is { } method)
                {
                    return method;
                }
            }

            return null;
        }

        /// <summary>
        /// 异常类型的默认复制器实现。
        /// </summary>
        /// <typeparam name="T">正在复制的异常类型。</typeparam>
        /// <typeparam name="B">由基类型复制器复制的基异常类型。</typeparam>
        public abstract class ExceptionCopier<T, B> : IDeepCopier<T>, IBaseCopier<T> where T : B where B : Exception
        {
            private readonly Type _fieldType = typeof(T);
            private readonly IActivator<T> _activator;
            private readonly IBaseCopier<B> _baseTypeCopier;

            /// <summary>
            /// 初始化 <see cref="ExceptionCopier{T, B}"/> 类的新实例。
            /// </summary>
            /// <param name="codecProvider">用于解析激活器和基类型复制器的编解码器提供程序。</param>
            protected ExceptionCopier(ICodecProvider codecProvider)
            {
                _activator = GetService<IActivator<T>>(this, codecProvider);
                _baseTypeCopier = GetService<IBaseCopier<B>>(this, codecProvider);
            }

            /// <inheritdoc/>
            [return: NotNullIfNotNull(nameof(original))]
            [SuppressMessage("Design", "CA1062:Validate arguments of public methods", Justification = "The Orleans deep-copy pipeline supplies the active non-null copy context.")]
            public T? DeepCopy(T? original, CopyContext context)
            {
                if (original is null)
                {
                    return default;
                }

                if (original.GetType() != _fieldType)
                {
                    return context.DeepCopy(original)!;
                }

                var result = _activator.Create();
                DeepCopy(original, result, context);
                return result;
            }

            /// <inheritdoc/>
            public virtual void DeepCopy(T input, T output, CopyContext context) => _baseTypeCopier.DeepCopy(input, output, context);
        }
    }
}
