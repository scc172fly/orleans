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
    /// 维护 Grain 类与相应的接口到实现映射之间的映射。
    /// </summary>
    internal class InterfaceToImplementationMappingCache
    {
        /// <summary>
        /// 将 Grain 接口方法的 <see cref="MethodInfo"/> 映射到实现方法的 <see cref="MethodInfo"/>。
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
            /// 获取 Grain 实现方法的 <see cref="MethodInfo"/>。
            /// </summary>
            public MethodInfo ImplementationMethod { get; }

            /// <summary>
            /// 获取 Grain 接口方法的 <see cref="MethodInfo"/>。
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
            /// 获取此方法的已构造泛型实例。
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
        /// 从实现类型和接口类型到方法映射的映射。
        /// </summary>
        private readonly ConcurrentDictionary<(Type ImplementationType, Type InterfaceType), Dictionary<MethodInfo, Entry>> mappings = new();

        /// <summary>
        /// 返回每个接口方法到其接口方法及实现方法元数据的映射，
        /// 适用于提供的实现类型与接口类型。
        /// </summary>
        /// <param name="implementationType">实现类型。</param>
        /// <param name="interfaceType">接口类型。</param>
        /// <returns>
        /// 从接口方法到接口方法及实现方法元数据的映射。
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
        /// 将 <paramref name="interfaceType"/> 中的每个方法（包括继承的接口方法）
        /// 映射到其在 <paramref name="implementationType"/> 上的实现。
        /// </summary>
        /// <param name="implementationType">实现类型。</param>
        /// <param name="interfaceType">接口类型。</param>
        /// <returns>从接口方法到实现方法的映射。</returns>
        private static Dictionary<MethodInfo, Entry> CreateInterfaceToImplementationMap(Type implementationType, Type interfaceType)
        {
            var methods = GrainInterfaceUtils.GetMethods(interfaceType);

            // 将此接口中定义的每个方法映射到实现类上的对应方法。
            var result = new Dictionary<MethodInfo, Entry>(methods.Length);

            var mapping = default(InterfaceMapping);
            for (var i = 0; i < methods.Length; i++)
            {
                var method = methods[i];

                // 如果此方法不是来自预期的接口（例如来自父接口），则
                // 获取其实际所属接口的映射。
                if (mapping.InterfaceType != method.DeclaringType)
                {
                    mapping = GrainInterfaceUtils.GetInterfaceMap(implementationType, method.DeclaringType!);
                }

                // 找到接口方法的索引，再获取相同位置的实现方法。
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
