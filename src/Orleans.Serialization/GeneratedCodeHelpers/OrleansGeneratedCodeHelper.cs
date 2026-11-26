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
    /// 用于生成代码的公用设备。
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
        /// 解析并卸载请求的服务。
        /// </summary>
        /// <remarks>
        /// 注册的序列化实施由编码器提供者藏匿。
        /// 其他服务保留其配置的自带注射寿命。
        /// </remarks>
        /// <typeparam name="TService">服务类型。</typeparam>
        /// <param name="caller">打电话者。</param>
        /// <param name="codecProvider">编码器提供者。</param>
        /// <returns>未包装的服务。</returns>
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
        /// 如果被包扎,则解开所提供的服务。
        /// </summary>
        /// <typeparam name="TService">服务类型。</typeparam>
        /// <param name="caller">打电话者。</param>
        /// <param name="service">服务。</param>
        /// <returns>未包装的服务。</returns>
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
        /// 如果无法浅显复制,则返回所提供的复印机。
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
        /// 生成代码助手方法, 投出 <see cref="ArgumentOutOfRangeException"/>。
        /// </summary>                
        public static object InvokableThrowArgumentOutOfRange(int index, int maxArgs)
            => throw new ArgumentOutOfRangeException(message: $"The argument index value {index} must be between 0 and {maxArgs}", null);

        /// <summary>
        /// 需要空内容( <see cref="ExtendedWireType.EndBaseFields"/> 或 <see cref="ExtendedWireType.EndTagDelimited"/>的单字段页眉),
        /// 但也将消耗任何未预料到的字段。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ConsumeEndBaseOrEndObject<TInput>(this ref Reader<TInput> reader)
        {
            Unsafe.SkipInit(out Field field);
            reader.ReadFieldHeader(ref field);
            reader.ConsumeEndBaseOrEndObject(ref field);
        }

        /// <summary>
        /// 需要空内容( <see cref="ExtendedWireType.EndBaseFields"/> 或 <see cref="ExtendedWireType.EndTagDelimited"/>的单字段页眉),
        /// 但也将消耗任何未预料到的字段。
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
        /// 将意外值分层。
        /// </summary>
        /// <typeparam name="TBufferWriter">缓冲写作类型。</typeparam>
        /// <param name="writer">作家。</param>
        /// <param name="fieldIdDelta">字段识别符号三角形。</param>
        /// <param name="expectedType">预想的类型。</param>
        /// <param name="value">数值。</param>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void SerializeUnexpectedType<TBufferWriter>(this ref Writer<TBufferWriter> writer, uint fieldIdDelta, [System.Diagnostics.CodeAnalysis.AllowNull] Type expectedType, object value) where TBufferWriter : IBufferWriter<byte>
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(value);

            var specificSerializer = writer.Session.CodecProvider.GetCodec(value.GetType());
            specificSerializer.WriteField(ref writer, fieldIdDelta, expectedType, value);
        }

        /// <summary>
        /// 使意外值降序。
        /// </summary>
        /// <typeparam name="TInput">读者输入类型。</typeparam>
        /// <typeparam name="TField">值类型。</typeparam>
        /// <param name="reader">读者</param>
        /// <param name="field">字段。</param>
        /// <returns>数值。</returns>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static TField DeserializeUnexpectedType<TInput, TField>(this ref Reader<TInput> reader, scoped ref Field field) where TField : class
        {
            var specificSerializer = reader.Session.CodecProvider.GetCodec(field.FieldType!);
            return (TField)specificSerializer.ReadValue(ref reader, field)!;
        }

        /// <summary>
        /// 写下一个贴有标签的工会球场的领头人
        /// </summary>
        /// <remarks>
        /// 标记的工会字段是一个限定标签的字段,最多包含一个字段。该内字段的身份识别符号是案例识别符。
        /// 联盟字段使用一个参考识别符,该标识符保持参考识别符与偏离字段的读者保持一致。
        /// </remarks>
        /// <typeparam name="TBufferWriter">缓冲写作类型。</typeparam>
        /// <param name="writer">作家。</param>
        /// <param name="fieldIdDelta">字段识别符号三角形。</param>
        /// <param name="expectedType">预想的类型。</param>
        /// <param name="unionType">工会类型或封闭基型。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteStartUnion<TBufferWriter>(this ref Writer<TBufferWriter> writer, uint fieldIdDelta, Type? expectedType, Type unionType) where TBufferWriter : IBufferWriter<byte>
        {
            ReferenceCodec.MarkValueField(writer.Session);
            writer.WriteStartObject(fieldIdDelta, expectedType, unionType);
        }

        /// <summary>
        /// 抛出一个例外,表明按序列排列的贴标签工会价值有未知的案件识别特征。
        /// </summary>
        /// <typeparam name="TField">工会类型或封闭基型。</typeparam>
        /// <param name="caseId">所读到的案例识别符号 。</param>
        /// <returns>此方法不返回。</returns>
        [DoesNotReturn]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static TField ThrowUnknownUnionCase<TField>(uint caseId) => throw new UnknownUnionCaseException(typeof(TField), caseId);

        /// <summary>
        /// 抛出一个例外,表明某一数值不是工会类型或封闭类型等级的任何案例。
        /// </summary>
        /// <typeparam name="TField">工会类型或封闭基型。</typeparam>
        /// <param name="value">数值。</param>
        [DoesNotReturn]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void ThrowUnsupportedUnionValue<TField>(object value)
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(value);
            throw new SerializerException($"A value of type {value.GetType()} is not a case of {typeof(TField)}.");
        }

        /// <summary>
        /// 获取符合提供值的 <see cref="MethodInfo"/> 。
        /// </summary>
        /// <param name="interfaceType">接口的类型。</param>
        /// <param name="methodName">方法名称。</param>
        /// <param name="methodTypeParameters">方法类型参数。</param>
        /// <param name="parameterTypes">参数类型。</param>
        /// <returns>对应对应 <see cref="MethodInfo"/>.</returns>
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
        /// 例外类型默认复印机执行 。
        /// </summary>
        /// <typeparam name="T">正在复制的例外类型。</typeparam>
        /// <typeparam name="B">基础复印机复制的例外类型。</typeparam>
        public abstract class ExceptionCopier<T, B> : IDeepCopier<T>, IBaseCopier<T> where T : B where B : Exception
        {
            private readonly Type _fieldType = typeof(T);
            private readonly IActivator<T> _activator;
            private readonly IBaseCopier<B> _baseTypeCopier;

            /// <summary>
            /// 初始化 <see cref="ExceptionCopier{T, B}"/> 类的新实例。
            /// </summary>
            /// <param name="codecProvider">用来解决引爆器和底部复印机的编码提供者。</param>
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
