using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orleans.Runtime
{
    /// <summary>
    /// 标识集群成员资格配置的版本。
    /// </summary>
    [Serializable, GenerateSerializer, Immutable]
    [JsonConverter(typeof(MembershipVersionConverter))]
    public readonly struct MembershipVersion : IComparable<MembershipVersion>, IEquatable<MembershipVersion>, ISpanFormattable
    {
        /// <summary>
        /// 初始化 <see cref="MembershipVersion"/> 结构的新实例。
        /// </summary>
        /// <param name="version">底层版本。</param>
        public MembershipVersion(long version)
        {
            this.Value = version;
        }

        /// <summary>
        /// 获取版本号。
        /// </summary>
        [Id(0)]
        public long Value { get; init; }

        /// <summary>
        /// 获取可能的最小版本。
        /// </summary>
        public static MembershipVersion MinValue => new MembershipVersion(long.MinValue);

        /// <inheritdoc/>
        public int CompareTo(MembershipVersion other) => this.Value.CompareTo(other.Value);

        /// <inheritdoc/>
        public bool Equals(MembershipVersion other) => this.Value == other.Value;

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is MembershipVersion other && this.Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => this.Value.GetHashCode();

        /// <inheritdoc/>
        public override string ToString() => Value != MinValue.Value ? Value.ToString() : "default";

        /// <inheritdoc/>
        string IFormattable.ToString(string? format, IFormatProvider? formatProvider)
            => Value != MinValue.Value ? Value.ToString(format, formatProvider) : "default";

        /// <inheritdoc/>
        bool ISpanFormattable.TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
        {
            if (Value != MinValue.Value)
            {
                return Value.TryFormat(destination, out charsWritten, format, provider);
            }

            if ("default".AsSpan().TryCopyTo(destination))
            {
                charsWritten = "default".Length;
                return true;
            }

            charsWritten = 0;
            return false;
        }

        /// <summary>
        /// 比较提供的操作数是否相等。
        /// </summary>
        /// <param name="left">左操作数。</param>
        /// <param name="right">右操作数。</param>
        /// <returns>如果提供的值相等，则返回 <see langword="true"/>，否则返回 <see langword="false"/>。</returns>
        public static bool operator ==(MembershipVersion left, MembershipVersion right) => left.Value == right.Value;

        /// <summary>
        /// 比较提供的操作数是否不相等。
        /// </summary>
        /// <param name="left">左操作数。</param>
        /// <param name="right">右操作数。</param>
        /// <returns>如果提供的值不相等，则返回 <see langword="true"/>，否则返回 <see langword="false"/>。</returns>
        public static bool operator !=(MembershipVersion left, MembershipVersion right) => left.Value != right.Value;

        /// <summary>
        /// 比较提供的操作数，左操作数大于或等于右操作数时返回 <see langword="true"/>，否则返回 <see langword="false"/>。
        /// </summary>
        /// <param name="left">左操作数。</param>
        /// <param name="right">右操作数。</param>
        /// <returns>如果左操作数大于或等于右操作数，则返回 <see langword="true"/>，否则返回 <see langword="false"/>。</returns>
        public static bool operator >=(MembershipVersion left, MembershipVersion right) => left.Value >= right.Value;

        /// <summary>
        /// 比较提供的操作数，左操作数小于或等于右操作数时返回 <see langword="true"/>，否则返回 <see langword="false"/>。
        /// </summary>
        /// <param name="left">左操作数。</param>
        /// <param name="right">右操作数。</param>
        /// <returns>如果左操作数小于或等于右操作数，则返回 <see langword="true"/>，否则返回 <see langword="false"/>。</returns>
        public static bool operator <=(MembershipVersion left, MembershipVersion right) => left.Value <= right.Value;

        /// <summary>
        /// 比较提供的操作数，左操作数大于右操作数时返回 <see langword="true"/>，否则返回 <see langword="false"/>。
        /// </summary>
        /// <param name="left">左操作数。</param>
        /// <param name="right">右操作数。</param>
        /// <returns>如果左操作数大于右操作数，则返回 <see langword="true"/>，否则返回 <see langword="false"/>。</returns>
        public static bool operator >(MembershipVersion left, MembershipVersion right) => left.Value > right.Value;

        /// <summary>
        /// 比较提供的操作数，左操作数小于右操作数时返回 <see langword="true"/>，否则返回 <see langword="false"/>。
        /// </summary>
        /// <param name="left">左操作数。</param>
        /// <param name="right">右操作数。</param>
        /// <returns>如果左操作数小于右操作数，则返回 <see langword="true"/>，否则返回 <see langword="false"/>。</returns>
        public static bool operator <(MembershipVersion left, MembershipVersion right) => left.Value < right.Value;
    }

    /// <summary>
    /// 提供 <see cref="MembershipVersion"/> 实例与 JSON 相互转换的功能。
    /// </summary>
    public sealed class MembershipVersionConverter : JsonConverter<MembershipVersion>
    {
        const int MaxStackallocLength = 64;
        /// <inheritdoc />
        public override MembershipVersion Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.Number)
            {
                throw new JsonException($"Could not deserialize {nameof(MembershipVersion)}.");
            }

            return new(reader.GetInt64());
        }

        /// <inheritdoc />
        public override void Write(Utf8JsonWriter writer, MembershipVersion value, JsonSerializerOptions options) => writer.WriteNumberValue(value.Value);

        /// <inheritdoc />
        public override MembershipVersion ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.ValueIsEscaped)
            {
                return new MembershipVersion(long.Parse(reader.GetString()!, CultureInfo.InvariantCulture));
            }

            if (reader.HasValueSequence)
            {
                var valueLength = checked((int)reader.ValueSequence.Length);
                if (valueLength < MaxStackallocLength)
                {
                    Span<byte> number = stackalloc byte[valueLength];
                    reader.ValueSequence.CopyTo(number);
                    return new MembershipVersion(long.Parse(number, CultureInfo.InvariantCulture));
                }
                else
                {
                    return new MembershipVersion(long.Parse(reader.GetString()!, CultureInfo.InvariantCulture));
                }
            }
            else
            {
                return new MembershipVersion(long.Parse(reader.ValueSpan, CultureInfo.InvariantCulture));
            }

        }
        /// <inheritdoc />
        public override void WriteAsPropertyName(Utf8JsonWriter writer, [DisallowNull] MembershipVersion value, JsonSerializerOptions options)
        {
            Span<byte> number = stackalloc byte[MaxStackallocLength];
            if (value.Value.TryFormat(number, out var written, provider: CultureInfo.InvariantCulture))
            {
                writer.WritePropertyName(number[..written]);
            }
            else
            {
                writer.WritePropertyName(value.Value.ToString(CultureInfo.InvariantCulture));
            }
        }
    }
}
