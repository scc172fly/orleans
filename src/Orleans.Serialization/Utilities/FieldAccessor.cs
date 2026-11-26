using System;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Emit;
using Orleans.Serialization.Codecs;

namespace Orleans.Serialization.Utilities
{
    /// <summary>
    /// 该代表过去用数值类型来设置字段。
    /// </summary>
    /// <typeparam name="TDeclaring">字段的申报类型。</typeparam>
    /// <typeparam name="TField">字段类型。</typeparam>
    /// <param name="instance">设置字段的实例。</param>
    public delegate TField ValueTypeGetter<TDeclaring, out TField>(ref TDeclaring instance) where TDeclaring : struct;

    /// <summary>
    /// 该代表过去用数值类型来设置字段。
    /// </summary>
    /// <typeparam name="TDeclaring">字段的申报类型。</typeparam>
    /// <typeparam name="TField">字段类型。</typeparam>
    /// <param name="instance">设置字段的实例。</param>
    /// <param name="value">值被设定。</param>
    public delegate void ValueTypeSetter<TDeclaring, in TField>(ref TDeclaring instance, TField value) where TDeclaring : struct;

    /// <summary>
    /// 进入田地的功能。
    /// </summary>
    public static class FieldAccessor
    {
#if NET5_0_OR_GREATER
        private const DynamicallyAccessedMemberTypes FieldMembers =
            DynamicallyAccessedMemberTypes.PublicFields
            | DynamicallyAccessedMemberTypes.NonPublicFields;
#endif

        /// <summary>
        /// 返回代表以获取指定字段的值。
        /// </summary>
        /// <returns>代表获取指定字段的值。</returns>
        public static Delegate GetGetter(
#if NET5_0_OR_GREATER
            [DynamicallyAccessedMembers(FieldMembers)]
#endif
            Type declaringType,
            string fieldName)
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(declaringType);
            return GetGetter(declaringType, fieldName, false);
        }

        /// <summary>
        /// 返回代表以获取指定字段的值。
        /// </summary>
        /// <returns>代表获取指定字段的值。</returns>
        public static Delegate GetValueGetter(
#if NET5_0_OR_GREATER
            [DynamicallyAccessedMembers(FieldMembers)]
#endif
            Type declaringType,
            string fieldName)
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(declaringType);
            return GetGetter(declaringType, fieldName, true);
        }

        private static Delegate GetGetter(
#if NET5_0_OR_GREATER
            [DynamicallyAccessedMembers(FieldMembers)]
#endif
            Type declaringType,
            string fieldName,
            bool byref)
        {
            var field = declaringType.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;
            var parameterTypes = new[] { typeof(object), byref ? declaringType.MakeByRefType() : declaringType };

            var method = new DynamicMethod(fieldName + "Get", field.FieldType, parameterTypes, typeof(FieldAccessor).Module, true);

            var emitter = method.GetILGenerator();
            // arg0 用于更好地履行代表职能(避免辩论打折)
            emitter.Emit(OpCodes.Ldarg_1);
            emitter.Emit(OpCodes.Ldfld, field);
            emitter.Emit(OpCodes.Ret);

            return method.CreateDelegate((byref ? typeof(ValueTypeGetter<,>) : typeof(Func<,>)).MakeGenericType(declaringType, field.FieldType));
        }

        /// <summary>
        /// 返回代表以设置此字段的值。
        /// </summary>
        /// <returns>代表为举例而确定此字段的价值。</returns>
        public static Delegate GetReferenceSetter(
#if NET5_0_OR_GREATER
            [DynamicallyAccessedMembers(FieldMembers)]
#endif
            Type declaringType,
            string fieldName)
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(declaringType);
            return GetSetter(declaringType, fieldName, false);
        }

        /// <summary>
        /// 返回代表以设置此字段的值。
        /// </summary>
        /// <returns>代表为举例而确定此字段的价值。</returns>
        public static Delegate GetValueSetter(
#if NET5_0_OR_GREATER
            [DynamicallyAccessedMembers(FieldMembers)]
#endif
            Type declaringType,
            string fieldName)
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(declaringType);
            return GetSetter(declaringType, fieldName, true);
        }

        private static Delegate GetSetter(
#if NET5_0_OR_GREATER
            [DynamicallyAccessedMembers(FieldMembers)]
#endif
            Type declaringType,
            string fieldName,
            bool byref)
        {
            var field = declaringType.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;
            var parameterTypes = new[] { typeof(object), byref ? declaringType.MakeByRefType() : declaringType, field.FieldType };

            var method = new DynamicMethod(fieldName + "Set", null, parameterTypes, typeof(FieldAccessor).Module, true);

            var emitter = method.GetILGenerator();
            // arg0 用于更好地履行代表职能(避免辩论打折)
            emitter.Emit(OpCodes.Ldarg_1);
            emitter.Emit(OpCodes.Ldarg_2);
            emitter.Emit(OpCodes.Stfld, field);
            emitter.Emit(OpCodes.Ret);

            return method.CreateDelegate((byref ? typeof(ValueTypeSetter<,>) : typeof(Action<,>)).MakeGenericType(declaringType, field.FieldType));
        }
    }
}
