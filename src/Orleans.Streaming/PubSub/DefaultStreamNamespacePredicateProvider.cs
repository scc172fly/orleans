using System;
using System.Diagnostics.CodeAnalysis;
using Orleans.Serialization.TypeSystem;

namespace Orleans.Streams
{
    /// <summary>
    /// 默认实施内部支持的流流上游 <see cref="IStreamNamespacePredicateProvider"/> 。
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
    /// 支持可构造对象的流命名空间上游提供者,可选择接受字符串作为构建参数。
    /// </summary>
    public class ConstructorStreamNamespacePredicateProvider : IStreamNamespacePredicateProvider
    {
        /// <summary>
        /// 用于识别该上游供应商的前缀。
        /// </summary>
        public const string Prefix = "ctor";

        /// <summary>
        /// 格式化流命名空间前置,该前置表示要构造的混凝土 <see cref="IStreamNamespacePredicate"/> 类型,并加上一个可选参数。
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

            var type = Type.GetType(typeName, throwOnError: true)!; // throwOnError 确保返回类型或提出例外。

            if (!typeof(IStreamNamespacePredicate).IsAssignableFrom(type))
            {
                throw new InvalidOperationException($"Type \"{type}\" is not a valid stream namespace predicate because it does not implement {nameof(IStreamNamespacePredicate)}.");
            }

            if (string.IsNullOrEmpty(arg))
            {
                predicate = (IStreamNamespacePredicate)Activator.CreateInstance(type)!; // CreateInstance 返回一个或几个的上游实例或投出。
            }
            else
            {
                predicate = (IStreamNamespacePredicate)Activator.CreateInstance(type, arg)!; // CreateInstance 返回一个或几个的上游实例或投出。
            }

            return true;
        }
    }
}
