using Orleans.Serialization.Configuration;
using Orleans.Serialization.Serializers;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Orleans.Serialization
{
    /// <summary>
    /// 分析序列器配置以查找可能的配置问题 。
    /// </summary>
    public static class SerializerConfigurationAnalyzer
    {
        /// <summary>
        /// 分析 grain 接口方法,以找到不可序列的参数类型和返回类型。
        /// </summary>
        /// <param name="codecProvider">
        /// 代码提供者
        /// </param>
        /// <param name="options">
        /// 类型列表选项。
        /// </param>
        /// <returns>
        /// 一系列具有连带性问题的类型。
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="codecProvider"/> 或 <paramref name="options"/> 是 <see langword="null"/>。</exception>
        public static Dictionary<Type, SerializerConfigurationComplaint> AnalyzeSerializerAvailability(ICodecProvider codecProvider, TypeManifestOptions options)
        {
            if (codecProvider is null) throw new ArgumentNullException(nameof(codecProvider));
            if (options is null) throw new ArgumentNullException(nameof(options));
            var allComplaints = new Dictionary<Type, SerializerConfigurationComplaint>();
            foreach (var @interface in options.InterfaceTypes)
            {
                foreach (var method in GetInterfaceMethods(@interface))
                {
                    if (typeof(Task).IsAssignableFrom(method.ReturnType))
                    {
                        if (method.ReturnType.IsConstructedGenericType && typeof(Task<>).IsAssignableFrom(method.ReturnType.GetGenericTypeDefinition()))
                        {
                            VisitType(method.ReturnType.GetGenericArguments()[0], method);
                        }
                    }

                    if (method.ReturnType.IsConstructedGenericType && typeof(ValueTask<>).IsAssignableFrom(method.ReturnType.GetGenericTypeDefinition()))
                    {
                        VisitType(method.ReturnType.GetGenericArguments()[0], method);
                    }

                    foreach (var param in method.GetParameters())
                    {
                        VisitType(param.ParameterType, method);
                    }
                }
            }

            return allComplaints;

            void VisitType(Type type, MethodInfo methodInfo)
            {
                if (!IsEligibleType(type))
                {
                    return;
                }

                var hasCodec = codecProvider.TryGetCodec(type) is not null;
                var hasCopier = codecProvider.TryGetDeepCopier(type) is not null;
                if (!hasCodec || !hasCopier)
                {
                    if (!allComplaints.TryGetValue(type, out var complaint))
                    {
                        complaint = allComplaints[type] = new()
                        {
                            HasSerializer = hasCodec,
                            HasCopier = hasCopier,
                        };
                    }

                    if (!complaint.Methods.TryGetValue(methodInfo.DeclaringType!, out var methodList))
                    {
                        methodList = complaint.Methods[methodInfo.DeclaringType!] = new HashSet<MethodInfo>();
                    }

                    methodList.Add(methodInfo);
                }
            }

            bool IsEligibleType(Type type)
            {
                if (type.IsGenericTypeParameter || type.IsGenericMethodParameter || type.ContainsGenericParameters || type.Equals(typeof(CancellationToken)))
                {
                    return false;
                }

                return true;
            }
        }

#if NET5_0_OR_GREATER
        [UnconditionalSuppressMessage(
            "Trimming",
            "IL2070",
            Justification = "Generated manifests and trim-safe manual configuration register grain interfaces through TypeManifestOptions.AddInterface, which preserves public and non-public methods and inherited interfaces. The HashSet<Type> boundary cannot retain that annotation.")]
#endif
        private static MethodInfo[] GetInterfaceMethods(Type interfaceType)
            => interfaceType.GetMethods(BindingFlags.Instance | BindingFlags.Public);

        /// <summary>
        /// 代表接口方法所用类型序列性的配置问题。
        /// </summary>
        public class SerializerConfigurationComplaint
        {
            /// <summary>
            /// 获取一系列界面类型, 其中引用了该投诉所代表的类型 。
            /// </summary>
            public Dictionary<Type, HashSet<MethodInfo>> Methods { get; } = new();

            /// <summary>
            /// 获取或设置值,以显示是否可使用此类型的序列器。
            /// </summary>
            public bool HasSerializer { get; set; }

            /// <summary>
            /// 获取或设置值,以显示是否为该类型提供复印机。
            /// </summary>
            public bool HasCopier { get; set; }
        }
    }
}
