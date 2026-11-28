using System;
using Orleans.Runtime;
using System.Diagnostics.CodeAnalysis;

namespace Orleans
{
    /// <summary>
    /// Grain 的扩展方法。
    /// </summary>
    public static class GrainExtensions
    {
        private const string WRONG_GRAIN_ERROR_MSG = "Passing a half baked grain as an argument. It is possible that you instantiated a grain class explicitly, as a regular object and not via Orleans runtime or via proper test mocking";

        /// <summary>
        /// 返回指向指定 Grain 的引用。
        /// </summary>
        /// <param name="grain">要为其创建引用的 Grain。</param>
        /// <returns>指定 Grain 的引用。</returns>
        internal static GrainReference AsReference(this IAddressable grain)
        {
            if (grain is null)
            {
                ThrowGrainNull();
            }

            // 如果针对 Grain 引用类的实例调用，则不执行任何操作。
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
        /// 返回指定 Grain 的类型化引用。
        /// </summary>
        /// <typeparam name="TGrainInterface">Grain 接口的类型。</typeparam>
        /// <param name="grain">要转换的 Grain。</param>
        /// <remarks>
        /// 如果提供的是 Grain 实例，将创建一个实现指定接口的引用。
        /// 如果提供的值已经是 Grain 引用，则创建一个实现指定接口的新引用。
        /// </remarks>
        /// <returns>指向指定 Grain 且实现 <typeparamref name="TGrainInterface"/> 的强类型引用。</returns>
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
        /// 返回指定 Grain 的类型化引用。
        /// </summary>
        /// <typeparam name="TGrainInterface">Grain 接口的类型。</typeparam>
        /// <param name="grain">要转换的 Grain。</param>
        /// <remarks>
        /// 此方法等效于 <see cref="AsReference{TGrainInterface}"/>。
        /// 如果提供的是 Grain 实例，将创建一个实现指定接口的引用。
        /// 如果提供的值已经是 Grain 引用，则创建一个实现指定接口的新引用。
        /// </remarks>
        /// <returns>指向指定 Grain 且实现 <typeparamref name="TGrainInterface"/> 的强类型引用。</returns>
        public static TGrainInterface Cast<TGrainInterface>(this IAddressable grain) => grain.AsReference<TGrainInterface>();

        /// <summary>
        /// 返回指定 Grain 的类型化引用。
        /// </summary>
        /// <param name="grain">要转换的 Grain。</param>
        /// <param name="interfaceType">Grain 接口的类型。</param>
        /// <remarks>
        /// 如果提供的是 Grain 实例，将创建一个实现指定接口的引用。
        /// 如果提供的值已经是 Grain 引用，则创建一个实现指定接口的新引用。
        /// </remarks>
        /// <returns>指向指定 Grain 且实现 <paramref name="interfaceType"/> 的强类型引用。</returns>
        public static object AsReference(this IAddressable grain, Type interfaceType) => grain.AsReference().Runtime.Cast(grain, interfaceType);

        /// <summary>
        /// 返回指定 Grain 的类型化引用。
        /// </summary>
        /// <param name="grain">要转换的 Grain。</param>
        /// <param name="interfaceType">Grain 接口的类型。</param>
        /// <remarks>
        /// 此方法等效于 <see cref="AsReference(IAddressable, Type)"/>。
        /// 如果提供的是 Grain 实例，将创建一个实现指定接口的引用。
        /// 如果提供的值已经是 Grain 引用，则创建一个实现指定接口的新引用。
        /// </remarks>
        /// <returns>指向指定 Grain 且实现 <paramref name="interfaceType"/> 的强类型引用。</returns>
        public static object Cast(this IAddressable grain, Type interfaceType) => grain.AsReference().Runtime.Cast(grain, interfaceType);

        /// <summary>
        /// 返回与指定 Grain 对应的 Grain ID。
        /// </summary>
        /// <param name="grain">Grain。</param>
        /// <returns>指定 Grain 对应的 Grain ID。</returns>
        /// <exception cref="ArgumentException">提供的值类型错误或没有 ID。</exception>
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
        /// 获取 Grain 参数的实现类型不受支持时所抛出异常的消息。
        /// </summary>
        /// <param name="grain">参数。</param>
        /// <returns>Grain 参数的实现类型不受支持时所抛出异常的消息。</returns>
        private static string GetWrongGrainTypeErrorMessage(IAddressable grain) =>
            $"{nameof(GetGrainId)} has been called on an unexpected type: {grain.GetType().FullName}."
            + $" If the parameter is a grain implementation, you can derive from {nameof(Grain)} or implement"
            + $" {nameof(IGrainBase)} in order to support this method. Alternatively, inject {nameof(IGrainContext)} and"
            + $" access the {nameof(IGrainContext.GrainId)} or {nameof(IGrainContext.GrainReference)} property.";

        /// <summary>
        /// 返回主键的一部分是否为 <see langword="long"/> 类型。
        /// </summary>
        /// <param name="grain">目标 Grain。</param>
        /// <exception cref="InvalidOperationException">指定 Grain 不具有基于 <see cref="long"/> 的键。</exception>
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
        /// 返回 Grain 主键的 <see langword="long"/> 表示形式。
        /// </summary>
        /// <param name="grain">要获取其主键的 Grain。</param>
        /// <param name="keyExt">输出参数；如果该 Grain 使用了扩展主键，则返回其主键的扩展部分。</param>
        /// <returns>表示此 Grain 主键的 <see langword="long"/> 值。</returns>
        /// <exception cref="InvalidOperationException">指定 Grain 不具有基于 <see cref="long"/> 的键。</exception>
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
        /// 返回 Grain 主键的 <see langword="long"/> 表示形式。
        /// </summary>
        /// <param name="grain">要获取其主键的 Grain。</param>
        /// <returns>表示此 Grain 主键的 <see langword="long"/> 值。</returns>
        /// <exception cref="InvalidOperationException">指定 Grain 不具有基于 <see cref="long"/> 的键。</exception>
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
        /// 返回 Grain 主键的 <see cref="Guid"/> 表示形式。
        /// </summary>
        /// <param name="grain">要获取其主键的 Grain。</param>
        /// <param name="keyExt">输出参数；如果该 Grain 使用了扩展主键，则返回其主键的扩展部分。</param>
        /// <returns>表示此 Grain 主键的 <see cref="Guid"/> 值。</returns>
        /// <exception cref="InvalidOperationException">指定 Grain 不具有基于 <see cref="Guid"/> 的键。</exception>
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
        /// 返回 Grain 主键的 <see cref="Guid"/> 表示形式。
        /// </summary>
        /// <param name="grain">要获取其主键的 Grain。</param>
        /// <returns>表示此 Grain 主键的 <see cref="Guid"/> 值。</returns>
        /// <exception cref="InvalidOperationException">指定 Grain 不具有基于 <see cref="Guid"/> 的键。</exception>
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
        /// 返回 Grain 的 <see langword="string"/> 主键。
        /// </summary>
        /// <param name="grain">要获取其主键的 Grain。</param>
        /// <returns>表示此 Grain 主键的 <see langword="string"/> 值。</returns>
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
        /// 抛出 <see cref="ArgumentNullException"/>，指示 Grain 参数为 null。
        /// </summary>
        /// <exception cref="ArgumentNullException">Grain 参数为 null。</exception>
        [DoesNotReturn]
        private static void ThrowGrainNull() => throw new ArgumentNullException("grain");
    }
}
