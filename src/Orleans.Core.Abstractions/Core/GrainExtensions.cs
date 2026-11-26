using System;
using Orleans.Runtime;
using System.Diagnostics.CodeAnalysis;

namespace Orleans
{
    /// <summary>
    /// grains的推广方法。
    /// </summary>
    public static class GrainExtensions
    {
        private const string WRONG_GRAIN_ERROR_MSG = "Passing a half baked grain as an argument. It is possible that you instantiated a grain class explicitly, as a regular object and not via Orleans runtime or via proper test mocking";

        /// <summary>
        /// 返回所提供 grain的引用。
        /// </summary>
        /// <param name="grain">创建参考的 grain 。</param>
        /// <returns>提及所提供的内容 grain.</returns>
        internal static GrainReference AsReference(this IAddressable grain)
        {
            if (grain is null)
            {
                ThrowGrainNull();
            }

            // 当对一个 grain 参考类的例子被调用时,什么都不做
            var reference = grain as GrainReference;
            if (reference != null) return reference;

            var context = grain switch
            {
                Grain grainBase => grainBase.GrainContext,
                IGrainBase activation => activation.GrainContext,
                ISystemTargetBase systemTarget => systemTarget,
                _ => throw new ArgumentException(GetWrongGrainTypeErrorMessage(grain), nameof(grain))
            };

            if (context?.GrainReference is not { } grainRef)
            {
                throw new ArgumentException(WRONG_GRAIN_ERROR_MSG, nameof(grain));
            }

            return grainRef;
        }

        /// <summary>
        /// 返回所提供 grain的已输入引用。
        /// </summary>
        /// <typeparam name="TGrainInterface"> grain 接口的类型。</typeparam>
        /// <param name="grain">下 grain 要转换。</param>
        /// <remarks>
        /// 如果提供的值是一个 grain 实例,这将创建一个执行所提供接口的引用。
        /// 如果提供值已经是 grain 引用, 这将创建一个新的引用, 以实施提供的接口 。
        /// </remarks>
        /// <returns>强烈打印的提及规定 grain 执行 <typeparamref name="TGrainInterface"/>.</returns>
        public static TGrainInterface AsReference<TGrainInterface>(this IAddressable grain)
        {
            if (grain is null)
            {
                ThrowGrainNull();
            }

            var grainReference = grain.AsReference();
            return (TGrainInterface)grainReference.Runtime.Cast(grain, typeof(TGrainInterface));
        }

        /// <summary>
        /// 返回所提供 grain的已输入引用。
        /// </summary>
        /// <typeparam name="TGrainInterface"> grain 接口的类型。</typeparam>
        /// <param name="grain">下 grain 要转换。</param>
        /// <remarks>
        /// 这一方法相当于 <see cref="AsReference{TGrainInterface}"/>。
        /// 如果提供的值是一个 grain 实例,这将创建一个执行所提供接口的引用。
        /// 如果提供值已经是 grain 引用, 这将创建一个新的引用, 以实施提供的接口 。
        /// </remarks>
        /// <returns>强烈打印的提及规定 grain 执行 <typeparamref name="TGrainInterface"/>.</returns>
        public static TGrainInterface Cast<TGrainInterface>(this IAddressable grain) => grain.AsReference<TGrainInterface>();

        /// <summary>
        /// 返回所提供 grain的已输入引用。
        /// </summary>
        /// <param name="grain">下 grain 要转换。</param>
        /// <param name="interfaceType"> grain 接口的类型。</param>
        /// <remarks>
        /// 如果提供的值是一个 grain 实例,这将创建一个执行所提供接口的引用。
        /// 如果提供值已经是 grain 引用, 这将创建一个新的引用, 以实施提供的接口 。
        /// </remarks>
        /// <returns>强烈打印的提及规定 grain 执行 <paramref name="interfaceType"/>.</returns>
        public static object AsReference(this IAddressable grain, Type interfaceType) => grain.AsReference().Runtime.Cast(grain, interfaceType);

        /// <summary>
        /// 返回所提供 grain的已输入引用。
        /// </summary>
        /// <param name="grain">下 grain 要转换。</param>
        /// <param name="interfaceType"> grain 接口的类型。</param>
        /// <remarks>
        /// 这一方法相当于 <see cref="AsReference(IAddressable, Type)"/>。
        /// 如果提供的值是一个 grain 实例,这将创建一个执行所提供接口的引用。
        /// 如果提供值已经是 grain 引用, 这将创建一个新的引用, 以实施提供的接口 。
        /// </remarks>
        /// <returns>强烈打印的提及规定 grain 执行 <paramref name="interfaceType"/>.</returns>
        public static object Cast(this IAddressable grain, Type interfaceType) => grain.AsReference().Runtime.Cast(grain, interfaceType);

        /// <summary>
        /// 返回 grain id 与所提供的对应 grain.
        /// </summary>
        /// <param name="grain">下 grain</param>
        /// <returns>下 grain id 与所提供的对应 grain.</returns>
        /// <exception cref="ArgumentException">所提供的数值类型错误或没有id。</exception>
        public static GrainId GetGrainId(this IAddressable grain)
        {
            var grainId = grain switch
            {
                Grain grainBase => grainBase.GrainId,
                GrainReference grainReference => grainReference.GrainId,
                IGrainBase grainActivation => grainActivation.GrainContext.GrainId,
                ISystemTargetBase systemTarget => systemTarget.GrainId,
                _ => throw new ArgumentException(GetWrongGrainTypeErrorMessage(grain), nameof(grain))
            };

            if (grainId.IsDefault)
            {
                throw new ArgumentException(WRONG_GRAIN_ERROR_MSG, nameof(grain));
            }

            return grainId;
        }

        /// <summary>
        /// 获取在 grain 参数有非支持的执行类型时所丢出的特殊信息。
        /// </summary>
        /// <param name="grain">参数。</param>
        /// <returns>当 grain 参数有非支持性执行类型时所投出的特殊信息。</returns>
        private static string GetWrongGrainTypeErrorMessage(IAddressable grain) =>
            $"{nameof(GetGrainId)} has been called on an unexpected type: {grain.GetType().FullName}."
            + $" If the parameter is a grain implementation, you can derive from {nameof(Grain)} or implement"
            + $" {nameof(IGrainBase)} in order to support this method. Alternatively, inject {nameof(IGrainContext)} and"
            + $" access the {nameof(IGrainContext.GrainId)} or {nameof(IGrainContext.GrainReference)} property.";

        /// <summary>
        /// 返回主键的一部分是否属于类型 <see langword="long"/>。
        /// </summary>
        /// <param name="grain">目标 grain。</param>
        /// <exception cref="InvalidOperationException">提供的情况 grain 没有 <see cref="long"/>- 以键为基础的</exception>
        public static bool IsPrimaryKeyBasedOnLong(this IAddressable grain)
        {
            var grainId = GetGrainId(grain);
            if (grainId.TryGetIntegerKey(out _))
            {
                return true;
            }

            if (LegacyGrainId.TryConvertFromGrainId(grainId, out var legacyId))
            {
                return legacyId.IsLongKey;
            }

            return false;
        }

        /// <summary>
        /// 返回 <see langword="long"/> 代表代表 grain 基本键。
        /// </summary>
        /// <param name="grain">查找主密钥的 grain 。</param>
        /// <param name="keyExt">返回扩展键部分的输出参数 grain 初级关键,如果为此提供了扩展主关键 grain.</param>
        /// <returns>A <see langword="long"/> 代表此 grain的主要关键字。</returns>
        /// <exception cref="InvalidOperationException">提供的情况 grain 没有 <see cref="long"/>- 以键为基础的</exception>
        public static long GetPrimaryKeyLong(this IAddressable grain, out string? keyExt)
        {
            var grainId = GetGrainId(grain);
            if (grainId.TryGetIntegerKey(out var primaryKey, out keyExt))
            {
                return primaryKey;
            }

            if (LegacyGrainId.TryConvertFromGrainId(grainId, out var legacyId))
            {
                return legacyId.GetPrimaryKeyLong(out keyExt);
            }

            throw new InvalidOperationException($"Unable to extract integer key from grain id {grainId}");
        }

        /// <summary>
        /// 返回 <see langword="long"/> 代表代表 grain 基本键。
        /// </summary>
        /// <param name="grain">查找主密钥的 grain 。</param>
        /// <returns>A <see langword="long"/> 代表此 grain的主要关键字。</returns>
        /// <exception cref="InvalidOperationException">提供的情况 grain 没有 <see cref="long"/>- 以键为基础的</exception>
        public static long GetPrimaryKeyLong(this IAddressable grain)
        {
            var grainId = GetGrainId(grain);
            if (grainId.TryGetIntegerKey(out var primaryKey))
            {
                return primaryKey;
            }

            if (LegacyGrainId.TryConvertFromGrainId(grainId, out var legacyId))
            {
                return legacyId.GetPrimaryKeyLong();
            }

            throw new InvalidOperationException($"Unable to extract integer key from grain id {grainId}");
        }

        /// <summary>
        /// 返回 <see cref="Guid"/> 代表代表 grain 基本键。
        /// </summary>
        /// <param name="grain">查找主密钥的 grain 。</param>
        /// <param name="keyExt">返回扩展键部分的输出参数 grain 初级关键,如果为此提供了扩展主关键 grain.</param>
        /// <returns>A <see cref="Guid"/> 代表此 grain的主要关键字。</returns>
        /// <exception cref="InvalidOperationException">提供的情况 grain 没有 <see cref="Guid"/>- 以键为基础的</exception>
        public static Guid GetPrimaryKey(this IAddressable grain, out string? keyExt)
        {
            var grainId = GetGrainId(grain);
            if (grainId.TryGetGuidKey(out var guid, out keyExt))
            {
                return guid;
            }

            if (LegacyGrainId.TryConvertFromGrainId(grainId, out var legacyId))
            {
                return legacyId.GetPrimaryKey(out keyExt);
            }

            if (grainId.TryGetIntegerKey(out var integerKey, out keyExt))
            {
                var N1 = integerKey;
                return new Guid(0, 0, 0, (byte)N1, (byte)(N1 >> 8), (byte)(N1 >> 16), (byte)(N1 >> 24), (byte)(N1 >> 32), (byte)(N1 >> 40), (byte)(N1 >> 48), (byte)(N1 >> 56));
            }

            throw new InvalidOperationException($"Unable to extract GUID key from grain id {grainId}");
        }

        /// <summary>
        /// 返回 <see cref="Guid"/> 代表代表 grain 基本键。
        /// </summary>
        /// <param name="grain">查找主密钥的 grain 。</param>
        /// <returns>A <see cref="Guid"/> 代表此 grain的主要关键字。</returns>
        /// <exception cref="InvalidOperationException">提供的情况 grain 没有 <see cref="Guid"/>- 以键为基础的</exception>
        public static Guid GetPrimaryKey(this IAddressable grain)
        {
            var grainId = GetGrainId(grain);
            if (grainId.TryGetGuidKey(out var guid))
                return guid;

            if (LegacyGrainId.TryConvertFromGrainId(grainId, out var legacyId))
                return legacyId.GetPrimaryKey();

            if (grainId.TryGetIntegerKey(out var integerKey))
            {
                var N1 = integerKey;
                return new Guid(0, 0, 0, (byte)N1, (byte)(N1 >> 8), (byte)(N1 >> 16), (byte)(N1 >> 24), (byte)(N1 >> 32), (byte)(N1 >> 40), (byte)(N1 >> 48), (byte)(N1 >> 56));
            }

            throw new InvalidOperationException($"Unable to extract GUID key from grain id {grainId}");
        }

        /// <summary>
        /// 返回 <see langword="string"/> 键的 grain.
        /// </summary>
        /// <param name="grain">查找主密钥的 grain 。</param>
        /// <returns>A <see langword="string"/> 代表此 grain的主要关键字。</returns>
        public static string GetPrimaryKeyString(this IAddressable grain)
        {
            var grainId = GetGrainId(grain);
            if (LegacyGrainId.TryConvertFromGrainId(grainId, out var legacyId))
            {
                return legacyId.GetPrimaryKeyString();
            }

            return grainId.Key.ToString();
        }

        /// <summary>
        /// 丢弃 <see cref="ArgumentNullException"/> ,表示 grain 参数无效。
        /// </summary>
        /// <exception cref="ArgumentNullException"> grain 参数为无效。</exception>
        [DoesNotReturn]
        private static void ThrowGrainNull() => throw new ArgumentNullException("grain");
    }
}
