using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using System.Security;

namespace Orleans.Serialization
{
    /// <summary>
    /// 创建带有序列属性的呼叫方法代表。
    /// </summary>
    internal sealed class SerializationCallbacksFactory
    {
#if NET5_0_OR_GREATER
        private const DynamicallyAccessedMemberTypes CallbackMethods =
            DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.NonPublicMethods;
#endif

        private readonly ConcurrentDictionary<Type, object> _cache = new();

        /// <summary>
        /// 获取引用类型的序列回调。
        /// </summary>
        /// <param name="type">类型。</param>
        /// <returns>序列化回调。</returns>
        [SecurityCritical]
        public SerializationCallbacks<Action<object, StreamingContext>> GetReferenceTypeCallbacks(
#if NET5_0_OR_GREATER
            [DynamicallyAccessedMembers(CallbackMethods)]
#endif
            Type type)
        {
            if (_cache.TryGetValue(type, out var existing))
            {
                return (SerializationCallbacks<Action<object, StreamingContext>>)existing;
            }

            var created = CreateTypedCallbacks<Action<object, StreamingContext>>(type, typeof(object));
            return (SerializationCallbacks<Action<object, StreamingContext>>)_cache.GetOrAdd(type, created);
        }

        /// <summary>
        /// 获取序列化回调值类型。
        /// </summary>
        /// <typeparam name="TOwner">声明类型。</typeparam>
        /// <typeparam name="TDelegate">代表类型。</typeparam>
        /// <param name="type">类型。</param>
        /// <returns>序列化回调。</returns>
        [SecurityCritical]
        public SerializationCallbacks<TDelegate> GetValueTypeCallbacks<TOwner, TDelegate>(
#if NET5_0_OR_GREATER
            [DynamicallyAccessedMembers(CallbackMethods)]
#endif
            Type type)
            where TOwner : struct
            where TDelegate : Delegate
            => GetValueTypeCallbacks<TDelegate>(type, typeof(TOwner));

        private SerializationCallbacks<TDelegate> GetValueTypeCallbacks<TDelegate>(
#if NET5_0_OR_GREATER
            [DynamicallyAccessedMembers(CallbackMethods)]
#endif
            Type type,
            Type owner)
            where TDelegate : Delegate
        {
            if (_cache.TryGetValue(type, out var existing))
            {
                return (SerializationCallbacks<TDelegate>)existing;
            }

            var created = CreateTypedCallbacks<TDelegate>(type, owner);
            return (SerializationCallbacks<TDelegate>)_cache.GetOrAdd(type, created);
        }

        [SecurityCritical]
        private static SerializationCallbacks<TDelegate> CreateTypedCallbacks<TDelegate>(
#if NET5_0_OR_GREATER
            [DynamicallyAccessedMembers(CallbackMethods)]
#endif
            Type type,
            Type owner)
            where TDelegate : Delegate
        {
            var onDeserializing = default(TDelegate);
            var onDeserialized = default(TDelegate);
            var onSerializing = default(TDelegate);
            var onSerialized = default(TDelegate);
            foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                var parameterInfos = method.GetParameters();
                if (parameterInfos.Length != 1)
                {
                    continue;
                }

                if (parameterInfos[0].ParameterType != typeof(StreamingContext))
                {
                    continue;
                }

                if (method.IsDefined(typeof(OnDeserializingAttribute), false))
                {
                    onDeserializing = (TDelegate)GetSerializationMethod(type, method, owner).CreateDelegate(typeof(TDelegate));
                }

                if (method.IsDefined(typeof(OnDeserializedAttribute), false))
                {
                    onDeserialized = (TDelegate)GetSerializationMethod(type, method, owner).CreateDelegate(typeof(TDelegate));
                }

                if (method.IsDefined(typeof(OnSerializingAttribute), false))
                {
                    onSerializing = (TDelegate)GetSerializationMethod(type, method, owner).CreateDelegate(typeof(TDelegate));
                }

                if (method.IsDefined(typeof(OnSerializedAttribute), false))
                {
                    onSerialized = (TDelegate)GetSerializationMethod(type, method, owner).CreateDelegate(typeof(TDelegate));
                }
            }

            return new SerializationCallbacks<TDelegate>(onDeserializing, onDeserialized, onSerializing, onSerialized);
        }

        [SecurityCritical]
        private static DynamicMethod GetSerializationMethod(Type type, MethodInfo callbackMethod, Type owner)
        {
            Type[] callbackParameterTypes;
            if (owner.IsValueType)
            {
                callbackParameterTypes = new[] { typeof(object), owner.MakeByRefType(), typeof(StreamingContext) };
            }
            else
            {
                callbackParameterTypes = new[] { typeof(object), typeof(object), typeof(StreamingContext) };
            }

            var method = new DynamicMethod($"{callbackMethod.Name}_Trampoline", null, callbackParameterTypes, type, skipVisibility: true);
            var il = method.GetILGenerator();

            // arg0 用于更好地履行代表职能(避免辩论打折)
            il.Emit(OpCodes.Ldarg_1);
            if (type != owner)
            {
                il.Emit(OpCodes.Castclass, type);
            }

            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Callvirt, callbackMethod);
            il.Emit(OpCodes.Ret);

            return method;
        }

        /// <summary>
        /// 序列回击
        /// </summary>
        /// <typeparam name="TDelegate">代表类型,用于每次回调。</typeparam>
        public sealed class SerializationCallbacks<TDelegate>
        {
            /// <summary>
            /// 初始化 <see cref="SerializationCallbacks{TDelegate}"/> 类的新实例。
            /// </summary>
            /// <param name="onDeserializing">解毒过程中援引的回调。</param>
            /// <param name="onDeserialized">数值被淡化时所引用的回调。</param>
            /// <param name="onSerializing">连环处理时的回调</param>
            /// <param name="onSerialized">数值序列化后引用的回调。</param>
            public SerializationCallbacks(
                TDelegate? onDeserializing,
                TDelegate? onDeserialized,
                TDelegate? onSerializing,
                TDelegate? onSerialized)
            {
                OnDeserializing = onDeserializing;
                OnDeserialized = onDeserialized;
                OnSerializing = onSerializing;
                OnSerialized = onSerialized;
            }

            /// <summary>
            /// 获得回调的引用 同时进行机能消减
            /// </summary>
            public readonly TDelegate? OnDeserializing;

            /// <summary>
            /// 一旦一个值被淡化, 就会得到回调的响应。
            /// </summary>
            public readonly TDelegate? OnDeserialized;

            /// <summary>
            /// 获得在序列过程中援引的回调。
            /// </summary>
            /// <value>连环化</value>
            public readonly TDelegate? OnSerializing;

            /// <summary>
            /// 一旦一个价值被序列化, 就会得到回调的响应。
            /// </summary>
            public readonly TDelegate? OnSerialized;
        }
    }
}
