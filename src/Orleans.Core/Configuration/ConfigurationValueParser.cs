using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using Microsoft.Extensions.Configuration;

namespace Orleans.Configuration;

/// <summary>
/// 分析配置中不反射的可选输入值 。
/// </summary>
/// <remarks>
/// Orleans.Core 和 Orleans.Runtime 使用这一方法代替 <see cref="ConfigurationBinder.GetValue{T}(IConfiguration, string)"/>,
/// 除非配置绑定源生成器被启用, 需要未引用的代码。 这些组件会自动连接到
/// 无法启用发电机, 因为它们与用户绑定选项类型, 且不支持生成器 。
/// 不存在或空值的数值将调用者的默认默认值留在原位。无效的数值,包括仅使用白空空空间的数值,会丢弃
/// <see cref="InvalidOperationException"/> 样式中的 <see cref="ConfigurationBinder"/>.
/// </remarks>
internal static class ConfigurationValueParser
{
    /// <summary>
    /// 将数值投入 <paramref name="key"/> 使用 <see cref="CultureInfo.InvariantCulture"/>.
    /// </summary>
    /// <returns><see langword="true"/> 当密钥有值时; <see langword="false"/> 空的或空的,</returns>
    /// <exception cref="InvalidOperationException">配置的值不是一个有效的 <typeparamref name="T"/>。</exception>
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
    /// 分析 <see cref="IPEndPoint"/> 值值 <paramref name="key"/>.
    /// </summary>
    /// <returns><see langword="true"/> 当密钥有值时; <see langword="false"/> 空的或空的,</returns>
    /// <exception cref="InvalidOperationException">配置的值不是一个有效的 <see cref="IPEndPoint"/>。</exception>
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
