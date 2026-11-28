using System;
using System.Diagnostics.CodeAnalysis;
using Orleans.Serialization.TypeSystem;

namespace Orleans.BroadcastChannel
{
    /// <summary>
    /// 用于内部支持的流谓词的 <see cref="IChannelNamespacePredicateProvider"/> 默认实现。
    /// </summary>
    public class DefaultChannelNamespacePredicateProvider : IChannelNamespacePredicateProvider
    {
        /// <inheritdoc/>
        public bool TryGetPredicate(string predicatePattern, [NotNullWhen(true)] out IChannelNamespacePredicate? predicate)
        {
            ArgumentNullException.ThrowIfNull(predicatePattern);

            switch (predicatePattern)
            {
                case "*":
                    predicate = new AllStreamNamespacesPredicate();
                    return true;
                case var regex when regex.StartsWith(RegexChannelNamespacePredicate.Prefix, StringComparison.Ordinal):
                    predicate = new RegexChannelNamespacePredicate(regex[RegexChannelNamespacePredicate.Prefix.Length..]);
                    return true;
                case var ns when ns.StartsWith(ExactMatchChannelNamespacePredicate.Prefix, StringComparison.Ordinal):
                    predicate = new ExactMatchChannelNamespacePredicate(ns[ExactMatchChannelNamespacePredicate.Prefix.Length..]);
                    return true;
            }

            predicate = null;
            return false;
        }
    }

    /// <summary>
    /// 流命名空间谓词提供程序，支持可通过构造函数创建且可选择接受字符串构造函数参数的对象。
    /// </summary>
    public class ConstructorChannelNamespacePredicateProvider : IChannelNamespacePredicateProvider
    {
        /// <summary>
        /// 用于标识此谓词提供程序的前缀。
        /// </summary>
        public const string Prefix = "ctor";

        /// <summary>
        /// 将流命名空间谓词格式化为指定要构造的具体 <see cref="IChannelNamespacePredicate"/> 类型及可选参数的表示形式。
        /// </summary>
        public static string FormatPattern(Type predicateType, string? constructorArgument)
        {
            if (constructorArgument is null)
            {
                return $"{Prefix}:{RuntimeTypeNameFormatter.Format(predicateType)}";
            }

            return $"{Prefix}:{RuntimeTypeNameFormatter.Format(predicateType)}:{constructorArgument}";
        }

        /// <inheritdoc/>
        public bool TryGetPredicate(string predicatePattern, [NotNullWhen(true)] out IChannelNamespacePredicate? predicate)
        {
            ArgumentNullException.ThrowIfNull(predicatePattern);

            if (!predicatePattern.StartsWith(Prefix, StringComparison.Ordinal))
            {
                predicate = null;
                return false;
            }

            var start = Prefix.Length + 1;
            string typeName;
            string? arg;
            var index = predicatePattern.IndexOf(':', start);
            if (index < 0)
            {
                typeName = predicatePattern[start..];
                arg = null;
            }
            else
            {
                typeName = predicatePattern[start..index];
                arg = predicatePattern[(index + 1)..];
            }

            var type = Type.GetType(typeName, throwOnError: true);

            if (!typeof(IChannelNamespacePredicate).IsAssignableFrom(type))
            {
                throw new InvalidOperationException($"Type \"{type}\" is not a valid channel namespace predicate because it does not implement {nameof(IChannelNamespacePredicate)}.");
            }

            if (string.IsNullOrEmpty(arg))
            {
                predicate = (IChannelNamespacePredicate)Activator.CreateInstance(type)!;
            }
            else
            {
                predicate = (IChannelNamespacePredicate)Activator.CreateInstance(type, arg)!;
            }

            return true;
        }
    }
}
