using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orleans.Runtime
{
    /// <summary>
    /// 确定分组成员组合的版本。
    /// </summary>
    [Serializable, GenerateSerializer, Immutable]
    [JsonConverter(typeof(MembershipVersionConverter))]
    public readonly struct MembershipVersion : IComparable<MembershipVersion>, IEquatable<MembershipVersion>, ISpanFormattable
    {
        /// <summary>
        /// 初始化 <see cref="MembershipVersion"/> 结构的新实例。
        /// </summary>
        /// <param name="version">基本版本。</param>
        public MembershipVersion(long version)
        {
            this.Value = version;
        }

        /// <summary>
        /// 获得版本。
        /// </summary>
        [Id(0)]
        public long Value { get; init; }

        /// <summary>
        /// 获得最小可能的版本。
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
        /// 与所提供的平等目标相比较。
        /// </summary>
        /// <param name="left">左侧操作。</param>
        /// <param name="right">右侧操作。</param>
        /// <returns><see langword="true"/> 如果规定的数值相等,否则 <see langword="false"/>。</returns>
        public static bool operator ==(MembershipVersion left, MembershipVersion right) => left.Value == right.Value;

        /// <summary>
        /// 比较所提供的不平等域。
        /// </summary>
        /// <param name="left">左侧操作。</param>
        /// <param name="right">右侧操作。</param>
        /// <returns><see langword="true"/> 如果所提供的数值不相等,否则 <see langword="false"/>.</returns>
        public static bool operator !=(MembershipVersion left, MembershipVersion right) => left.Value != right.Value;

        /// <summary>
        /// 比较所提供的操作和返回 <see langword="true"/> 倘若左边的手术功过右的或等量,否则 <see langword="false"/>.
        /// </summary>
        /// <param name="left">左侧操作。</param>
        /// <param name="right">右侧操作。</param>
        /// <returns><see langword="true"/> 如果左形操作大于或等于右形操作,否则 <see langword="false"/>。</returns>
        public static bool operator >=(MembershipVersion left, MembershipVersion right) => left.Value >= right.Value;

        /// <summary>
        /// 比较所提供的操作和返回 <see langword="true"/> ,如果左操作小于或等于右操作,否则 <see langword="false"/>。
        /// </summary>
        /// <param name="left">左侧操作。</param>
        /// <param name="right">右侧操作。</param>
        /// <returns><see langword="true"/> 如果左形操作小于或等于右形操作,否则 <see langword="false"/>。</returns>
        public static bool operator <=(MembershipVersion left, MembershipVersion right) => left.Value <= right.Value;

        /// <summary>
        /// 比较所提供的操作和返回 <see langword="true"/> 倘若左边的手术胜过右边的手术,否则 <see langword="false"/>.
        /// </summary>
        /// <param name="left">左侧操作。</param>
        /// <param name="right">右侧操作。</param>
        /// <returns><see langword="true"/> 如果左形大于右形,否则 <see langword="false"/>。</returns>
        public static bool operator >(MembershipVersion left, MembershipVersion right) => left.Value > right.Value;

        /// <summary>
        /// 比较所提供的操作和返回 <see langword="true"/> 如果左边的手术功率低于右边的手术功率,那末, <see langword="false"/>.
        /// </summary>
        /// <param name="left">左侧操作。</param>
        /// <param name="right">右侧操作。</param>
        /// <returns><see langword="true"/> 如果左边的手术功率低于右边的手术功率,那末, <see langword="false"/>.</returns>
        public static bool operator <(MembershipVersion left, MembershipVersion right) => left.Value < right.Value;
    }

    /// <summary>
    /// 将 <see cref="MembershipVersion"/> 例转换为或从 JSON函数。
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
