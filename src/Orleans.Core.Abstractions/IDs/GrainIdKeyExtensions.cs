using System;
using System.Buffers.Text;
using System.Diagnostics;
using System.Text;

namespace Orleans.Runtime
{
    /// <summary>
    /// 用于 <see cref="GrainId"/> 键的扩展方法。
    /// </summary>
    public static class GrainIdKeyExtensions
    {
        /// <summary>
        /// 创建 <see cref="IdSpan"/>，用于表示 <see cref="long"/> 键。
        /// </summary>
        /// <param name="key">
        /// 键。
        /// </param>
        /// <returns>
        /// 表示给定键的 <see cref="IdSpan"/>。
        /// </returns>
        public static IdSpan CreateIntegerKey(long key)
        {
            Span<byte> buf = stackalloc byte[sizeof(long) * 2];
            Utf8Formatter.TryFormat(key, buf, out var len, 'X');
            Debug.Assert(len > 0, "Unable to format the provided value as a UTF8 string");
            return new IdSpan(buf[..len].ToArray());
        }

        /// <summary>
        /// 创建 <see cref="IdSpan"/>，用于表示 <see cref="long"/> 键及键扩展字符串。
        /// </summary>
        /// <param name="key">
        /// 键。
        /// </param>
        /// <param name="keyExtension">
        /// 采用 UTF-8 编码的键扩展。
        /// </param>
        /// <returns>
        /// 表示给定键及键扩展的 <see cref="IdSpan"/>。
        /// </returns>
        public static IdSpan CreateIntegerKey(long key, ReadOnlySpan<byte> keyExtension)
        {
            if (keyExtension.IsEmpty)
                return CreateIntegerKey(key);

            Span<byte> tmp = stackalloc byte[sizeof(long) * 2];
            Utf8Formatter.TryFormat(key, tmp, out var len, 'X');
            Debug.Assert(len > 0, "Unable to format the provided value as a UTF8 string");

            var buf = new byte[len + 1 + keyExtension.Length];
            tmp[..len].CopyTo(buf);
            buf[len] = (byte)'+';
            keyExtension.CopyTo(buf.AsSpan(len + 1));
            return new(buf);
        }

        /// <summary>
        /// 创建 <see cref="IdSpan"/>，用于表示 <see cref="long"/> 键及键扩展字符串。
        /// </summary>
        /// <param name="key">
        /// 键。
        /// </param>
        /// <param name="keyExtension">
        /// 键扩展。
        /// </param>
        /// <returns>
        /// 表示给定键及键扩展的 <see cref="IdSpan"/>。
        /// </returns>
        public static IdSpan CreateIntegerKey(long key, string? keyExtension)
        {
            if (string.IsNullOrWhiteSpace(keyExtension))
            {
                return CreateIntegerKey(key);
            }

            Span<byte> tmp = stackalloc byte[sizeof(long) * 2];
            Utf8Formatter.TryFormat(key, tmp, out var len, 'X');
            Debug.Assert(len > 0, "Unable to format the provided value as a UTF8 string");

            var extLen = Encoding.UTF8.GetByteCount(keyExtension);
            var buf = new byte[len + 1 + extLen];
            tmp[..len].CopyTo(buf);
            buf[len] = (byte)'+';
            Encoding.UTF8.GetBytes(keyExtension, 0, keyExtension.Length, buf, len + 1);

            return new IdSpan(buf);
        }

        /// <summary>
        /// 创建 <see cref="IdSpan"/>，用于表示 <see cref="Guid"/> 键。
        /// </summary>
        /// <param name="key">
        /// 键。
        /// </param>
        /// <returns>
        /// 表示给定键的 <see cref="IdSpan"/>。
        /// </returns>
        public static IdSpan CreateGuidKey(Guid key)
        {
            var buf = new byte[32];
            Utf8Formatter.TryFormat(key, buf, out var len, 'N');
            Debug.Assert(len == 32, "Unable to format the provided value as a UTF8 string");
            return new IdSpan(buf);
        }

        /// <summary>
        /// 创建 <see cref="IdSpan"/>，用于表示 <see cref="Guid"/> 键及键扩展字符串。
        /// </summary>
        /// <param name="key">
        /// 键。
        /// </param>
        /// <param name="keyExtension">
        /// 采用 UTF-8 编码的键扩展。
        /// </param>
        /// <returns>
        /// 表示给定键及键扩展的 <see cref="IdSpan"/>。
        /// </returns>
        public static IdSpan CreateGuidKey(Guid key, ReadOnlySpan<byte> keyExtension)
        {
            if (keyExtension.IsEmpty)
                return CreateGuidKey(key);

            var buf = new byte[32 + 1 + keyExtension.Length];
            Utf8Formatter.TryFormat(key, buf, out var len, 'N');
            Debug.Assert(len == 32, "Unable to format the provided value as a UTF8 string");
            buf[32] = (byte)'+';
            keyExtension.CopyTo(buf.AsSpan(len + 1));
            return new(buf);
        }

        /// <summary>
        /// 创建 <see cref="IdSpan"/>，用于表示 <see cref="Guid"/> 键及键扩展字符串。
        /// </summary>
        /// <param name="key">
        /// 键。
        /// </param>
        /// <param name="keyExtension">
        /// 键扩展。
        /// </param>
        /// <returns>
        /// 表示给定键及键扩展的 <see cref="IdSpan"/>。
        /// </returns>
        public static IdSpan CreateGuidKey(Guid key, string? keyExtension)
        {
            if (string.IsNullOrWhiteSpace(keyExtension))
            {
                return CreateGuidKey(key);
            }

            var extLen = Encoding.UTF8.GetByteCount(keyExtension);
            var buf = new byte[32 + 1 + extLen];
            Utf8Formatter.TryFormat(key, buf, out var len, 'N');
            Debug.Assert(len == 32, "Unable to format the provided value as a UTF8 string");
            buf[32] = (byte)'+';
            Encoding.UTF8.GetBytes(keyExtension, 0, keyExtension.Length, buf, 33);

            return new IdSpan(buf);
        }

        /// <summary>
        /// 尝试解析所提供 Grain ID 的 <see cref="GrainId.Key"/> 部分，以提取 <see cref="long"/> 键和 <see cref="string"/> 键扩展。
        /// </summary>
        /// <param name="grainId">
        /// Grain ID。
        /// </param>
        /// <param name="key">
        /// 键。
        /// </param>
        /// <param name="keyExt">
        /// 键扩展。
        /// </param>
        /// <returns>
        /// 成功解析 Grain ID 时返回 <see langword="true"/>，否则返回 <see langword="false" />。
        /// </returns>
        public static bool TryGetIntegerKey(this GrainId grainId, out long key, out string? keyExt)
        {
            keyExt = null;
            var keyString = grainId.Key.AsSpan();
            if (keyString.IndexOf((byte)'+') is int index && index >= 0)
            {
                keyExt = Encoding.UTF8.GetString(keyString[(index + 1)..]);
                keyString = keyString[..index];
            }

            return Utf8Parser.TryParse(keyString, out key, out var len, 'X') && len == keyString.Length;
        }

        /// <summary>
        /// 尝试解析所提供 Grain ID 的 <see cref="GrainId.Key"/> 部分，以提取 <see cref="long"/> 键。
        /// </summary>
        /// <param name="grainId">
        /// Grain ID。
        /// </param>
        /// <param name="key">
        /// 键。
        /// </param>
        /// <returns>
        /// 成功解析 Grain ID 时返回 <see langword="true"/>，否则返回 <see langword="false" />。
        /// </returns>
        internal static bool TryGetIntegerKey(this GrainId grainId, out long key)
        {
            var keyString = grainId.Key.AsSpan();
            if (keyString.IndexOf((byte)'+') is int index && index >= 0)
                keyString = keyString[..index];

            return Utf8Parser.TryParse(keyString, out key, out var len, 'X') && len == keyString.Length;
        }

        /// <summary>
        /// 返回 Grain 键的 <see cref="long"/> 表示形式。
        /// </summary>
        /// <param name="grainId">Grain ID。</param>
        /// <param name="keyExt">输出参数；如果该 Grain 使用了扩展主键，则返回其主键的扩展部分。</param>
        /// <returns>表示此 Grain 键的 long 值。</returns>
        public static long GetIntegerKey(this GrainId grainId, out string? keyExt)
        {
            if (!grainId.TryGetIntegerKey(out var result, out keyExt))
            {
                ThrowInvalidIntegerKeyFormat(grainId);
            }

            return result;
        }

        /// <summary>
        /// 返回 Grain 键的 <see cref="long"/> 表示形式。
        /// </summary>
        /// <param name="grainId">要获取其键的 Grain。</param>
        /// <returns>表示此 Grain 键的 <see cref="long"/> 值。</returns>
        public static long GetIntegerKey(this GrainId grainId)
        {
            if (!grainId.TryGetIntegerKey(out var result))
            {
                ThrowInvalidIntegerKeyFormat(grainId);
            }

            return result;
        }

        /// <summary>
        /// 尝试解析所提供 Grain ID 的 <see cref="GrainId.Key"/> 部分，以提取 <see cref="Guid"/> 键和 <see cref="string"/> 键扩展。
        /// </summary>
        /// <param name="grainId">
        /// Grain ID。
        /// </param>
        /// <param name="key">
        /// 键。
        /// </param>
        /// <param name="keyExt">
        /// 键扩展。
        /// </param>
        /// <returns>
        /// 成功解析 Grain ID 时返回 <see langword="true"/>，否则返回 <see langword="false" />。
        /// </returns>
        public static bool TryGetGuidKey(this GrainId grainId, out Guid key, out string? keyExt)
        {
            keyExt = null;
            var keyString = grainId.Key.AsSpan();
            if (keyString.Length > 32 && keyString[32] == (byte)'+')
            {
                keyExt = Encoding.UTF8.GetString(keyString[33..]);
                keyString = keyString[..32];
            }
            else if (keyString.Length != 32)
            {
                key = default;
                return false;
            }

            return Utf8Parser.TryParse(keyString, out key, out var len, 'N') && len == 32;
        }

        /// <summary>
        /// 尝试解析所提供 Grain ID 的 <see cref="GrainId.Key"/> 部分，以提取 <see cref="Guid"/> 键。
        /// </summary>
        /// <param name="grainId">
        /// Grain ID。
        /// </param>
        /// <param name="key">
        /// 键。
        /// </param>
        /// <returns>
        /// 成功解析 Grain ID 时返回 <see langword="true"/>，否则返回 <see langword="false" />。
        /// </returns>
        internal static bool TryGetGuidKey(this GrainId grainId, out Guid key)
        {
            var keyString = grainId.Key.AsSpan();
            if (keyString.Length > 32 && keyString[32] == (byte)'+')
            {
                keyString = keyString[..32];
            }
            else if (keyString.Length != 32)
            {
                key = default;
                return false;
            }

            return Utf8Parser.TryParse(keyString, out key, out var len, 'N') && len == 32;
        }

        /// <summary>
        /// 返回 Grain 主键的 <see cref="Guid"/> 表示形式。
        /// </summary>
        /// <param name="grainId">要获取其主键的 Grain。</param>
        /// <param name="keyExt">输出参数；如果该 Grain 使用了扩展主键，则返回其主键的扩展部分。</param>
        /// <returns>表示此 Grain 主键的 <see cref="Guid"/> 值。</returns>
        public static Guid GetGuidKey(this GrainId grainId, out string? keyExt)
        {
            if (!grainId.TryGetGuidKey(out var result, out keyExt))
            {
                ThrowInvalidGuidKeyFormat(grainId);
            }

            return result;
        }

        /// <summary>
        /// 返回 Grain 主键的 <see cref="Guid"/> 表示形式。
        /// </summary>
        /// <param name="grainId">要获取其主键的 Grain。</param>
        /// <returns>表示此 Grain 主键的 <see cref="Guid"/> 值。</returns>
        public static Guid GetGuidKey(this GrainId grainId)
        {
            if (!grainId.TryGetGuidKey(out var result))
            {
                ThrowInvalidGuidKeyFormat(grainId);
            }

            return result;
        }

        /// <summary>
        /// 抛出异常，指示基于 <see cref="Guid"/> 的 Grain ID 格式不正确。
        /// </summary>
        /// <param name="grainId">
        /// Grain ID。
        /// </param>
        private static void ThrowInvalidGuidKeyFormat(GrainId grainId) => throw new ArgumentException($"Value \"{grainId}\" is not in the correct format for a Guid key.", nameof(grainId));

        /// <summary>
        /// 抛出异常，指示基于 <see cref="long"/> 的 Grain ID 格式不正确。
        /// </summary>
        /// <param name="grainId">
        /// Grain ID。
        /// </param>
        private static void ThrowInvalidIntegerKeyFormat(GrainId grainId) => throw new ArgumentException($"Value \"{grainId}\" is not in the correct format for an Integer key.", nameof(grainId));
    }
}
