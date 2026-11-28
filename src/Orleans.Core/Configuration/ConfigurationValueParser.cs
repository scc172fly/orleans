using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using Microsoft.Extensions.Configuration;

namespace Orleans.Configuration;

/// <summary>
/// 无需反射即可从配置中解析可选的强类型值。
/// </summary>
/// <remarks>
/// Orleans.Core 和 Orleans.Runtime 使用此方法代替 <see cref="ConfigurationBinder.GetValue{T}(IConfiguration, string)"/>，
/// 后者要求使用未被引用的代码，除非启用了配置绑定源生成器。这两个程序集
/// 无法启用生成器，因为它们绑定的选项类型包含生成器不支持的成员。
/// 缺失或为空的值会保留调用方的默认值。无效值（包括仅含空白字符的值）会引发
/// <see cref="InvalidOperationException"/>（其风格与 <see cref="ConfigurationBinder"/> 一致）。
/// </remarks>
internal static class ConfigurationValueParser
{
    /// <summary>
    /// 解析 <paramref name="key"/> 对应的值时使用 <see cref="CultureInfo.InvariantCulture"/>。
    /// </summary>
    /// <returns><see langword="true"/> 表示键存在非空值；键缺失或为空时返回 <see langword="false"/>。</returns>
    /// <exception cref="InvalidOperationException">配置值不是有效的 <typeparamref name="T"/>。</exception>
    public static bool TryParseValue<T>(IConfiguration configuration, string key, [MaybeNullWhen(false)] out T value) where T : IParsable<T>
    {
        var text = configuration[key];
        if (string.IsNullOrEmpty(text))
        {
            value = default;
            return false;
        }

        if (T.TryParse(text, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        throw CreateInvalidValueException(configuration, key, typeof(T));
    }

    /// <summary>
    /// 解析 <see cref="IPEndPoint"/> 类型的值，配置键为 <paramref name="key"/>。
    /// </summary>
    /// <returns><see langword="true"/> 表示键存在非空值；键缺失或为空时返回 <see langword="false"/>。</returns>
    /// <exception cref="InvalidOperationException">配置值不是有效的 <see cref="IPEndPoint"/>。</exception>
    public static bool TryParseIPEndPoint(IConfiguration configuration, string key, [NotNullWhen(true)] out IPEndPoint? value)
    {
        var text = configuration[key];
        if (string.IsNullOrEmpty(text))
        {
            value = null;
            return false;
        }

        if (IPEndPoint.TryParse(text, out value))
        {
            return true;
        }

        throw CreateInvalidValueException(configuration, key, typeof(IPEndPoint));
    }

    private static InvalidOperationException CreateInvalidValueException(IConfiguration configuration, string key, Type type)
    {
        var path = configuration is IConfigurationSection section ? ConfigurationPath.Combine(section.Path, key) : key;
        return new InvalidOperationException($"Failed to convert configuration value at '{path}' to type '{type}'.");
    }
}
