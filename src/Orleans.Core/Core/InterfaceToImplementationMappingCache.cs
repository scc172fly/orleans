using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using Orleans.CodeGeneration;

namespace Orleans
{
    /// <summary>
    /// 维护 grain 类和相应的界面-实施图绘制之间的地图。
    /// </summary>
    internal class InterfaceToImplementationMappingCache
    {
        /// <summary>
        /// 将 grain 接口方法的 <see cref="MethodInfo"/> 映射为执行 <see cref="MethodInfo"/>。
        /// </summary>
        public readonly struct Entry
        {
            public Entry(MethodInfo implementationMethod, MethodInfo interfaceMethod)
            {
                Debug.Assert(implementationMethod is not null);
                Debug.Assert(interfaceMethod is not null);
                ImplementationMethod = implementationMethod;
                InterfaceMethod = interfaceMethod;
            }

            /// <summary>
            /// 获取 grain 执行 <see cref="MethodInfo"/>。
            /// </summary>
            public MethodInfo ImplementationMethod { get; }

            /// <summary>
            /// 获取 grain 接口 <see cref="MethodInfo"/>。
            /// </summary>
            public MethodInfo InterfaceMethod { get; }

            public (MethodInfo ImplementationMethod, MethodInfo InterfaceMethod) GetConstructedGenericMethod(MethodInfo method)
            {
                return ConstructedGenericMethods.GetOrAdd(method.GetGenericArguments(), (key, state) =>
                {
                    var (entry, method) = state;
                    var genericArgs = key;
                    var constructedImplementationMethod = entry.ImplementationMethod.MakeGenericMethod(genericArgs);
                    var constructedInterfaceMethod = entry.InterfaceMethod.MakeGenericMethod(genericArgs);
                    return (constructedImplementationMethod, constructedInterfaceMethod);
                }, (this, method));
            }

            /// <summary>
            /// 获取该方法的构建通用实例。
            /// </summary>
            public ConcurrentDictionary<Type[], (MethodInfo ImplementationMethod, MethodInfo InterfaceMethod)> ConstructedGenericMethods { get; } = new(TypeArrayComparer.Instance);

            private sealed class TypeArrayComparer : IEqualityComparer<Type[]>
            {
                internal static readonly TypeArrayComparer Instance = new();

                public bool Equals(Type[]? x, Type[]? y) =>
                    ReferenceEquals(x, y) || x is not null && y is not null && x.AsSpan().SequenceEqual(y);

                public int GetHashCode([DisallowNull] Type[] obj)
                {
                    HashCode result = new();
                    result.Add(obj.Length);
                    foreach (var value in obj)
                    {
                        result.Add(value);
                    }

                    return result.ToHashCode();
                }
            }
        }

        /// <summary>
        /// 从实施和接口类型到方法绘图的地图。
        /// </summary>
        private readonly ConcurrentDictionary<(Type ImplementationType, Type InterfaceType), Dictionary<MethodInfo, Entry>> mappings = new();

        /// <summary>
        /// 返回从每个接口方法到其界面和实施方法元数据的映射
        /// 用于提供实施和接口类型的数据。
        /// </summary>
        /// <param name="implementationType">执行类型。</param>
        /// <param name="interfaceType">接口类型。</param>
        /// <returns>
        /// 从接口方法到接口和实施方法元数据绘图。
        /// </returns>
        public Dictionary<MethodInfo, Entry> GetOrCreate(Type implementationType, Type interfaceType)
        {
            if (!interfaceType.IsInterface)
            {
                throw new ArgumentException($"Type {interfaceType} is not an interface", nameof(interfaceType));
            }

            if (!interfaceType.IsAssignableFrom(implementationType))
            {
                throw new InvalidOperationException($"Type {implementationType} does not implement interface {interfaceType}");
            }

            return mappings.GetOrAdd(
                (implementationType, interfaceType),
                static key => CreateInterfaceToImplementationMap(key.ImplementationType, key.InterfaceType));
        }

        /// <summary>
        /// 绘制 <paramref name="interfaceType"/>的每种方法图,包括遗留的接口方法,
        /// <paramref name="implementationType"/>履行时的[问题]。
        /// </summary>
        /// <param name="implementationType">执行类型。</param>
        /// <param name="interfaceType">接口类型。</param>
        /// <returns>从接口方法到执行方法的绘图。</returns>
        private static Dictionary<MethodInfo, Entry> CreateInterfaceToImplementationMap(Type implementationType, Type interfaceType)
        {
            var methods = GrainInterfaceUtils.GetMethods(interfaceType);

            // 绘制此界面上从定义接口到执行类的每一种方法。
            var result = new Dictionary<MethodInfo, Entry>(methods.Length);

            var mapping = default(InterfaceMapping);
            for (var i = 0; i < methods.Length; i++)
            {
                var method = methods[i];

                // 如果此方法不是来自预期的界面( 例如, 因为是父界面) , 那么
                // 获取它所属界面的映射。
                if (mapping.InterfaceType != method.DeclaringType)
                {
                    mapping = GrainInterfaceUtils.GetInterfaceMap(implementationType, method.DeclaringType!);
                }

                // 查找接口方法的索引,然后将执行方法置于该位置。
                for (var k = 0; k < mapping.InterfaceMethods!.Length; k++)
                {
                    if (mapping.InterfaceMethods[k] != method) continue;
                    Debug.Assert(method is not null);
                    result[method] = new Entry(mapping.TargetMethods![k], method);

                    break;
                }
            }

            return result;
        }
    }
}
