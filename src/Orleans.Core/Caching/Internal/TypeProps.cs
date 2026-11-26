using System;

namespace Orleans.Caching.Internal;

// https://source.dot.net/#System.Collections.Concurrent/System/Collections/Concurrent/ConcurrentDictionary.cs,2293
internal static class TypeProps<T>
{
    /// <summary>T 类型是否可以用原子法(i.e.)写成, 且无被撕破的危险)。</summary>
    internal static readonly bool IsWriteAtomic = IsWriteAtomicPrivate();

    private static bool IsWriteAtomicPrivate()
    {
        // ECMA CLI 第12.6.6节解释哪些类型可以不按原子读和写,而不用
        // 见 https://www.ecma-international.org/publications/files/ECMA-ST/ECMA-335.pdf

        if (!typeof(T).IsValueType ||
            typeof(T) == typeof(nint) ||
            typeof(T) == typeof(nuint))
        {
            return true;
        }

        switch (Type.GetTypeCode(typeof(T)))
        {
            case TypeCode.Boolean:
            case TypeCode.Byte:
            case TypeCode.Char:
            case TypeCode.Int16:
            case TypeCode.Int32:
            case TypeCode.SByte:
            case TypeCode.Single:
            case TypeCode.UInt16:
            case TypeCode.UInt32:
                return true;

            case TypeCode.Double:
            case TypeCode.Int64:
            case TypeCode.UInt64:
                return nint.Size == 8;

            default:
                return false;
        }
    }
}
