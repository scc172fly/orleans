using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Durable.Messaging;
using Orleans.Serialization;

namespace Orleans.Hosting;

/// <summary>用于注册强类型持久化消息主题的扩展方法。</summary>
public static class MessageTypeExtensions
{
    /// <summary>注册单例主题绑定，以按序数比较的主题为键。</summary>
    /// <typeparam name="T">主题的有效载荷契约。</typeparam>
    /// <param name="services">服务集合。</param>
    /// <param name="subject">精确的应用协议主题。</param>
    /// <returns>服务集合。</returns>
    /// <exception cref="InvalidOperationException">已注册该主题的绑定。</exception>
    public static IServiceCollection AddMessageType<T>(this IServiceCollection services, string subject)
    {
        ArgumentNullException.ThrowIfNull(services);
        EnvelopeValidation.ValidateSubject(subject);
        foreach (var descriptor in services)
        {
            if (descriptor.IsKeyedService
                && descriptor.ServiceType.IsGenericType
                && descriptor.ServiceType.GetGenericTypeDefinition() == typeof(MessageType<>)
                && Equals(descriptor.ServiceKey, subject))
            {
                throw new InvalidOperationException($"A payload contract is already registered for durable message subject '{subject}'.");
            }
        }

        services.TryAddSingleton<Serializer<T>>(static provider => provider.GetRequiredService<Serializer>().GetSerializer<T>());
        services.AddKeyedSingleton<MessageType<T>>(subject,
            (provider, _) => new MessageType<T>(subject, provider.GetRequiredService<Serializer<T>>()));
        return services;
    }
}
