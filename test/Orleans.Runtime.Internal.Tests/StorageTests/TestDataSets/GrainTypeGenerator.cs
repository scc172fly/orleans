namespace UnitTests.StorageTests.Relational.TestDataSets
{
    public static class GrainTypeGenerator
    {
        /// <summary>
        /// 当某个泛型参数未使用但仍需传入某个函数调用时使用的哨兵值。
        /// </summary>
        private class NotApplicable { };

        public interface ITestGrainWithIntegerKey : IGrainWithIntegerKey { }

        public interface ITestGrainGenericWithIntegerKey<T> : IGrainWithIntegerKey { }

        public class TestGrainWithIntegerKey : Grain, ITestGrainWithIntegerKey { }

        public class TestGrainGenericWithIntegerKey<T> : Grain, ITestGrainGenericWithIntegerKey<T> { }

        public interface ITestGrainWithGuidKey : IGrainWithGuidKey { }

        public interface ITestGrainGenericWithGuidKey<T> : IGrainWithGuidKey { }

        public class TestGrainWithGuidKey : Grain, ITestGrainWithGuidKey { }

        public class TestGrainGenericWithGuidKey<T> : Grain, ITestGrainGenericWithGuidKey<T> { }

        public interface ITestGrainWithStringKey : IGrainWithStringKey { }

        public interface ITestGrainGenericWithStringKey<T> : IGrainWithStringKey { }

        public class TestGrainWithStringKey : Grain, ITestGrainWithStringKey { }

        public class TestGrainGenericWithStringKey<T> : Grain, ITestGrainGenericWithStringKey<T> { }

        private static Dictionary<Type, Func<Type, Type, Type>> GrainTypeSwitch { get; } = new Dictionary<Type, Func<Type, Type, Type>>
        {
            [typeof(Guid)] = (grainType, stateType) =>
            {
                if (grainType == typeof(NotApplicable))
                {
                    return typeof(TestGrainWithGuidKey);
                }
                return typeof(TestGrainGenericWithGuidKey<double>);
            },
            [typeof(long)] = (grainType, stateType) =>
            {
                if (grainType == typeof(NotApplicable))
                {
                    return typeof(TestGrainWithIntegerKey);
                }
                return typeof(TestGrainGenericWithIntegerKey<double>);
            },
            [typeof(string)] = (grainType, stateType) =>
            {
                if (grainType == typeof(NotApplicable))
                {
                    return typeof(TestGrainWithStringKey);
                }
                return typeof(TestGrainGenericWithStringKey<double>);
            }
        };


        public static string GetGrainType<TGrainKey>()
        {
            return GetGrainType<TGrainKey, NotApplicable>();
        }

        // 无法解析 Orleans.Storage.AdoNetStorageProvider，因为不需要引用包含它的程序集，故未引用。
#pragma warning disable 1574
        /// <summary>
        /// 返回 Grain 类型名称。
        /// </summary>
        /// <typeparam name="TGrainKey">用于选择键类型接口。</typeparam>
        /// <typeparam name="TGrain">用于选择 Grain 类型。</typeparam>
        /// <returns>以 typeof(T).AssemblyQualifiedName 形式表示的类。</returns>
        /// <remarks>假定 Orleans 以此形式将 <em>grainType</em> 参数传递给 <see cref="Orleans.Storage.IStorageProvider"/> 接口的实现函数。
        /// <see cref="Orleans.Storage.AdoNetStorageProvider"/> 中的私有函数 <em>ExtractClassBaseType</em> 依赖此形式。如果 Orleans 改变了此形式，
        /// 则应修正该函数。
        /// </remarks>
#pragma warning restore 1574
        public static string GetGrainType<TGrainKey, TGrain>()
        {
            Func<Type, Type, Type>? func;
            if (GrainTypeSwitch.TryGetValue(typeof(TGrainKey), out func))
            {
                return (func(typeof(TGrainKey), typeof(TGrain))).AssemblyQualifiedName!;
            }

            throw new ArgumentException(typeof(TGrainKey).Name);

        }
    }
}

#pragma warning restore ORLEANS0102
