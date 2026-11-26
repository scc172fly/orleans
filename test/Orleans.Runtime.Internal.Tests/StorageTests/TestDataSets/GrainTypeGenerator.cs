namespace UnitTests.StorageTests.Relational.TestDataSets
{
    public static class GrainTypeGenerator
    {
        /// <summary>
        /// 当通用参数未被使用并应用到某些函数调用时, 将一个监控值用于该值。
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

        // Orleans.Storage.AdoNetStorageProvider 无法解决,因为由于不需要,没有提及含有组件的组装。
#pragma warning disable 1574
        /// <summary>
        /// 返回 grain 类型名称。
        /// </summary>
        /// <typeparam name="TGrainKey">用于选择密钥类型接口。</typeparam>
        /// <typeparam name="TGrain">用于选择 grain的类型。</typeparam>
        /// <returns>类型( T) 中的类别 。AssemblyQualifiedName 窗体。</returns>
        /// <remarks> Asumes Orleans 将这种形式的 <em>grainType</em> 参数改为 <see cref="Orleans.Storage.IStorageProvider"/> 接口执行功能。
        /// 在 <see cref="Orleans.Storage.AdoNetStorageProvider"/> 私用函数 <em>ExtractClassBaseType</em> 中,此格式为此。应该固定
        /// 是否/如果在 Orleans中更改了该数值。
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
