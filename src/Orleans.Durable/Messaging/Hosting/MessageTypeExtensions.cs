using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Durable.Messaging;
using Orleans.Serialization;

namespace Orleans.Hosting;

/// <summary>用于登记输入的耐久信息主题的扩展。</summary>
public static class MessageTypeExtensions
{
    /// <summary>登记单吨的受约束标,以其正态标为键。</summary>
    /// <typeparam name="T">目标的载荷合同</typeparam>
    /// <param name="services">服务收藏。</param>
    /// <param name="subject">确切的应用程序协议主题。</param>
    /// <returns>服务收藏。</returns>
    /// <exception cref="InvalidOperationException">已经登记了约束标的。</exception>
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
