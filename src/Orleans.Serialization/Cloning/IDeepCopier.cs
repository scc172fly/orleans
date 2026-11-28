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
        /// 获取能够复制 <typeparamref name="T"/> 类型实例的深拷贝器。
        /// </summary>
        /// <typeparam name="T">拷贝器支持的类型。</typeparam>
        /// <returns>能够复制 <typeparamref name="T"/> 类型实例的深拷贝器。</returns>
        IDeepCopier<T> GetDeepCopier<T>();

        /// <summary>
        /// 获取能够复制 <typeparamref name="T"/> 类型实例的深拷贝器；若未找到合适的拷贝器，则返回 <see langword="null"/>。
        /// </summary>
        /// <typeparam name="T">拷贝器支持的类型。</typeparam>
        /// <returns>能够复制 <typeparamref name="T"/> 类型实例的深拷贝器；若未找到合适的拷贝器，则为 <see langword="null"/>。</returns>
        IDeepCopier<T>? TryGetDeepCopier<T>();

        /// <summary>
        /// 获取能够复制 <paramref name="type"/> 类型实例的深拷贝器。
        /// </summary>
        /// <param name="type">
        /// 返回的拷贝器所支持的类型。
        /// </param>
        /// <returns>能够复制 <paramref name="type"/> 类型实例的深拷贝器。</returns>
        IDeepCopier GetDeepCopier(Type type);

        /// <summary>
        /// 获取能够复制 <paramref name="type"/> 类型实例的深拷贝器；若未找到合适的拷贝器，则返回 <see langword="null"/>。
        /// </summary>
        /// <param name="type">
        /// 返回的拷贝器所支持的类型。
        /// </param>
        /// <returns>能够复制 <paramref name="type"/> 类型实例的深拷贝器；若未找到合适的拷贝器，则为 <see langword="null"/>。</returns>
        IDeepCopier? TryGetDeepCopier(Type type);

        /// <summary>
        /// 获取能够复制 <typeparamref name="T"/> 类型实例的基类型拷贝器。
        /// </summary>
        /// <typeparam name="T">
        /// 返回的拷贝器所支持的类型。
        /// </typeparam>
        /// <returns>能够复制 <typeparamref name="T"/> 类型实例的基类型拷贝器。</returns>
        IBaseCopier<T> GetBaseCopier<T>() where T : class;
    }

    /// <summary>
    /// 深拷贝器的标记类型。
    /// </summary>
    public interface IDeepCopier
    {
        /// <summary>
        /// 对提供的无类型输入创建深拷贝。输入的实际类型仍必须与拷贝器实例匹配！
        /// </summary>
        [return: NotNullIfNotNull(nameof(input))]
        object? DeepCopy(object? input, CopyContext context);
    }

    /// <summary>
    /// 用于可能支持浅拷贝的类型的深拷贝器的标记接口。
    /// </summary>
    public interface IOptionalDeepCopier : IDeepCopier
    {
        /// <summary>
        /// 如果类型可以浅拷贝，则返回 true。
        /// </summary>
        /// <returns><see langword="true"/> 表示该类型可浅拷贝；否则为 <see langword="false"/>。</returns>
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
    /// 实际支持浅拷贝的类型所使用的深拷贝器基类型。
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
    /// 提供创建 <typeparamref name="T"/> 类型对象副本的功能。
    /// </summary>
    /// <typeparam name="T">此实例能够复制的对象类型。</typeparam>
    /// <seealso cref="Orleans.Serialization.Cloning.IDeepCopier" />
    public interface IDeepCopier<T> : IDeepCopier
    {
        /// <summary>
        /// 创建给定输入的深拷贝。
        /// </summary>
        /// <param name="input">输入。</param>
        /// <param name="context">上下文。</param>
        /// <returns>
        /// <paramref name="input"/> 的副本；或者返回 <see langword="null"/>（当 <paramref name="input"/> 为 <see langword="null"/> 时）。
        /// </returns>
        [return: NotNullIfNotNull(nameof(input))]
        T? DeepCopy(T? input, CopyContext context);

        [return: NotNullIfNotNull(nameof(input))]
        object? IDeepCopier.DeepCopy(object? input, CopyContext context) => input is null ? null : DeepCopy((T)input, context);
    }

    /// <summary>
    /// 基类型拷贝器的标记类型。
    /// </summary>
    public interface IBaseCopier
    {
    }

    /// <summary>
    /// 提供将成员从一个对象复制到另一个对象的功能。
    /// </summary>
    /// <typeparam name="T">此实例能够复制的对象类型。</typeparam>
    /// <seealso cref="Orleans.Serialization.Cloning.IBaseCopier" />
    public interface IBaseCopier<T> : IBaseCopier where T : class
    {
        /// <summary>
        /// 克隆 <paramref name="input"/> 的成员并将其复制到 <paramref name="output"/>。
        /// </summary>
        /// <param name="input">输入。</param>
        /// <param name="output">输出对象。</param>
        /// <param name="context">上下文。</param>
        void DeepCopy(T input, T output, CopyContext context);
    }

    /// <summary>
    /// 表示 <see cref="IDeepCopier"/> 的实现适用于所有子类型。
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
        /// 返回一个值，指示此实现是否支持给定类型。
        /// </summary>
        /// <param name="type">类型。</param>
        /// <returns><see langword="true"/> 表示此实现支持该类型；否则为 <see langword="false"/>。</returns>
        bool IsSupportedType(Type type);
    }

    /// <summary>
    /// 提供创建支持给定类型的 <see cref="IDeepCopier"/> 实例的功能。
    /// </summary>
    public interface ISpecializableCopier
    {
        /// <summary>
        /// 返回一个值，指示此实现是否支持给定类型。
        /// </summary>
        /// <param name="type">类型。</param>
        /// <returns><see langword="true"/> 表示此实现支持该类型；否则为 <see langword="false"/>。</returns>
        bool IsSupportedType(Type type);

        /// <summary>
        /// 获取支持指定类型的 <see cref="IDeepCopier"/> 实现。
        /// </summary>
        /// <param name="type">类型。</param>
        /// <returns>支持指定类型的 <see cref="IDeepCopier"/> 实现。</returns>
        IDeepCopier GetSpecializedCopier(Type type);
    }

    /// <summary>
    /// 提供拷贝操作的上下文。
    /// </summary>
    /// <seealso cref="System.IDisposable" />
    /// <remarks>
    /// 初始化 <see cref="CopyContext"/> 类的新实例。
    /// </remarks>
    /// <param name="codecProvider">编解码器提供程序。</param>
    /// <param name="onDisposed">释放此上下文时调用的操作。</param>
    public sealed class CopyContext(CodecProvider codecProvider, Action<CopyContext> onDisposed) : IDisposable
    {
        private readonly Dictionary<object, object> _copies = new(ReferenceEqualsComparer.Default);
        private readonly CodecProvider _copierProvider = codecProvider;
        private readonly Action<CopyContext> _onDisposed = onDisposed;

        /// <summary>
        /// 返回先前记录的给定对象的副本（如果存在）。
        /// </summary>
        /// <typeparam name="T">对象类型。</typeparam>
        /// <param name="original">原始对象。</param>
        /// <param name="result">先前记录的 <paramref name="original"/> 的副本。</param>
        /// <returns><see langword="true"/> 表示已记录 <paramref name="original"/> 的副本，否则为 <see langword="false"/>。</returns>
        /// <remarks>
        /// <see langword="null"/> 原始对象视为已经拷贝，并产生 <see langword="null"/> 结果。
        /// 对于非 null 的原始对象，返回 <see langword="true"/> 可保证结果非 null。
        /// </remarks>
        /// <exception cref="InvalidCastException">
        /// 记录的副本无法赋值给 <typeparamref name="T"/>。
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
        /// 记录对象的副本。
        /// </summary>
        /// <param name="original">原始值。</param>
        /// <param name="copy"><paramref name="original"/> 的副本。</param>
        public void RecordCopy(object original, object copy)
        {
            _copies[original] = copy;
        }

        /// <summary>
        /// 重置此实例。
        /// </summary>
        public void Reset() => _copies.Clear();

        /// <summary>
        /// 复制给定值。
        /// </summary>
        /// <typeparam name="T">要复制的值的类型。</typeparam>
        /// <param name="value">值。</param>
        /// <returns>给定值的副本。</returns>
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
    /// 将无类型拷贝器转换为强类型拷贝器。
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
    /// <see cref="CopyContext"/> 实例的对象池。
    /// </summary>
    public sealed class CopyContextPool : IDisposable
    {
        private readonly ConcurrentObjectPool<CopyContext, PoolPolicy> _pool;

        /// <summary>
        /// 初始化 <see cref="CopyContextPool"/> 类的新实例。
        /// </summary>
        /// <param name="codecProvider">编解码器提供程序。</param>
        public CopyContextPool(CodecProvider codecProvider)
        {
            var sessionPoolPolicy = new PoolPolicy(codecProvider, Return);
            _pool = new ConcurrentObjectPool<CopyContext, PoolPolicy>(sessionPoolPolicy);
        }

        /// <summary>
        /// 获取一个 <see cref="CopyContext"/>。
        /// </summary>
        /// <returns>一个 <see cref="CopyContext"/>。</returns>
        public CopyContext GetContext() => _pool.Get();

        /// <inheritdoc/>
        public void Dispose() => _pool.Dispose();

        /// <summary>
        /// 将指定拷贝上下文归还对象池。
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
