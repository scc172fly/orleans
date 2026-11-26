using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Reflection;
using System.Threading;
using Microsoft.Extensions.ObjectPool;
using Orleans.Serialization.Invocation;
using Orleans.Serialization.Serializers;
using Orleans.Serialization.Utilities;

namespace Orleans.Serialization.Cloning
{
    /// <summary>
    /// 提供 <see cref="IDeepCopier{T}"/> 实例。
    /// </summary>
    public interface IDeepCopierProvider
    {
        /// <summary>
        /// 获得一个能复制类型 <typeparamref name="T"/>的深部复印机。
        /// </summary>
        /// <typeparam name="T">由复印机支持的类型。</typeparam>
        /// <returns>能够复制类型实例的深层复印机 <typeparamref name="T"/>.</returns>
        IDeepCopier<T> GetDeepCopier<T>();

        /// <summary>
        /// 获得一个能够复制类型 <typeparamref name="T"/>的深部复印机,或者如果找不到合适的复印机则返回 <see langword="null"/> 。
        /// </summary>
        /// <typeparam name="T">由复印机支持的类型。</typeparam>
        /// <returns>能够复制类型实例的深层复印机 <typeparamref name="T"/>,或 <see langword="null"/> 如果找不到适当的复印机。</returns>
        IDeepCopier<T>? TryGetDeepCopier<T>();

        /// <summary>
        /// 获得一个能复制类型 <paramref name="type"/>的深部复印机。
        /// </summary>
        /// <param name="type">
        /// 由返回的复印机支持的类型 。
        /// </param>
        /// <returns>能够复制类型实例的深层复印机 <paramref name="type"/>.</returns>
        IDeepCopier GetDeepCopier(Type type);

        /// <summary>
        /// 获得一个能够复制类型 <paramref name="type"/>的深部复印机,或者如果找不到合适的复印机则返回 <see langword="null"/> 。
        /// </summary>
        /// <param name="type">
        /// 由返回的复印机支持的类型 。
        /// </param>
        /// <returns>能够复制类型实例的深层复印机 <paramref name="type"/>,或 <see langword="null"/> 如果找不到适当的复印机。</returns>
        IDeepCopier? TryGetDeepCopier(Type type);

        /// <summary>
        /// 获得一个能够复制类型 <typeparamref name="T"/>实例的基型复印机。
        /// </summary>
        /// <typeparam name="T">
        /// 由返回的复印机支持的类型 。
        /// </typeparam>
        /// <returns>能够复制类型实例的基型复印机 <typeparamref name="T"/>.</returns>
        IBaseCopier<T> GetBaseCopier<T>() where T : class;
    }

    /// <summary>
    /// 深影印机的标记类型。
    /// </summary>
    public interface IDeepCopier
    {
        /// <summary>
        /// 创建提供未输入的深重输入副本。 此类型必须仍然与复制实例匹配!
        /// </summary>
        [return: NotNullIfNotNull(nameof(input))]
        object? DeepCopy(object? input, CopyContext context);
    }

    /// <summary>
    /// 深影印件机的标记界面,这些机型可以选择浅可复制。
    /// </summary>
    public interface IOptionalDeepCopier : IDeepCopier
    {
        /// <summary>
        /// 如果类型为浅可复制,则返回真实。
        /// </summary>
        /// <returns><see langword="true"/> 如果该类型是浅可复制的;否则, <see langword="false"/>。</returns>
        bool IsShallowCopyable();
    }

    internal sealed class ShallowCopier : IOptionalDeepCopier
    {
        public static readonly ShallowCopier Instance = new();

        public bool IsShallowCopyable() => true;
        [return: NotNullIfNotNull(nameof(input))]
        public object? DeepCopy(object? input, CopyContext _) => input;
    }

    /// <summary>
    /// 深影印件机的底部类型,这些机型实际上可以浅显可复制。
    /// </summary>
    public class ShallowCopier<T> : IOptionalDeepCopier, IDeepCopier<T>
    {
        /// <inheritdoc/>
        public bool IsShallowCopyable() => true;

        /// <summary>返回输入值。</summary>
        [return: NotNullIfNotNull(nameof(input))]
        public T? DeepCopy(T? input, CopyContext _) => input;

        /// <summary>返回输入值。</summary>
        [return: NotNullIfNotNull(nameof(input))]
        public object? DeepCopy(object? input, CopyContext _) => input;
    }

    /// <summary>
    /// 提供创建类型 <typeparamref name="T"/>物体克隆的功能。
    /// </summary>
    /// <typeparam name="T">此实例可复制的对象类型。</typeparam>
    /// <seealso cref="Orleans.Serialization.Cloning.IDeepCopier" />
    public interface IDeepCopier<T> : IDeepCopier
    {
        /// <summary>
        /// 创建所提供输入的深度副本。
        /// </summary>
        /// <param name="input">输入。</param>
        /// <param name="context">上下文。</param>
        /// <returns>
        /// <paramref name="input"/>或 <see langword="null"/> 的复制件,如果 <paramref name="input"/> 是 <see langword="null"/>。
        /// </returns>
        [return: NotNullIfNotNull(nameof(input))]
        T? DeepCopy(T? input, CopyContext context);

        [return: NotNullIfNotNull(nameof(input))]
        object? IDeepCopier.DeepCopy(object? input, CopyContext context) => input is null ? null : DeepCopy((T)input, context);
    }

    /// <summary>
    /// 底型复印机的标记类型。
    /// </summary>
    public interface IBaseCopier
    {
    }

    /// <summary>
    /// 提供将成员从一个物体复制到另一个物体的功能。
    /// </summary>
    /// <typeparam name="T">此实例可复制的对象类型。</typeparam>
    /// <seealso cref="Orleans.Serialization.Cloning.IBaseCopier" />
    public interface IBaseCopier<T> : IBaseCopier where T : class
    {
        /// <summary>
        /// 克隆成员从 <paramref name="input"/> 复制至 <paramref name="output"/>。
        /// </summary>
        /// <param name="input">输入。</param>
        /// <param name="output">输出。</param>
        /// <param name="context">上下文。</param>
        void DeepCopy(T input, T output, CopyContext context);
    }

    /// <summary>
    /// 表示 <see cref="IDeepCopier"/> 执行对所有子类型都具有通用性。
    /// </summary>
    public interface IDerivedTypeCopier : IDeepCopier
    {
    }

    /// <summary>
    /// 提供复制多种类型对象的功能。
    /// </summary>
    public interface IGeneralizedCopier : IDeepCopier
    {
        /// <summary>
        /// 返回一个值,以显示所提供的类型是否得到此执行的支持。
        /// </summary>
        /// <param name="type">类型。</param>
        /// <returns><see langword="true"/> ,如果该类型得到这种执行的支持的类型;否则, <see langword="false"/>。</returns>
        bool IsSupportedType(Type type);
    }

    /// <summary>
    /// 提供功能,用于创建支持特定类型的 <see cref="IDeepCopier"/> 实例。
    /// </summary>
    public interface ISpecializableCopier
    {
        /// <summary>
        /// 返回一个值,以显示所提供的类型是否得到此执行的支持。
        /// </summary>
        /// <param name="type">类型。</param>
        /// <returns><see langword="true"/> ,如果该类型得到这种执行的支持的类型;否则, <see langword="false"/>。</returns>
        bool IsSupportedType(Type type);

        /// <summary>
        /// 获得支持指定类型的 <see cref="IDeepCopier"/> 执行。
        /// </summary>
        /// <param name="type">类型。</param>
        /// <returns>A 音 A <see cref="IDeepCopier"/> 支持指定类型的执行。</returns>
        IDeepCopier GetSpecializedCopier(Type type);
    }

    /// <summary>
    /// 提供了复制操作的背景。
    /// </summary>
    /// <seealso cref="System.IDisposable" />
    /// <remarks>
    /// 初始化 <see cref="CopyContext"/> 类的新实例。
    /// </remarks>
    /// <param name="codecProvider">编码器提供者。</param>
    /// <param name="onDisposed">当处理此上下文时要呼叫的动作 。</param>
    public sealed class CopyContext(CodecProvider codecProvider, Action<CopyContext> onDisposed) : IDisposable
    {
        private readonly Dictionary<object, object> _copies = new(ReferenceEqualsComparer.Default);
        private readonly CodecProvider _copierProvider = codecProvider;
        private readonly Action<CopyContext> _onDisposed = onDisposed;

        /// <summary>
        /// 如果提供对象存在,则返回该对象先前记录的复制件。
        /// </summary>
        /// <typeparam name="T">对象类型。</typeparam>
        /// <param name="original">原始对象</param>
        /// <param name="result">以前记录的 <paramref name="original"/>副本。</param>
        /// <returns><see langword="true"/> ,如果已记录 <paramref name="original"/> 副本,则以其他方式 <see langword="false"/> 。</returns>
        /// <remarks>
        /// <see langword="null"/> 原件作为已复制的处理,并产生 <see langword="null"/> 结果。
        /// 对于非null 原件, <see langword="true"/> 返回保证不null 结果。
        /// </remarks>
        /// <exception cref="InvalidCastException">
        /// 录音副本不可转让给 <typeparamref name="T"/>。
        /// </exception>
        public bool TryGetCopy<T>([NotNullWhen(false)] object? original, out T? result) where T : class
        {
            if (original is null)
            {
                result = null;
                return true;
            }

            if (_copies.TryGetValue(original, out var existing))
            {
                result = existing as T ?? ThrowInvalidRecordedCopyType<T>(original, existing);
                return true;
            }

            result = null;
            return false;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static T ThrowInvalidRecordedCopyType<T>(object original, object? value) where T : class
            => throw new InvalidCastException(
                $"The copy recorded for an object of type '{original.GetType()}' is of type "
                + $"'{value?.GetType().ToString() ?? "null"}' and cannot be returned as '{typeof(T)}'.");

        /// <summary>
        /// 记录一个物体的副本。
        /// </summary>
        /// <param name="original">原始价值。</param>
        /// <param name="copy"><paramref name="original"/> 的副本。</param>
        public void RecordCopy(object original, object copy)
        {
            _copies[original] = copy;
        }

        /// <summary>
        /// 重设此实例 。
        /// </summary>
        public void Reset() => _copies.Clear();

        /// <summary>
        /// 所提供的价值副本。
        /// </summary>
        /// <typeparam name="T">值类型。</typeparam>
        /// <param name="value">数值。</param>
        /// <returns>提供价值的副本。</returns>
        [return: NotNullIfNotNull(nameof(value))]
        public T? DeepCopy<T>(T? value)
        {
            if (default(T) is null)
            {
                if (value is null) return default;
            }

            var copier = _copierProvider.GetDeepCopier(value!.GetType());
            return (T?)copier.DeepCopy(value, this)!;
        }

        /// <inheritdoc/>
        public void Dispose() => _onDisposed?.Invoke(this);
    }

    internal static class ShallowCopyableTypes
    {
        private static readonly ConcurrentDictionary<Type, bool> Types = new()
        {
            [typeof(decimal)] = true,
            [typeof(DateTime)] = true,
#if NET6_0_OR_GREATER
            [typeof(DateOnly)] = true,
            [typeof(TimeOnly)] = true,
#endif
            [typeof(DateTimeOffset)] = true,
            [typeof(TimeSpan)] = true,
            [typeof(IPAddress)] = true,
            [typeof(IPEndPoint)] = true,
            [typeof(string)] = true,
            [typeof(CancellationToken)] = true,
            [typeof(Guid)] = true,
            [typeof(BitVector32)] = true,
            [typeof(CompareInfo)] = true,
            [typeof(CultureInfo)] = true,
            [typeof(Version)] = true,
            [typeof(Uri)] = true,
#if NET7_0_OR_GREATER
            [typeof(UInt128)] = true,
            [typeof(Int128)] = true,
#endif
            [typeof(System.Numerics.BigInteger)] = true,
#if NET5_0_OR_GREATER
            [typeof(Half)] = true,
#endif
        };

        public static bool Contains(Type type)
        {
            if (Types.TryGetValue(type, out var result))
            {
                return result;
            }

            return Types.GetOrAdd(type, IsShallowCopyableInternal(type));
        }

#if NET5_0_OR_GREATER
        [UnconditionalSuppressMessage(
            "Trimming",
            "IL2070",
            Justification = "The reflected branch only handles value types. The runtime preserves every instance field which contributes to a value type's layout, including non-public fields, so trimming cannot change this shallow-copyability decision.")]
#endif
        private static bool IsShallowCopyableInternal(Type type)
        {
            if (type.IsPrimitive || type.IsEnum)
            {
                return true;
            }

            if (type.IsSealed && type.IsDefined(typeof(ImmutableAttribute), false))
            {
                return true;
            }

            if (type.IsConstructedGenericType)
            {
                var def = type.GetGenericTypeDefinition();

                if (def == typeof(Nullable<>)
                    || def == typeof(Tuple<>)
                    || def == typeof(Tuple<,>)
                    || def == typeof(Tuple<,,>)
                    || def == typeof(Tuple<,,,>)
                    || def == typeof(Tuple<,,,,>)
                    || def == typeof(Tuple<,,,,,>)
                    || def == typeof(Tuple<,,,,,,>)
                    || def == typeof(Tuple<,,,,,,,>))
                {
                    return Array.TrueForAll(type.GenericTypeArguments, a => Contains(a));
                }
            }

            if (type.IsValueType && !type.IsGenericTypeDefinition)
            {
                return Array.TrueForAll(type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic), f => Contains(f.FieldType));
            }

            if (typeof(Exception).IsAssignableFrom(type))
                return true;

            if (typeof(Type).IsAssignableFrom(type))
                return true;

            return false;
        }
    }

    /// <summary>
    /// 将非类型复印机转换为强型复印机。
    /// </summary>
    internal sealed class UntypedCopierWrapper<T>(IDeepCopier copier) : IDeepCopier<T>
    {
        private readonly IDeepCopier _copier = copier;

        [return: NotNullIfNotNull(nameof(original))]
        public T? DeepCopy(T? original, CopyContext context) => (T?)_copier.DeepCopy(original, context);

        [return: NotNullIfNotNull(nameof(original))]
        public object? DeepCopy(object? original, CopyContext context) => _copier.DeepCopy(original, context);
    }

    /// <summary>
    /// <see cref="CopyContext"/> 实例对象集合。
    /// </summary>
    public sealed class CopyContextPool : IDisposable
    {
        private readonly ConcurrentObjectPool<CopyContext, PoolPolicy> _pool;

        /// <summary>
        /// 初始化 <see cref="CopyContextPool"/> 类的新实例。
        /// </summary>
        /// <param name="codecProvider">编码器提供者。</param>
        public CopyContextPool(CodecProvider codecProvider)
        {
            var sessionPoolPolicy = new PoolPolicy(codecProvider, Return);
            _pool = new ConcurrentObjectPool<CopyContext, PoolPolicy>(sessionPoolPolicy);
        }

        /// <summary>
        /// 获得 <see cref="CopyContext"/>。
        /// </summary>
        /// <returns>A <see cref="CopyContext"/>.</returns>
        public CopyContext GetContext() => _pool.Get();

        /// <inheritdoc/>
        public void Dispose() => _pool.Dispose();

        /// <summary>
        /// 返回该集合的指定复制上下文。
        /// </summary>
        /// <param name="context">上下文。</param>
        private void Return(CopyContext context) => _pool.Return(context);

        private readonly struct PoolPolicy(CodecProvider codecProvider, Action<CopyContext> onDisposed) : IPooledObjectPolicy<CopyContext>
        {
            private readonly CodecProvider _codecProvider = codecProvider;
            private readonly Action<CopyContext> _onDisposed = onDisposed;

            public CopyContext Create() => new(_codecProvider, _onDisposed);

            public bool Return(CopyContext obj)
            {
                obj.Reset();
                return true;
            }
        }
    }
}
