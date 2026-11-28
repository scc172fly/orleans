using System;
using System.Diagnostics.CodeAnalysis;
using Orleans.Serialization.TypeSystem;

namespace Orleans.Streams
{
    /// <summary>
    /// 用于内部支持的流谓词的 <see cref="IStreamNamespacePredicateProvider"/> 默认实现。
    /// </summary>
    public class DefaultStreamNamespacePredicateProvider : IStreamNamespacePredicateProvider
    {
        /// <inheritdoc/>
        public bool TryGetPredicate(string predicatePattern, [MaybeNullWhen(false)] out IStreamNamespacePredicate predicate)
        {
            ArgumentNullException.ThrowIfNull(predicatePattern);

            switch (predicatePattern)
            {
                case "*":
                    predicate = new AllStreamNamespacesPredicate();
                    return true;
                case var regex when regex.StartsWith(RegexStreamNamespacePredicate.Prefix, StringComparison.Ordinal):
                    predicate = new RegexStreamNamespacePredicate(regex[RegexStreamNamespacePredicate.Prefix.Length..]);
                    return true;
                case var ns when ns.StartsWith(ExactMatchStreamNamespacePredicate.Prefix, StringComparison.Ordinal):
                    predicate = new ExactMatchStreamNamespacePredicate(ns[ExactMatchStreamNamespacePredicate.Prefix.Length..]);
                    return true;
            }

            predicate = null;
            return false;
        }
    }

    /// <summary>
    /// 流命名空间谓词提供程序，支持可构造且可以选择接受字符串作为构造函数参数的对象。
    /// </summary>
    public class ConstructorStreamNamespacePredicateProvider : IStreamNamespacePredicateProvider
    {
        /// <summary>
        /// 用于标识此谓词提供程序的前缀。
        /// </summary>
        public const string Prefix = "ctor";

        /// <summary>
        /// 格式化流命名空间谓词，指定要构造的具体 <see cref="IStreamNamespacePredicate"/> 类型及可选参数。
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
        public bool TryGetPredicate(string predicatePattern, [MaybeNullWhen(false)] out IStreamNamespacePredicate predicate)
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

            var type = Type.GetType(typeName, throwOnError: true)!; // throwOnError 确保返回 Type 或抛出异常。

            if (!typeof(IStreamNamespacePredicate).IsAssignableFrom(type))
            {
                throw new InvalidOperationException($"Type \"{type}\" is not a valid stream namespace predicate because it does not implement {nameof(IStreamNamespacePredicate)}.");
            }

            if (string.IsNullOrEmpty(arg))
            {
                predicate = (IStreamNamespacePredicate)Activator.CreateInstance(type)!; // CreateInstance 返回谓词实例或抛出异常。
            }
            else
            {
                predicate = (IStreamNamespacePredicate)Activator.CreateInstance(type, arg)!; // CreateInstance 返回谓词实例或抛出异常。
            }

            return true;
        }
    }
}
