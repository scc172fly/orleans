using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Providers;

namespace Orleans.Storage;

/// <summary>
/// grain 存储的实用功能。
/// </summary>
public static class GrainStorageHelpers
{
    /// <summary>
    /// 得到 <see cref="IGrainStorage"/> 与指定 grain 类型,必须从 <see cref="Grain{T}"/>.
    /// </summary>
    /// <param name="grainType"> grain 类型,必须来自 <see cref="Grain{T}"/>。</param>
    /// <param name="services">服务供应商。</param>
    /// <returns>
    /// 下 <see cref="IGrainStorage"/> 与指定 grain 类型,必须从 <see cref="Grain{T}"/>.
    /// </returns>
    public static IGrainStorage GetGrainStorage(Type grainType, IServiceProvider services)
    {
        if (grainType is null) throw new ArgumentNullException(nameof(grainType));
        var attrs = grainType.GetCustomAttributes(typeof(StorageProviderAttribute), true);
        var attr = attrs.Length > 0 ? (StorageProviderAttribute)attrs[0] : null;
        var storageProvider = attr != null
            ? services.GetKeyedService<IGrainStorage>(attr.ProviderName)
            : services.GetService<IGrainStorage>();
        if (storageProvider == null)
        {
            ThrowMissingProviderException(grainType, attr?.ProviderName);
        }

        return storageProvider;
    }

    [DoesNotReturn]
    private static void ThrowMissingProviderException(Type grainType, string? name)
    {
        var grainTypeName = grainType.FullName;
        var errMsg = string.IsNullOrEmpty(name)
            ? $"No default storage provider found loading grain type {grainTypeName}."
            : $"No storage provider named \"{name}\" found loading grain type {grainTypeName}.";
        throw new BadProviderConfigException(errMsg);
    }
}
