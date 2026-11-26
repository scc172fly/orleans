using System;
using System.Buffers.Text;
using System.Diagnostics;
using System.Text;

namespace Orleans.Runtime
{
    /// <summary>
    /// <see cref="GrainId"/> 密钥的扩展名。
    /// </summary>
    public static class GrainIdKeyExtensions
    {
        /// <summary>
        /// 创建 <see cref="IdSpan"/> ,代表 <see cref="long"/> 密钥。
        /// </summary>
        /// <param name="key">
        /// 钥匙
        /// </param>
        /// <returns>
        /// 代表所提供密钥的 <see cref="IdSpan"/> 。
        /// </returns>
        public static IdSpan CreateIntegerKey(long key)
        {
            Span<byte> buf = stackalloc byte[sizeof(long) * 2];
            Utf8Formatter.TryFormat(key, buf, out var len, 'X');
            Debug.Assert(len > 0, "Unable to format the provided value as a UTF8 string");
            return new IdSpan(buf[..len].ToArray());
        }

        /// <summary>
        /// 创建 <see cref="IdSpan"/> ,代表 <see cref="long"/> 键和密钥扩展字符串。
        /// </summary>
        /// <param name="key">
        /// 钥匙
        /// </param>
        /// <param name="keyExtension">
        /// UTF-8 编码密钥扩展名 。
        /// </param>
        /// <returns>
        /// 代表所提供的关键和关键扩展名的 <see cref="IdSpan"/> 。
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
        /// 创建 <see cref="IdSpan"/> ,代表 <see cref="long"/> 键和密钥扩展字符串。
        /// </summary>
        /// <param name="key">
        /// 钥匙
        /// </param>
        /// <param name="keyExtension">
        /// 关键扩展名 。
        /// </param>
        /// <returns>
        /// 代表所提供的关键和关键扩展名的 <see cref="IdSpan"/> 。
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
        /// 创建 <see cref="IdSpan"/> ,代表 <see cref="Guid"/> 密钥。
        /// </summary>
        /// <param name="key">
        /// 钥匙
        /// </param>
        /// <returns>
        /// 代表所提供密钥的 <see cref="IdSpan"/> 。
        /// </returns>
        public static IdSpan CreateGuidKey(Guid key)
        {
            var buf = new byte[32];
            Utf8Formatter.TryFormat(key, buf, out var len, 'N');
            Debug.Assert(len == 32, "Unable to format the provided value as a UTF8 string");
            return new IdSpan(buf);
        }

        /// <summary>
        /// 创建 <see cref="IdSpan"/> ,代表 <see cref="Guid"/> 键和密钥扩展字符串。
        /// </summary>
        /// <param name="key">
        /// 钥匙
        /// </param>
        /// <param name="keyExtension">
        /// UTF-8 编码密钥扩展名 。
        /// </param>
        /// <returns>
        /// 代表所提供的关键和关键扩展名的 <see cref="IdSpan"/> 。
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
        /// 创建 <see cref="IdSpan"/> ,代表 <see cref="Guid"/> 键和密钥扩展字符串。
        /// </summary>
        /// <param name="key">
        /// 钥匙
        /// </param>
        /// <param name="keyExtension">
        /// 关键扩展名 。
        /// </param>
        /// <returns>
        /// 代表所提供的关键和关键扩展名的 <see cref="IdSpan"/> 。
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
        /// 尝试解析 <see cref="GrainId.Key"/> 提供部分 grain 要提取 a 的id , <see cref="long"/> 键和 <see cref="string"/> 键扩展名。
        /// </summary>
        /// <param name="grainId">
        /// 下 grain id. 数据。
        /// </param>
        /// <param name="key">
        /// 钥匙
        /// </param>
        /// <param name="keyExt">
        /// 关键扩展名 。
        /// </param>
        /// <returns>
        /// <see langword="true"/> 时 grain 成功解析了id, <see langword="false" /> 否则。
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
        /// 尝试解析 <see cref="GrainId.Key"/> 提供部分 grain 要提取 a 的id , <see cref="long"/> 键。
        /// </summary>
        /// <param name="grainId">
        /// 下 grain id. 数据。
        /// </param>
        /// <param name="key">
        /// 钥匙
        /// </param>
        /// <returns>
        /// <see langword="true"/> 时 grain 成功解析了id, <see langword="false" /> 否则。
        /// </returns>
        internal static bool TryGetIntegerKey(this GrainId grainId, out long key)
        {
            var keyString = grainId.Key.AsSpan();
            if (keyString.IndexOf((byte)'+') is int index && index >= 0)
                keyString = keyString[..index];

            return Utf8Parser.TryParse(keyString, out key, out var len, 'X') && len == keyString.Length;
        }

        /// <summary>
        /// 返回 <see cref="long"/> 表示的 grain 键。
        /// </summary>
        /// <param name="grainId">下 grain id. 数据。</param>
        /// <param name="keyExt">返回扩展键部分的输出参数 grain 初级关键,如果为此提供了扩展主关键 grain.</param>
        /// <returns>此 grain的密钥的长代表。</returns>
        public static long GetIntegerKey(this GrainId grainId, out string? keyExt)
        {
            if (!grainId.TryGetIntegerKey(out var result, out keyExt))
            {
                ThrowInvalidIntegerKeyFormat(grainId);
            }

            return result;
        }

        /// <summary>
        /// 返回 <see cref="long"/> 表示的 grain 键。
        /// </summary>
        /// <param name="grainId">查找密钥的 grain 。</param>
        /// <returns>A <see cref="long"/> 代表此 grain的键。</returns>
        public static long GetIntegerKey(this GrainId grainId)
        {
            if (!grainId.TryGetIntegerKey(out var result))
            {
                ThrowInvalidIntegerKeyFormat(grainId);
            }

            return result;
        }

        /// <summary>
        /// 尝试解析 <see cref="GrainId.Key"/> 提供部分 grain 要提取 a 的id , <see cref="Guid"/> 键和 <see cref="string"/> 键扩展名。
        /// </summary>
        /// <param name="grainId">
        /// 下 grain id. 数据。
        /// </param>
        /// <param name="key">
        /// 钥匙
        /// </param>
        /// <param name="keyExt">
        /// 关键扩展名 。
        /// </param>
        /// <returns>
        /// <see langword="true"/> 时 grain 成功解析了id, <see langword="false" /> 否则。
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
        /// 尝试解析 <see cref="GrainId.Key"/> 提供部分 grain 要提取 a 的id , <see cref="Guid"/> 键。
        /// </summary>
        /// <param name="grainId">
        /// 下 grain id. 数据。
        /// </param>
        /// <param name="key">
        /// 钥匙
        /// </param>
        /// <returns>
        /// <see langword="true"/> 时 grain 成功解析了id, <see langword="false" /> 否则。
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
        /// 返回 <see cref="Guid"/> 代表代表 grain 基本键。
        /// </summary>
        /// <param name="grainId">查找主密钥的 grain 。</param>
        /// <param name="keyExt">返回扩展键部分的输出参数 grain 初级关键,如果为此提供了扩展主关键 grain.</param>
        /// <returns>A <see cref="Guid"/> 代表此 grain的主要关键字。</returns>
        public static Guid GetGuidKey(this GrainId grainId, out string? keyExt)
        {
            if (!grainId.TryGetGuidKey(out var result, out keyExt))
            {
                ThrowInvalidGuidKeyFormat(grainId);
            }

            return result;
        }

        /// <summary>
        /// 返回 <see cref="Guid"/> 代表代表 grain 基本键。
        /// </summary>
        /// <param name="grainId">查找主密钥的 grain 。</param>
        /// <returns>A <see cref="Guid"/> 代表此 grain的主要关键字。</returns>
        public static Guid GetGuidKey(this GrainId grainId)
        {
            if (!grainId.TryGetGuidKey(out var result))
            {
                ThrowInvalidGuidKeyFormat(grainId);
            }

            return result;
        }

        /// <summary>
        /// 丢弃一个例外, 表明基于 <see cref="Guid"/>- 基 grain id的格式错误。
        /// </summary>
        /// <param name="grainId">
        /// 下 grain id. 数据。
        /// </param>
        private static void ThrowInvalidGuidKeyFormat(GrainId grainId) => throw new ArgumentException($"Value \"{grainId}\" is not in the correct format for a Guid key.", nameof(grainId));

        /// <summary>
        /// 丢弃一个例外, 表明基于 <see cref="long"/>- 基 grain id的格式错误。
        /// </summary>
        /// <param name="grainId">
        /// 下 grain id. 数据。
        /// </param>
        private static void ThrowInvalidIntegerKeyFormat(GrainId grainId) => throw new ArgumentException($"Value \"{grainId}\" is not in the correct format for an Integer key.", nameof(grainId));
    }
}
