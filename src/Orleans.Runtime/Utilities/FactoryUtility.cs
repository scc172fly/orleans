using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace Orleans.Runtime.Utilities
{
    /// <summary>
    /// 用于创建工厂的实用方法，工厂使用 <see cref="IServiceProvider"/> 构造对象实例。
    /// </summary>
    internal static class FactoryUtility
    {
        private static readonly object[] EmptyArguments = new object[0];

        /// <summary>
        /// 创建返回新 <typeparamref name="TInstance"/> 的工厂。
        /// </summary>
        /// <typeparam name="TInstance">实例类型。</typeparam>
        /// <param name="serviceProvider">服务提供程序。</param>
        /// <returns>新的工厂。</returns>
        public static Factory<TInstance> Create<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TInstance>(
            IServiceProvider serviceProvider)
        {
            var factory = ActivatorUtilities.CreateFactory(typeof(TInstance), Type.EmptyTypes);
            return () => (TInstance)factory(serviceProvider, EmptyArguments);
        }

        /// <summary>
        /// 创建工厂，返回新的 <typeparamref name="TInstance"/>，并接受类型为 <typeparamref name="TParam1"/> 的参数。
        /// </summary>
        /// <typeparam name="TParam1">工厂参数的类型。</typeparam>
        /// <typeparam name="TInstance">实例类型。</typeparam>
        /// <param name="serviceProvider">服务提供程序。</param>
        /// <returns>新的工厂。</returns>
        public static Factory<TParam1, TInstance> Create<
            TParam1,
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TInstance>(
            IServiceProvider serviceProvider)
        {
            var factory = ActivatorUtilities.CreateFactory(typeof(TInstance), new[] { typeof(TParam1) });
            return arg1 => (TInstance)factory(serviceProvider, new object?[] { arg1 });
        }

        /// <summary>
        /// 创建工厂，根据指定类型的参数返回新的 <typeparamref name="TInstance"/>。
        /// </summary>
        /// <typeparam name="TParam1">工厂第一个参数的类型。</typeparam>
        /// <typeparam name="TParam2">工厂第二个参数的类型。</typeparam>
        /// <typeparam name="TInstance">实例类型。</typeparam>
        /// <param name="serviceProvider">服务提供程序。</param>
        /// <returns>新的工厂。</returns>
        public static Factory<TParam1, TParam2, TInstance> Create<
            TParam1,
            TParam2,
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TInstance>(
            IServiceProvider serviceProvider)
        {
            var factory = ActivatorUtilities.CreateFactory(typeof(TInstance), new[] { typeof(TParam1), typeof(TParam2) });
            return (arg1, arg2) => (TInstance)factory(serviceProvider, new object?[] { arg1, arg2 });
        }
        /// <summary>
        /// 创建工厂，根据指定类型的参数返回新的 <typeparamref name="TInstance"/>。
        /// </summary>
        /// <typeparam name="TParam1">工厂第一个参数的类型。</typeparam>
        /// <typeparam name="TParam2">工厂第二个参数的类型。</typeparam>
        /// <typeparam name="TParam3">工厂第三个参数的类型。</typeparam>
        /// <typeparam name="TInstance">实例类型。</typeparam>
        /// <param name="serviceProvider">服务提供程序。</param>
        /// <returns>新的工厂。</returns>
        public static Factory<TParam1, TParam2, TParam3, TInstance> Create<
            TParam1,
            TParam2,
            TParam3,
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TInstance>(
            IServiceProvider serviceProvider)
        {
            var factory = ActivatorUtilities.CreateFactory(typeof(TInstance), new[] { typeof(TParam1), typeof(TParam2), typeof(TParam3) });
            return (arg1, arg2, arg3) => (TInstance)factory(serviceProvider, new object?[] { arg1, arg2, arg3 });
        }
    }
}
