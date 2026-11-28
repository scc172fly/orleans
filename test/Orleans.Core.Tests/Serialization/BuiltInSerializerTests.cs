using System.Collections.ObjectModel;
using Orleans.Concurrency;
using Orleans.GrainDirectory;
using Orleans.Runtime;
using Orleans.Serialization;
using Orleans.Serialization.TypeSystem;
using Orleans.Streaming.EventHubs;
using Orleans.Streams;
using TestExtensions;
using TestGrainInterfaces;
using UnitTests.GrainInterfaces;
using UnitTests.Grains;
using Xunit;

// ReSharper disable NotAccessedVariable

namespace UnitTests.Serialization
{
    /// <summary>
    /// Orleans 内置序列化器的测试，这些序列化器处理常见 .NET 类型及 Orleans 特有类型的序列化。
    /// 这些序列化器对于 Grain 通信、状态持久化及流处理至关重要。测试验证
    /// Orleans 能否正确序列化和反序列化整个框架中使用的各种数据结构。
    /// </summary>
    [Collection(TestEnvironmentFixture.DefaultCollection), TestCategory("Serialization")]
    [TestArea("Serialization")]
    public class BuiltInSerializerTests
    {
        private readonly ITestOutputHelper output;
        private readonly TestEnvironmentFixture environment;

        public BuiltInSerializerTests(ITestOutputHelper output, TestEnvironmentFixture fixture)
        {
            this.output = output;
            this.environment = fixture;
        }

        /// <summary>
        /// 验证 Orleans 内部类型是否注册了适当的序列化器。
        /// 这对 Orleans 内部消息传递及状态管理的正常运行至关重要。
        /// </summary>
        [TestSuite("BVT")]
        [TestProvider("None")]
        [Fact, TestCategory("BVT"), TestCategory("CodeGen")]
        public void InternalSerializableTypesHaveSerializers()
        {
            Assert.True(
                environment.Serializer.CanSerialize<int>(),
                $"Should be able to serialize internal type {nameof(Int32)}.");
            Assert.True(
                environment.Serializer.CanSerialize(typeof(List<int>)),
                $"Should be able to serialize internal type {nameof(List<int>)}.");
            Assert.True(
                environment.Serializer.CanSerialize(typeof(PubSubGrainState)),
                $"Should be able to serialize internal type {nameof(PubSubGrainState)}.");
            Assert.True(
                environment.Serializer.CanSerialize(typeof(EventHubBatchContainer)),
                $"Should be able to serialize internal type {nameof(EventHubBatchContainer)}.");
            Assert.True(
                environment.Serializer.CanSerialize(typeof(EventHubSequenceTokenV2)),
                $"Should be able to serialize internal type {nameof(EventHubSequenceTokenV2)}.");
        }

        [TestSuite("BVT")]
        [TestProvider("None")]
        [Fact, TestCategory("BVT"), TestCategory("CodeGen")]
        public void ValueTupleTypesHasSerializer()
        {
            Assert.True(
                environment.Serializer.CanSerialize(typeof(ValueTuple<int, AddressAndTag>)),
                $"Should be able to serialize internal type {nameof(ValueTuple<int, AddressAndTag>)}.");
        }

        /// <summary>
        /// 测试默认（非回退）序列化器能否处理复杂类。
        /// </summary>
        [TestSuite("BVT")]
        [TestProvider("None")]
        [Fact, TestCategory("BVT")]
        public void Serialize_ComplexAccessibleClass()
        {
            var expected = new AnotherConcreteClass
            {
                Int = 89,
                AnotherString = Guid.NewGuid().ToString(),
                NonSerializedInt = 39,
                Enum = SomeAbstractClass.SomeEnum.Something,
            };

            expected.Classes = new SomeAbstractClass[]
            {
                expected,
                new AnotherConcreteClass
                {
                    AnotherString = "hi",
                    Interfaces = new List<ISomeInterface> { expected }
                }
            };
            expected.Interfaces = new List<ISomeInterface>
            {
                expected.Classes[1]
            };
            expected.SetObsoleteInt(38);

            var actual = (AnotherConcreteClass)OrleansSerializationLoop(environment.Serializer, environment.DeepCopier, expected);
            var expectedClasses = expected.Classes;
            var expectedInterfaces = expected.Interfaces;
            var actualClasses = actual.Classes;
            var actualInterfaces = actual.Interfaces;
            Assert.NotNull(expectedClasses);
            Assert.NotNull(expectedInterfaces);
            Assert.NotNull(actualClasses);
            Assert.NotNull(actualInterfaces);
            var expectedNestedInterfaces = expectedClasses[1].Interfaces;
            var actualNestedInterfaces = actualClasses[1].Interfaces;
            Assert.NotNull(expectedNestedInterfaces);
            Assert.NotNull(actualNestedInterfaces);

            Assert.Equal(expected.Int, actual.Int);
            Assert.Equal(expected.Enum, actual.Enum);
            Assert.Equal(expected.AnotherString, actual.AnotherString);
            Assert.Equal(expectedClasses.Length, actualClasses.Length);
            Assert.Equal(expectedNestedInterfaces[0].Int, actualNestedInterfaces[0].Int);
            Assert.Equal(expectedInterfaces[0].Int, actualInterfaces[0].Int);
            Assert.Equal(actualInterfaces[0], actualClasses[1]);
            Assert.NotEqual(expected.NonSerializedInt, actual.NonSerializedInt);
            Assert.Equal(0, actual.NonSerializedInt);
            Assert.Equal(expected.GetObsoleteInt(), actual.GetObsoleteInt());
            Assert.Null(actual.SomeGrainReference);
        }

        [TestSuite("BVT")]
        [TestProvider("None")]
        [Fact, TestCategory("BVT")]
        public void Serialize_Type()
        {

            // 测试 Type 的序列化。
            var expected = typeof(int);
            var actual = (Type)OrleansSerializationLoop(environment.Serializer, environment.DeepCopier, expected);
            Assert.Equal(expected.AssemblyQualifiedName, actual.AssemblyQualifiedName);

            // 测试 RuntimeType 的序列化。
            expected = 8.GetType();
            actual = (Type)OrleansSerializationLoop(environment.Serializer, environment.DeepCopier, expected);
            Assert.Equal(expected.AssemblyQualifiedName, actual.AssemblyQualifiedName);
        }

        [TestSuite("BVT")]
        [TestProvider("None")]
        [Fact, TestCategory("BVT")]
        public void Serialize_ComplexStruct()
        {

            // 测试结构体序列化。
            var expected = new SomeStruct(10) { Id = Guid.NewGuid(), PublicValue = 6, ValueWithPrivateGetter = 7 };
            expected.SetValueWithPrivateSetter(8);
            expected.SetPrivateValue(9);
            var actual = (SomeStruct)OrleansSerializationLoop(environment.Serializer, environment.DeepCopier, expected);
            Assert.Equal(expected.Id, actual.Id);
            Assert.Equal(expected.ReadonlyField, actual.ReadonlyField);
            Assert.Equal(expected.PublicValue, actual.PublicValue);
            Assert.Equal(expected.ValueWithPrivateSetter, actual.ValueWithPrivateSetter);
            Assert.Null(actual.SomeGrainReference);
            Assert.Equal(expected.GetPrivateValue(), actual.GetPrivateValue());
            Assert.Equal(expected.GetValueWithPrivateGetter(), actual.GetValueWithPrivateGetter());
        }

        [TestSuite("Functional")]
        [TestProvider("None")]
        [Fact, TestCategory("Functional")]
        public void Serialize_EmptyList()
        {
            var list = new List<int>();
            var deserialized = OrleansSerializationLoop(environment.Serializer, environment.DeepCopier, list, false);
            Assert.IsAssignableFrom<List<int>>(deserialized);  //空整数列表被复制为错误的类型"
            ValidateList(list, (List<int>)deserialized, "int (empty, copy)");
            deserialized = OrleansSerializationLoop(environment.Serializer, environment.DeepCopier, list);
            Assert.IsAssignableFrom<List<int>>(deserialized); //空整数列表经过完整序列化往返后的类型错误
            ValidateList(list, (List<int>)deserialized, "int (empty)");
        }

        [TestSuite("Functional")]
        [TestProvider("None")]
        [Fact, TestCategory("Functional")]
        public void Serialize_BasicDictionaries()
        {

            Dictionary<string, string> source1 = new Dictionary<string, string>();
            source1["Hello"] = "Yes";
            source1["Goodbye"] = "No";
            var deserialized = OrleansSerializationLoop(environment.Serializer, environment.DeepCopier, source1);
            ValidateDictionary<string, string>(source1, deserialized, "string/string");

            Dictionary<int, DateTime> source2 = new Dictionary<int, DateTime>();
            source2[3] = DateTime.Now;
            source2[27] = DateTime.Now.AddHours(2);
            deserialized = OrleansSerializationLoop(environment.Serializer, environment.DeepCopier, source2);
            ValidateDictionary<int, DateTime>(source2, deserialized, "int/date");
        }

        [TestSuite("Functional")]
        [TestProvider("None")]
        [Fact, TestCategory("Functional")]
        public void Serialize_ReadOnlyDictionary()
        {
            Dictionary<string, string> source1 = new Dictionary<string, string>();
            source1["Hello"] = "Yes";
            source1["Goodbye"] = "No";
            var readOnlySource1 = new ReadOnlyDictionary<string, string>(source1);
            var deserialized = OrleansSerializationLoop(environment.Serializer, environment.DeepCopier, readOnlySource1);
            ValidateReadOnlyDictionary(readOnlySource1, deserialized, "string/string");

            Dictionary<int, DateTime> source2 = new Dictionary<int, DateTime>();
            source2[3] = DateTime.Now;
            source2[27] = DateTime.Now.AddHours(2);
            var readOnlySource2 = new ReadOnlyDictionary<int, DateTime>(source2);
            deserialized = OrleansSerializationLoop(environment.Serializer, environment.DeepCopier, readOnlySource2);
            ValidateReadOnlyDictionary(readOnlySource2, deserialized, "int/date");
        }

        [TestSuite("Functional")]
        [TestProvider("None")]
        [Fact, TestCategory("Functional")]
        public void Serialize_DictionaryWithComparer()
        {
            Dictionary<string, string> source1 = new Dictionary<string, string>(new CaseInsensitiveStringEquality());
            source1["Hello"] = "Yes";
            source1["Goodbye"] = "No";
            var deserialized = OrleansSerializationLoop(environment.Serializer, environment.DeepCopier, source1);
            ValidateDictionary<string, string>(source1, deserialized, "case-insensitive string/string");
            Dictionary<string, string> result1 = (deserialized as Dictionary<string, string>)!;
            Assert.Equal(source1["Hello"], result1["hElLo"]); //不区分大小写的 string/string 字典在序列化往返后丢失了自定义比较器

            Dictionary<int, DateTime> source2 = new Dictionary<int, DateTime>(new Mod5IntegerComparer());
            source2[3] = DateTime.Now;
            source2[27] = DateTime.Now.AddHours(2);
            deserialized = OrleansSerializationLoop(environment.Serializer, environment.DeepCopier, source2);
            ValidateDictionary<int, DateTime>(source2, deserialized, "int/date");
            Dictionary<int, DateTime> result2 = (Dictionary<int, DateTime>)deserialized;
            Assert.Equal<DateTime>(source2[3], result2[13]);  //不区分大小写的 int/DateTime 字典在序列化往返后丢失了自定义比较器"（译注：原注释中的“不区分大小写”不适用于此处；此处实际使用 Mod5IntegerComparer。）
        }

        [TestSuite("Functional")]
        [TestProvider("None")]
        [Fact, TestCategory("Functional")]
        public void Serialize_SortedDictionaryWithComparer()
        {
            var source1 = new SortedDictionary<string, string>(new CaseInsensitiveStringComparer());
            source1["Hello"] = "Yes";
            source1["Goodbye"] = "No";
            object deserialized = OrleansSerializationLoop(environment.Serializer, environment.DeepCopier, source1);
            ValidateSortedDictionary<string, string>(source1, deserialized, "string/string");
        }

        [TestSuite("Functional")]
        [TestProvider("None")]
        [Fact, TestCategory("Functional")]
        public void Serialize_SortedListWithComparer()
        {
            var source1 = new SortedList<string, string>(new CaseInsensitiveStringComparer());
            source1["Hello"] = "Yes";
            source1["Goodbye"] = "No";
            object deserialized = OrleansSerializationLoop(environment.Serializer, environment.DeepCopier, source1);
            ValidateSortedList<string, string>(source1, deserialized, "string/string");
        }

        [TestSuite("Functional")]
        [TestProvider("None")]
        [Fact, TestCategory("Functional")]
        public void Serialize_HashSetWithComparer()
        {
            var source1 = new HashSet<string>(new CaseInsensitiveStringEquality());
            source1.Add("one");
            source1.Add("two");
            source1.Add("three");
            var deserialized = OrleansSerializationLoop(environment.Serializer, environment.DeepCopier, source1);
            Assert.IsAssignableFrom(source1.GetType(), deserialized); //带比较器的字符串哈希集合在序列化往返后的类型错误
            var result = (deserialized as HashSet<string>)!;
            Assert.Equal(source1.Count, result.Count); //带比较器的字符串哈希集合在序列化往返后的元素数量错误
#pragma warning disable xUnit2017 // Do not use Contains() to check if a value exists in a collection
            foreach (var key in source1)
            {
                Assert.True(result.Contains(key)); //带比较器的字符串哈希集合在序列化往返后缺少键
            }
            Assert.True(result.Contains("One")); //带比较器的字符串哈希集合在序列化往返后的比较器错误
#pragma warning restore xUnit2017 // Do not use Contains() to check if a value exists in a collection
        }

        [TestSuite("Functional")]
        [TestProvider("None")]
        [Fact, TestCategory("Functional")]
        public void Serialize_Stack()
        {
            var source1 = new Stack<string>();
            source1.Push("one");
            source1.Push("two");
            source1.Push("three");
            object deserialized = OrleansSerializationLoop(environment.Serializer, environment.DeepCopier, source1);
            Assert.IsAssignableFrom(source1.GetType(), deserialized); //字符串栈在序列化往返后的类型错误
            var result = (deserialized as Stack<string>)!;
            Assert.Equal(source1.Count, result.Count); //字符串栈在序列化往返后的元素数量错误

            var srcIter = source1.GetEnumerator();
            var resIter = result.GetEnumerator();
            while (srcIter.MoveNext() && resIter.MoveNext())
            {
                Assert.Equal(srcIter.Current, resIter.Current); //字符串栈在序列化往返后的数据错误
            }
        }

        /// <summary>
        /// 测试反序列化后是否调用 <see cref="IOnDeserialized"/> 回调。
        /// </summary>
        /// <param name="serializerToUse"></param>
        [TestSuite("Functional")]
        [TestProvider("None")]
        [Fact, TestCategory("Functional")]
        public void Serialize_TypeWithOnDeserializedHook()
        {
            var input = new TypeWithOnDeserializedHook
            {
                Int = 5
            };
            var deserialized = OrleansSerializationLoop(environment.Serializer, environment.DeepCopier, input);
            var result = Assert.IsType<TypeWithOnDeserializedHook>(deserialized);
            Assert.Equal(input.Int, result.Int);
            Assert.Null(input.Context);
            Assert.NotNull(result.Context);
        }

        [TestSuite("Functional")]
        [TestProvider("None")]
        [Fact, TestCategory("Functional")]
        public void Serialize_SortedSetWithComparer()
        {
            var source1 = new SortedSet<string>(new CaseInsensitiveStringComparer());
            source1.Add("one");
            source1.Add("two");
            source1.Add("three");
            object deserialized = OrleansSerializationLoop(environment.Serializer, environment.DeepCopier, source1);
            Assert.IsAssignableFrom(source1.GetType(), deserialized); //带比较器的字符串有序集合在序列化往返后的类型错误
            var result = (SortedSet<string>)deserialized;
            Assert.Equal(source1.Count, result.Count); //带比较器的字符串有序集合在序列化往返后的元素数量错误
#pragma warning disable xUnit2017 // Do not use Contains() to check if a value exists in a collection
            foreach (var key in source1)
            {
                Assert.True(result.Contains(key)); //带比较器的字符串有序集合在序列化往返后缺少键
            }
            Assert.True(result.Contains("One")); //带比较器的字符串有序集合在序列化往返后的比较器错误
#pragma warning restore xUnit2017 // Do not use Contains() to check if a value exists in a collection
        }

        [TestSuite("Functional")]
        [TestProvider("None")]
        [Fact, TestCategory("Functional")]
        public void Serialize_Array()
        {
            var source1 = new int[] { 1, 3, 5 };
            object deserialized = OrleansSerializationLoop(environment.Serializer, environment.DeepCopier, source1);
            ValidateArray<int>(source1, deserialized, "int");

            var source2 = new string[] { "hello", "goodbye", "yes", "no", "", "I don't know" };
            deserialized = OrleansSerializationLoop(environment.Serializer, environment.DeepCopier, source2);
            ValidateArray<string>(source2, deserialized, "string");

            var source3 = new sbyte[] { 1, 3, 5 };
            deserialized = OrleansSerializationLoop(environment.Serializer, environment.DeepCopier, source3);
            ValidateArray<sbyte>(source3, deserialized, "sbyte");

            var source4 = new byte[] { 1, 3, 5 };
            deserialized = OrleansSerializationLoop(environment.Serializer, environment.DeepCopier, source4);
            ValidateArray<byte>(source4, deserialized, "byte");
        }

        [TestSuite("Functional")]
        [TestProvider("None")]
        [Fact, TestCategory("Functional")]
        public void Serialize_ArrayOfArrays()
        {
            var source1 = new[] { new[] { 1, 3, 5 }, new[] { 10, 20, 30 }, new[] { 17, 13, 11, 7, 5, 3, 2 } };
            object deserialized = OrleansSerializationLoop(environment.Serializer, environment.DeepCopier, source1);
            ValidateArrayOfArrays(source1, deserialized, "int");

            var source2 = new[] { new[] { "hello", "goodbye", "yes", "no", "", "I don't know" }, new[] { "yes" } };
            deserialized = OrleansSerializationLoop(environment.Serializer, environment.DeepCopier, source2);
            ValidateArrayOfArrays(source2, deserialized, "string");

            var source3 = new HashSet<string>?[3][];
            source3[0] = new HashSet<string>?[2];
            source3[1] = new HashSet<string>?[3];
            source3[2] = new HashSet<string>?[1];
            source3[0][0] = new HashSet<string>();
            source3[0][1] = new HashSet<string>();
            source3[1][0] = new HashSet<string>();
            source3[1][1] = null;
            source3[1][2] = new HashSet<string>();
            source3[2][0] = new HashSet<string>();
            source3[0][0]!.Add("this");
            source3[0][0]!.Add("that");
            source3[1][0]!.Add("the other");
            source3[1][2]!.Add("and another");
            source3[2][0]!.Add("but not yet another");
            deserialized = OrleansSerializationLoop(environment.Serializer, environment.DeepCopier, source3);
            var result = Assert.IsAssignableFrom<HashSet<string>?[][]>(deserialized); //哈希集合嵌套数组在反序列化后的类型错误
            Assert.Equal(3, result.Length); //集合嵌套数组的外层数组长度错误
            Assert.Equal(2, result[0][0]!.Count); //集合嵌套数组中位置 0,0 的内层集合大小错误
            Assert.Empty(result[0][1]!); //集合嵌套数组中位置 0,1 的内层集合大小错误
            Assert.Single(result[1][0]!); //集合嵌套数组中位置 1,0 的内层集合大小错误
            Assert.Null(result[1][1]); //集合嵌套数组中位置 1, 1 的内层集合不是 null
            Assert.Single(result[1][2]!); //集合嵌套数组中位置 1,2 的内层集合大小错误
            Assert.Single(result[2][0]!); //集合嵌套数组中位置 2,0 的内层集合大小错误

            var source4 = new GrainReference[3][];
            source4[0] = new GrainReference[2];
            source4[1] = new GrainReference[3];
            source4[2] = new GrainReference[1];
            source4[0][0] = (GrainReference)environment.InternalGrainFactory.GetGrain(LegacyGrainId.NewId());
            source4[0][1] = (GrainReference)environment.InternalGrainFactory.GetGrain(LegacyGrainId.NewId());
            source4[1][0] = (GrainReference)environment.InternalGrainFactory.GetGrain(LegacyGrainId.NewId());
            source4[1][1] = (GrainReference)environment.InternalGrainFactory.GetGrain(LegacyGrainId.NewId());
            source4[1][2] = (GrainReference)environment.InternalGrainFactory.GetGrain(LegacyGrainId.NewId());
            source4[2][0] = (GrainReference)environment.InternalGrainFactory.GetGrain(LegacyGrainId.NewId());
            deserialized = OrleansSerializationLoop(environment.Serializer, environment.DeepCopier, source4);
            ValidateArrayOfArrays(source4, deserialized, "grain reference");

            var source5 = new GrainReference[32][];
            for (int i = 0; i < source5.Length; i++)
            {
                source5[i] = new GrainReference[64];
                for (int j = 0; j < source5[i].Length; j++)
                {
                    source5[i][j] = (GrainReference)environment.InternalGrainFactory.GetGrain(LegacyGrainId.NewId());
                }
            }
            deserialized = OrleansSerializationLoop(environment.Serializer, environment.DeepCopier, source5);
            ValidateArrayOfArrays(source5, deserialized, "grain reference (large)");
        }

        [TestSuite("Functional")]
        [TestProvider("None")]
        [Fact, TestCategory("Functional")]
        public void Serialize_ArrayOfArrayOfArrays()
        {
            var source1 = new[] { new[] { 1, 3, 5 }, new[] { 10, 20, 30 }, new[] { 17, 13, 11, 7, 5, 3, 2 } };
            var source2 = new[] { new[] { 1, 3 }, new[] { 10, 20 }, new[] { 17, 13, 11, 7, 5 } };
            var source3 = new[] { new[] { 1, 3, 5 }, new[] { 10, 20, 30 } };
            var source = new[] { source1, source2, source3 };
            object deserialized = OrleansSerializationLoop(environment.Serializer, environment.DeepCopier, source);
            ValidateArrayOfArrayOfArrays(source, deserialized, "int");
        }

        [TestSuite("Functional")]
        [TestProvider("None")]
        [Fact, TestCategory("Functional")]
        public void Serialize_ReadOnlyCollection()
        {
            var source1 = new List<string> { "Yes", "No" };
            var collection = new ReadOnlyCollection<string>(source1);
            var deserialized = OrleansSerializationLoop(environment.Serializer, environment.DeepCopier, collection);
            ValidateReadOnlyCollectionList(collection, deserialized, "string/string");
        }

        private class BanningTypeResolver : TypeResolver
        {
            private readonly CachedTypeResolver _resolver = new CachedTypeResolver();
            private readonly HashSet<Type> _blockedTypes;

            public BanningTypeResolver(params Type[] blockedTypes)
            {
                _blockedTypes = new HashSet<Type>();
                foreach (var type in blockedTypes ?? Array.Empty<Type>())
                {
                    _blockedTypes.Add(type);
                }
            }

            public override Type ResolveType(string name)
            {
                var result = _resolver.ResolveType(name);
                if (_blockedTypes.Contains(result))
                {
                    result = null!;
                }

                return result;
            }

            public override bool TryResolveType(string name, out Type type)
            {
                if (_resolver.TryResolveType(name, out type))
                {
                    if (_blockedTypes.Contains(type))
                    {
                        type = null!;
                        return false;
                    }

                    return true;
                }

                return false;
            }
        }

        [TestSuite("Functional")]
        [TestProvider("None")]
        [Fact, TestCategory("Functional")]
        public void Serialize_ObjectIdentity()
        {
            var val = new List<string> { "first", "second" };

            var val2 = new List<string> { "first", "second" };

            var source = new Dictionary<string, List<string>>();
            source["one"] = val;
            source["two"] = val;
            source["three"] = val2;
            Assert.Same(source["one"], source["two"]); //string/list 字典在序列化往返前已丢失对象标识一致性!!!

            var deserialized = OrleansSerializationLoop(environment.Serializer, environment.DeepCopier, source);
            var result = Assert.IsAssignableFrom<Dictionary<string, List<string>>>(deserialized); //string/list 字典在序列化往返后的类型错误
            Assert.Equal(source.Count, result.Count); //string/list 字典在序列化往返后的元素数量错误

            List<string>? list1;
            List<string>? list2;
            List<string>? list3;
            Assert.True(result.TryGetValue("one", out list1)); //string/list 字典在序列化往返后找不到键 'one'
            Assert.True(result.TryGetValue("two", out list2)); //string/list 字典在序列化往返后找不到键 'two'
            Assert.True(result.TryGetValue("three", out list3)); //string/list 字典在序列化往返后找不到键 'three'

            ValidateList<string>(val, list1!, "string");
            ValidateList<string>(val, list2!, "string");
            ValidateList<string>(val2, list3!, "string");

            Assert.Same(list1, list2); //string/list 字典在序列化往返后丢失了对象标识一致性
            Assert.NotSame(list2, list3); //string/list 字典在序列化往返后使原本不同的对象意外共用了同一对象引用
            Assert.NotSame(list1, list3); //string/list 字典在序列化往返后使原本不同的对象意外共用了同一对象引用
        }

        [TestSuite("Functional")]
        [TestProvider("None")]
        [Fact, TestCategory("Functional")]
        public void Serialize_Unrecognized()
        {
            var test1 = new Unrecognized { A = 3, B = 27 };
            var raw = OrleansSerializationLoop(environment.Serializer, environment.DeepCopier, test1, false);
            var result = Assert.IsAssignableFrom<Unrecognized>(raw); //unrecognized 对象在深拷贝后的类型错误
            Assert.Equal(3, result.A);  //unrecognized 对象在深拷贝后的属性 A 错误"
            Assert.Equal(27, result.B);  //unrecognized 对象在深拷贝后的属性 B 错误"

            var test2 = new Unrecognized[3];
            for (int i = 0; i < 3; i++)
            {
                test2[i] = new Unrecognized { A = i, B = 2 * i };
            }
            raw = OrleansSerializationLoop(environment.Serializer, environment.DeepCopier, test2);
            Assert.IsAssignableFrom<Unrecognized[]>(raw); //unrecognized 数组在序列化往返后的类型错误
            var result2 = (Unrecognized[])raw;
            Assert.Equal(3, result2.Length); //unrecognized 数组在序列化往返后的长度错误
            for (int j = 0; j < 3; j++)
            {
                Assert.Equal(j, result2[j].A); //unrecognized 数组在序列化往返后，索引 " + j + " 处的属性 A 错误
                Assert.Equal(2 * j, result2[j].B); //unrecognized 数组在序列化往返后，索引 " + j + " 处的属性 B 错误
            }
        }

        [TestSuite("Functional")]
        [TestProvider("None")]
        [Fact, TestCategory("Functional")]
        public void Serialize_Immutable()
        {
            var test1 = new ImmutableType(3, 27);
            object? raw = environment.DeepCopier.Copy<object>(test1);
            var copiedTest1 = Assert.IsAssignableFrom<ImmutableType>(raw); //[Immutable] 类型在深拷贝后的类型错误
            Assert.Same(test1, copiedTest1); //对 [Immutable] 对象的深拷贝创建了副本，而不是仅复制引用

            var test2list = new List<int>();
            for (int i = 0; i < 3; i++)
            {
                test2list.Add(i);
            }
            var test2 = new Immutable<List<int>>(test2list);
            raw = environment.DeepCopier.Copy<object>(test2);
            var copiedTest2 = Assert.IsAssignableFrom<Immutable<List<int>>>(raw); //Immutable<> 数组在序列化往返后的类型错误（译注：原注释称数组往返，此处实际对 Immutable<List<int>> 对象执行深拷贝。）
            Assert.Same(test2.Value, copiedTest2.Value); //对 Immutable<> 对象的深拷贝创建了副本，而不是仅复制引用

            var test3 = new EmbeddedImmutable("test", 1, 2, 3, 4);
            raw = environment.DeepCopier.Copy<object>(test3);
            var copiedTest3 = Assert.IsAssignableFrom<EmbeddedImmutable>(raw); //包含 Immutable<> 字段的类型在深拷贝后的类型错误
            Assert.Same(test3.B.Value, copiedTest3.B.Value); //对内嵌 [Immutable] 对象的深拷贝创建了副本，而不是仅复制引用

            var test4 = new ClassWithEmbeddedImmutable { Immutable = new byte[] { 1 }, Mutable = new byte[] { 2 } };
            raw = environment.DeepCopier.Copy<object>(test4);
            var copiedTest4 = Assert.IsAssignableFrom<ClassWithEmbeddedImmutable>(raw); //包含 Immutable<> 字段的类型在深拷贝后的类型错误（译注：原注释中的 Immutable<> 并非此处的字段类型；该字段实际为带 [Immutable] 标记的 IEnumerable<byte>?。）
            Assert.Same(test4.Immutable, copiedTest4.Immutable); //对内嵌 [Immutable] 对象的深拷贝创建了副本，而不是仅复制引用
            Assert.NotSame(test4.Mutable, copiedTest4.Mutable);

            var test5 = new StructWithEmbeddedImmutable { Immutable = new byte[] { 1 }, Mutable = new byte[] { 2 } };
            raw = environment.DeepCopier.Copy<object>(test5);
            var copiedTest5 = Assert.IsAssignableFrom<StructWithEmbeddedImmutable>(raw); //包含 Immutable<> 字段的类型在深拷贝后的类型错误（译注：原注释中的 Immutable<> 并非此处的字段类型；该字段实际为带 [Immutable] 标记的 byte[]?。）
            Assert.Same(test5.Immutable, copiedTest5.Immutable); //对内嵌 [Immutable] 对象的深拷贝创建了副本，而不是仅复制引用
            Assert.NotSame(test5.Mutable, copiedTest5.Mutable);
        }

        [TestSuite("Functional")]
        [TestProvider("None")]
        [Fact, TestCategory("Functional")]
        public void Serialize_GrainReference()
        {
            GrainId grainId = LegacyGrainId.NewId();
            GrainReference input = (GrainReference)environment.InternalGrainFactory.GetGrain(grainId);

            object deserialized = OrleansSerializationLoop(environment.Serializer, environment.DeepCopier, input);

            var grainRef = Assert.IsAssignableFrom<GrainReference>(deserialized); //GrainReference 被复制为错误的类型
            Assert.Equal(grainId, grainRef.GrainId); //复制后的 GrainId 不同
            Assert.Equal(input, grainRef); //input 在序列化往返后的内容错误
        }

        internal bool AreByteArraysAreEqual(byte[] array1, byte[] array2)
        {
            if (array1.Length != array2.Length)
                return false;

            for (int i = 0; i < array1.Length; i++)
            {
                if (array1[i] != array2[i])
                    return false;
            }

            return true;
        }

        internal static object OrleansSerializationLoop(Serializer serializer, DeepCopier copier, object input, bool includeWire = true)
        {
            object? copy = copier.Copy(input);
            if (includeWire)
            {
                copy = serializer.RoundTripSerializationForTesting(copy);
            }

            Assert.NotNull(copy);
            return copy;
        }

        private void ValidateDictionary<K, V>(Dictionary<K, V> source, object deserialized, string type)
            where K : notnull
        {
            var result = Assert.IsAssignableFrom<Dictionary<K, V>>(deserialized); //字典在序列化往返后的类型错误
            ValidateDictionaryContent(source, result, type);
        }

        private void ValidateDictionaryContent<K, V>(IDictionary<K, V> source, IDictionary<K, V> result, string type)
            where K : notnull
        {
            Assert.Equal(source.Count, result.Count);  //" + type + " 字典在序列化往返后的元素数量错误"
            foreach (var pair in source)
            {
                Assert.True(result.ContainsKey(pair.Key), "Key " + pair.Key.ToString() + " is missing after round-trip of " + type + " dict");
                Assert.Equal<V>(pair.Value, result[pair.Key]); //序列化往返后，键所对应的值错误
            }
        }

        private void ValidateReadOnlyDictionary<K, V>(ReadOnlyDictionary<K, V> source, object deserialized, string type)
            where K : notnull
        {
            var result = Assert.IsAssignableFrom<ReadOnlyDictionary<K, V>>(deserialized); //序列化往返后的类型错误
            ValidateDictionaryContent(source, result, type);
        }

        private void ValidateSortedDictionary<K, V>(SortedDictionary<K, V> source, object deserialized, string type)
            where K : notnull
        {
            Assert.IsAssignableFrom<SortedDictionary<K, V>>(deserialized);
            SortedDictionary<K, V> result = (deserialized as SortedDictionary<K, V>)!;
            Assert.Equal(source.Count, result.Count); //" + type + " 有序字典在序列化往返后的元素数量错误
            foreach (var pair in source)
            {
                Assert.True(result.ContainsKey(pair.Key)); //" + type + " 有序字典在序列化往返后缺少键 " + pair.Key.ToString() + "
                Assert.Equal<V>(pair.Value, result[pair.Key]); //" + type + " 有序字典在序列化往返后，键 " + pair.Key.ToString() + " 所对应的值错误
            }

            var sourceKeys = source.Keys.GetEnumerator();
            var resultKeys = result.Keys.GetEnumerator();
            while (sourceKeys.MoveNext() && resultKeys.MoveNext())
            {
                Assert.Equal<K>(sourceKeys.Current, resultKeys.Current); //" + type + " 有序字典在序列化往返后的键顺序错误
            }
        }

        private void ValidateSortedList<K, V>(SortedList<K, V> source, object deserialized, string type)
            where K : notnull
        {
            Assert.IsAssignableFrom<SortedList<K, V>>(deserialized);
            SortedList<K, V> result = (deserialized as SortedList<K, V>)!;
            Assert.Equal(source.Count, result.Count);  //" + type + " 有序列表在序列化往返后的元素数量错误"
            foreach (var pair in source)
            {
                Assert.True(result.ContainsKey(pair.Key)); //" + type + " 有序列表在序列化往返后缺少键 " + pair.Key.ToString() + "
                Assert.Equal<V>(pair.Value, result[pair.Key]); //" + type + " 有序列表在序列化往返后，键 " + pair.Key.ToString() + " 所对应的值错误
            }

            var sourceKeys = source.Keys.GetEnumerator();
            var resultKeys = result.Keys.GetEnumerator();
            while (sourceKeys.MoveNext() && resultKeys.MoveNext())
            {
                Assert.Equal<K>(sourceKeys.Current, resultKeys.Current); //" + type + " 有序列表在序列化往返后的键顺序错误
            }
        }

        private void ValidateReadOnlyCollectionList<T>(ReadOnlyCollection<T> expected, object deserialized, string type)
        {
            Assert.IsAssignableFrom<ReadOnlyCollection<T>>(deserialized); //" + type + " 数组在序列化往返后的类型错误（译注：原注释称数组，此处实际验证 ReadOnlyCollection<T>。）
            ValidateList(expected, (deserialized as IList<T>)!, type);
        }

        private void ValidateList<T>(IList<T> expected, IList<T> result, string type)
        {
            Assert.Equal(expected.Count, result.Count);
            for (int i = 0; i < expected.Count; i++)
            {
                Assert.Equal<T>(expected[i], result[i]); //" + type + " 列表在序列化往返后，第 " + i + " 个元素错误
            }
        }

        private void ValidateArray<T>(T[] expected, object deserialized, string type)
        {
            var result = Assert.IsAssignableFrom<T[]>(deserialized);
            Assert.Equal(expected.Length, result.Length);  //" + type + " 数组在序列化往返后的长度错误"
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.Equal<T>(expected[i], result[i]);  //" + type + " 数组在序列化往返后，第 " + i + " 个元素错误"
            }
        }

        private void ValidateArrayOfArrays<T>(T[][] expected, object deserialized, string type)
        {
            var result = Assert.IsAssignableFrom<T[][]>(deserialized);  //" + type + " 嵌套数组在序列化往返后的类型错误"
            Assert.Equal(expected.Length, result.Length);  //" + type + " 嵌套数组在序列化往返后的长度错误"
            for (int i = 0; i < expected.Length; i++)
            {
                ValidateArray<T>(expected[i], result[i], "Array of " + type + "[" + i + "] ");
            }
        }

        private void ValidateArrayOfArrayOfArrays<T>(T[][][] expected, object deserialized, string type)
        {
            var result = Assert.IsAssignableFrom<T[][][]>(deserialized);  //" + type + " 嵌套数组在序列化往返后的类型错误"
            Assert.Equal(expected.Length, result.Length);  //" + type + " 嵌套数组在序列化往返后的长度错误"
            for (int i = 0; i < expected.Length; i++)
            {
                ValidateArrayOfArrays<T>(expected[i], result[i], "Array of " + type + "[" + i + "][]");
            }
        }

        [TestSuite("Functional")]
        [TestProvider("None")]
        [Fact, TestCategory("Functional")]
        public void Serialize_CircularReference()
        {
            var c1 = new CircularTest1();
            var c2 = new CircularTest2();
            c2.CircularTest1List.Add(c1);
            c1.CircularTest2 = c2;

            var deserialized = (CircularTest1)OrleansSerializationLoop(environment.Serializer, environment.DeepCopier, c1);
            var deserializedCircularTest2 = deserialized.CircularTest2;
            Assert.NotNull(deserializedCircularTest2);
            Assert.Equal(c2.CircularTest1List.Count, deserializedCircularTest2.CircularTest1List.Count);
            Assert.Same(deserialized, deserializedCircularTest2.CircularTest1List[0]);

            deserialized = (CircularTest1)OrleansSerializationLoop(environment.Serializer, environment.DeepCopier, c1, true);
            deserializedCircularTest2 = deserialized.CircularTest2;
            Assert.NotNull(deserializedCircularTest2);
            Assert.Equal(c2.CircularTest1List.Count, deserializedCircularTest2.CircularTest1List.Count);
            Assert.Same(deserialized, deserializedCircularTest2.CircularTest1List[0]);
        }

        [TestSuite("Functional")]
        [TestProvider("None")]
        [Fact, TestCategory("Functional")]
        public void Serialize_Enums()
        {
            var result = OrleansSerializationLoop(environment.Serializer, environment.DeepCopier, IntEnum.Value2);
            var typedResult = Assert.IsType<IntEnum>(result);
            Assert.Equal(IntEnum.Value2, typedResult);

            var result2 = OrleansSerializationLoop(environment.Serializer, environment.DeepCopier, UShortEnum.Value3);
            var typedResult2 = Assert.IsType<UShortEnum>(result2);
            Assert.Equal(UShortEnum.Value3, typedResult2);

            var test = new ClassWithEnumTestData { EnumValue = TestEnum.Third, Enemy = CampaignEnemyTestType.Enemy3 };
            var result3 = OrleansSerializationLoop(environment.Serializer, environment.DeepCopier, test);
            var typedResult3 = Assert.IsType<ClassWithEnumTestData>(result3);

            Assert.Equal(TestEnum.Third, typedResult3.EnumValue);
            Assert.Equal(CampaignEnemyTestType.Enemy3, typedResult3.Enemy);

            var result4 = OrleansSerializationLoop(environment.Serializer, environment.DeepCopier, CampaignEnemyType.Enemy3);
            var typedResult4 = Assert.IsType<CampaignEnemyType>(result4);
            Assert.Equal(CampaignEnemyType.Enemy3, typedResult4);
        }
    }
}

// ReSharper restore NotAccessedVariable
