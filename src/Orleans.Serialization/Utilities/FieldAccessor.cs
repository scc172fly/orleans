using System;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Emit;
using Orleans.Serialization.Codecs;

namespace Orleans.Serialization.Utilities
{
    /// <summary>
    /// 用于设置值类型字段的委托。
    /// </summary>
    /// <typeparam name="TDeclaring">字段的声明类型。</typeparam>
    /// <typeparam name="TField">字段类型。</typeparam>
    /// <param name="instance">要设置字段的实例。</param>
    public delegate TField ValueTypeGetter<TDeclaring, out TField>(ref TDeclaring instance) where TDeclaring : struct;

    /// <summary>
    /// 用于设置值类型字段的委托。
    /// </summary>
    /// <typeparam name="TDeclaring">字段的声明类型。</typeparam>
    /// <typeparam name="TField">字段类型。</typeparam>
    /// <param name="instance">要设置字段的实例。</param>
    /// <param name="value">要设置的值。</param>
    public delegate void ValueTypeSetter<TDeclaring, in TField>(ref TDeclaring instance, TField value) where TDeclaring : struct;

    /// <summary>
    /// 提供字段访问功能。
    /// </summary>
    public static class FieldAccessor
    {
#if NET5_0_OR_GREATER
        private const DynamicallyAccessedMemberTypes FieldMembers =
            DynamicallyAccessedMemberTypes.PublicFields
            | DynamicallyAccessedMemberTypes.NonPublicFields;
#endif

        /// <summary>
        /// 返回用于获取指定字段值的委托。
        /// </summary>
        /// <returns>用于获取指定字段值的委托。</returns>
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
        /// 返回用于获取指定字段值的委托。
        /// </summary>
        /// <returns>用于获取指定字段值的委托。</returns>
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
            // 为提高委托性能，不使用 arg0（避免参数重新排列的转换桩）。
            emitter.Emit(OpCodes.Ldarg_1);
            emitter.Emit(OpCodes.Ldfld, field);
            emitter.Emit(OpCodes.Ret);

            return method.CreateDelegate((byref ? typeof(ValueTypeGetter<,>) : typeof(Func<,>)).MakeGenericType(declaringType, field.FieldType));
        }

        /// <summary>
        /// 返回用于设置某个实例中此字段值的委托。
        /// </summary>
        /// <returns>用于设置某个实例中此字段值的委托。</returns>
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
        /// 返回用于设置某个实例中此字段值的委托。
        /// </summary>
        /// <returns>用于设置某个实例中此字段值的委托。</returns>
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
            // 为提高委托性能，不使用 arg0（避免参数重新排列的转换桩）。
            emitter.Emit(OpCodes.Ldarg_1);
            emitter.Emit(OpCodes.Ldarg_2);
            emitter.Emit(OpCodes.Stfld, field);
            emitter.Emit(OpCodes.Ret);

            return method.CreateDelegate((byref ? typeof(ValueTypeSetter<,>) : typeof(Action<,>)).MakeGenericType(declaringType, field.FieldType));
        }
    }
}
