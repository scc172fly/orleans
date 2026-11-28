using System.Globalization;
using Orleans.Runtime;
using Orleans.TestingHost.Utils;
using TestExtensions;
using TestGrainInterfaces;
using UnitTests.GrainInterfaces;
using UnitTests.Grains;
using Xunit;

namespace DefaultCluster.Tests.General
{
    /// <summary>
    /// 全面测试 Orleans 对泛型 Grain 和接口的支持。
    /// 验证 Orleans 能正确处理具有类型参数的 Grain 类和接口，
    /// 包括多个类型参数、继承层次结构、类型约束等复杂场景，
    /// 以及根据泛型类型实参进行正确的 Grain 激活和路由。
    /// </summary>
    [TestCategory("BVT"), TestCategory("Generics")]
    [TestSuite("BVT")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    public class GenericGrainTests : HostedTestClusterEnsureDefaultStarted
    {
        private static int grainId = 0;

        public GenericGrainTests(DefaultClusterFixture fixture) : base(fixture)
        {
        }

        public TGrainInterface GetGrain<TGrainInterface>(long i) where TGrainInterface : IGrainWithIntegerKey
        {
            return this.GrainFactory.GetGrain<TGrainInterface>(i);
        }

        public TGrainInterface GetGrain<TGrainInterface>() where TGrainInterface : IGrainWithIntegerKey
        {
            return this.GrainFactory.GetGrain<TGrainInterface>(GetRandomGrainId());
        }

        /// <summary>
        /// 测试 Orleans 能否实例化多个具体 Grain 类型，这些类型实现
        /// 同一泛型接口的不同具体化版本。验证特定类型的 Grain
        /// 实现能否根据泛型类型实参正确解析并激活。
        /// </summary>
        [Fact]
        public async Task GenericGrainTests_ConcreteGrainWithGenericInterfaceGetGrain()
        {
            var grainOfIntFloat1 = GetGrain<IGenericGrain<int, float>>();
            var grainOfIntFloat2 = GetGrain<IGenericGrain<int, float>>();
            var grainOfFloatString = GetGrain<IGenericGrain<float, string>>();

            await grainOfIntFloat1.SetT(123);
            await grainOfIntFloat2.SetT(456);
            await grainOfFloatString.SetT(789.0f);

            var floatResult1 = await grainOfIntFloat1.MapT2U();
            var floatResult2 = await grainOfIntFloat2.MapT2U();
            var stringResult = await grainOfFloatString.MapT2U();

            Assert.Equal(123f, floatResult1);
            Assert.Equal(456f, floatResult2);
            Assert.Equal("789", stringResult);
        }

        /// <summary>
        /// 验证 Grain 标识的保留：使用相同 ID 多次调用 GetGrain
        /// 会返回指向同一 Grain 激活实例的引用。这确保 Orleans 的单激活实例
        /// 保证对泛型 Grain 同样成立。
        /// </summary>
        [Fact]
        public async Task GenericGrainTests_ConcreteGrainWithGenericInterfaceMultiplicity()
        {
            var grainId = GetRandomGrainId();

            var grainRef1 = GetGrain<IGenericGrain<int, float>>(grainId);
            await grainRef1.SetT(123);

            var grainRef2 = GetGrain<IGenericGrain<int, float>>(grainId);
            var floatResult = await grainRef2.MapT2U();

            Assert.Equal(123f, floatResult);
        }

        /// <summary>
        /// 测试 Orleans 使用各种类型实参实例化具体泛型 Grain 的能力。
        /// 使用基于 Theory 的测试验证泛型 Grain 能正确处理不同的
        /// 基本类型（float、string），展示类型参数的灵活性。
        /// </summary>
        [Theory]
        [InlineData(1.2f)]
        [InlineData(3.4f)]
        [InlineData("5.6")]
        public async Task GenericGrainTests_SimpleGenericGrainGetGrain<T>(T setValue)
        {

            var grain = GetGrain<ISimpleGenericGrain<T>>();

            await grain.Set(setValue);

            // 泛型 Grain 实现不改变设置的值：
            await grain.Transform();

            T result = await grain.Get();

            Assert.Equal(setValue, result);
        }

        /// <summary>
        /// 测试使用数组作为类型实参的泛型 Grain。
        /// 验证 Orleans 能处理泛型 Grain 接口中数组等复杂类型实参。
        /// </summary>
        [Fact]
        public async Task GenericGrainTests_SimpleGenericGrainGetGrain_ArrayTypeParameter()
        {
            var grain = GetGrain<ISimpleGenericGrain<int[]>>();

            var expected = new[] { 1, 2, 3 };
            await grain.Set(expected);

            // 泛型 Grain 实现不改变设置的值：
            await grain.Transform();

            var result = await grain.Get();

            Assert.Equal(expected, result);
        }

        /// <summary>
        /// 测试通过继承使用数组的泛型 Grain。
        /// 验证 Orleans 对注册并管理基于数组的状态的泛型 Grain 的处理。
        /// </summary>
        [Fact]
        public async Task GenericGrainTests_GenericGrainInheritingArray()
        {
            var grain = GetGrain<IGenericArrayRegisterGrain<int>>();

            var expected = new[] { 1, 2, 3 };
            await grain.Set(expected);

            var result = await grain.Get();

            Assert.Equal(expected, result);
        }

        /// <summary>
        /// 测试实现带嵌套泛型类型参数的泛型接口的 Grain。
        /// 验证 Orleans 处理 List&lt;T&gt; 等复杂泛型类型实参的能力。
        /// </summary>
        [Fact]
        public async Task GenericGrainTests_GenericInterfaceWithGenericParametersGetGrain()
        {

            var grain = GetGrain<ISimpleGenericGrain<List<float>>>();
            var list = new List<float>();
            list.Add(0.1f);
            await grain.Set(list);

            var result = await grain.Get();

            Assert.Single(result);
            Assert.Equal(0.1f, result[0]);
        }


        /// <summary>
        /// 验证针对相同泛型 Grain 类型和 ID 多次调用 GetGrain
        /// 会返回同一激活实例。确保 Orleans 的单激活实例语义适用于泛型 Grain。
        /// </summary>
        [Fact]
        public async Task GenericGrainTests_SimpleGenericGrainMultiplicity()
        {
            var grainId = GetRandomGrainId();

            var grainRef1 = GetGrain<ISimpleGenericGrain<float>>(grainId);
            await grainRef1.Set(1.2f);
            await grainRef1.Transform();  // 泛型 Grain 类不执行任何操作

            var grainRef2 = GetGrain<ISimpleGenericGrain<float>>(grainId);
            var floatResult = await grainRef2.Get();

            Assert.Equal(1.2f, floatResult);
        }

        /// <summary>
        /// 测试 Orleans 的类型解析优先规则：当泛型接口同时存在具体实现
        /// 和泛型实现时，Orleans 应优先选择
        /// 更具体的实现。此测试验证正确的类型匹配优先级。
        /// </summary>
        [Fact]
        public async Task GenericGrainTests_PreferConcreteGrainImplementationOfGenericInterface()
        {
            var grainOfDouble1 = GetGrain<ISimpleGenericGrain<double>>();
            var grainOfDouble2 = GetGrain<ISimpleGenericGrain<double>>();

            await grainOfDouble1.Set(1.0);
            await grainOfDouble2.Set(2.0);

            // 具体实现（SpecializedSimpleGenericGrain）将设置的值乘以 2：
            await grainOfDouble1.Transform();
            await grainOfDouble2.Transform();

            var result1 = await grainOfDouble1.Get();
            var result2 = await grainOfDouble2.Get();

            Assert.Equal(2.0, result1);
            Assert.Equal(4.0, result2);
        }

        /// <summary>
        /// 验证 Orleans 优先选择具体实现时的激活实例标识。
        /// 确保对具体实现的优先选择不会破坏
        /// 单激活实例保证。
        /// </summary>
        [Fact]
        public async Task GenericGrainTests_PreferConcreteGrainImplementationOfGenericInterfaceMultiplicity()
        {
            var grainId = GetRandomGrainId();

            var grainRef1 = GetGrain<ISimpleGenericGrain<double>>(grainId);
            await grainRef1.Set(1.0);
            await grainRef1.Transform();  // SpecializedSimpleGenericGrain 将泛型 Grain 类的值乘以 2

            // 具有相同 ID 的第二个引用指向同一 Grain：
            var grainRef2 = GetGrain<ISimpleGenericGrain<double>>(grainId);
            await grainRef2.Transform();
            var floatResult = await grainRef2.Get();

            Assert.Equal(4.0f, floatResult);
        }

        /// <summary>
        /// 测试实现多个泛型接口的 Grain。
        /// 验证 Orleans 能正确处理具有复杂接口层次结构的 Grain，
        /// 其中涉及多个泛型接口。
        /// </summary>
        [Fact]
        public async Task GenericGrainTests_ConcreteGrainWithMultipleGenericInterfacesGetGrain()
        {
            var grain1 = GetGrain<ISimpleGenericGrain<int>>();
            var grain2 = GetGrain<ISimpleGenericGrain<int>>();

            await grain1.Set(1);
            await grain2.Set(2);

            // ConcreteGrainWith2GenericInterfaces 将设置的值乘以 10：
            await grain1.Transform();
            await grain2.Transform();

            var result1 = await grain1.Get();
            var result2 = await grain2.Get();

            Assert.Equal(10, result1);
            Assert.Equal(20, result2);
        }

        /// <summary>
        /// 验证具有多个接口的 Grain 的单激活实例语义。
        /// 测试使用同一接口反复调用 GetGrain 会返回同一激活实例，
        /// 即使该 Grain 实现了多个接口。
        /// </summary>
        [Fact]
        public async Task GenericGrainTests_ConcreteGrainWithMultipleGenericInterfacesMultiplicity1()
        {
            var grainId = GetRandomGrainId();

            var grainRef1 = GetGrain<ISimpleGenericGrain<int>>(grainId);
            await grainRef1.Set(1);

            // ConcreteGrainWith2GenericInterfaces 将设置的值乘以 10：
            await grainRef1.Transform();

            //该接口的第二个引用将指向同一 Grain
            var grainRef2 = GetGrain<ISimpleGenericGrain<int>>(grainId);
            await grainRef2.Transform();
            var floatResult = await grainRef2.Get();

            Assert.Equal(100, floatResult);
        }

        /// <summary>
        /// 测试不同接口引用之间的 Grain 标识。
        /// 验证通过同一 Grain 实现的不同接口访问它时，
        /// 仍指向同一底层激活实例，从而保持状态一致性。
        /// </summary>
        [Fact]
        public async Task GenericGrainTests_ConcreteGrainWithMultipleGenericInterfacesMultiplicity2()
        {
            var grainId = GetRandomGrainId();

            var grainRef1 = GetGrain<ISimpleGenericGrain<int>>(grainId);
            await grainRef1.Set(1);
            await grainRef1.Transform();  // ConcreteGrainWith2GenericInterfaces 将设置的值乘以 10：

            // 对 ConcreteGrainWith2GenericInterfaces 实现的另一个接口的第二个引用
            // 将引用同一 Grain：
            var grainRef2 = GetGrain<IGenericGrain<int, string>>(grainId);
            // ConcreteGrainWith2GenericInterfaces 返回当前值乘以 10 后的字符串表示形式：
            var floatResult = await grainRef2.MapT2U();

            Assert.Equal("100", floatResult);
        }

        /// <summary>
        /// 测试 Grain 能否使用 Grain 工厂创建对其他泛型 Grain 的引用。
        /// 验证 Grain 代码内的 Grain 工厂操作能否正确处理泛型类型。
        /// </summary>
        [Fact]
        public async Task GenericGrainTests_UseGenericFactoryInsideGrain()
        {
            var grainId = GetRandomGrainId();

            var grainRef1 = GetGrain<ISimpleGenericGrain<string>>(grainId);
            await grainRef1.Set("JustString");
            await grainRef1.CompareGrainReferences(grainRef1);
        }


        [Fact]
        public async Task Generic_SimpleGrain_GetGrain()
        {
            var grain = this.GrainFactory.GetGrain<ISimpleGenericGrain1<int>>(grainId++);
            await grain.GetA();
        }

        [Fact]
        public async Task Generic_SimpleGrainControlFlow()
        {
            var a = Random.Shared.Next(100);
            var b = a + 1;
            var expected = a + "x" + b;

            var grain = this.GrainFactory.GetGrain<ISimpleGenericGrain1<int>>(grainId++);

            await grain.SetA(a);

            await grain.SetB(b);

            Task<string> stringPromise = grain.GetAxB();
            Assert.Equal(expected, await stringPromise);
        }

        [Fact]
        public void Generic_SimpleGrainControlFlow_Blocking()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var a = Random.Shared.Next(100);
            var b = a + 1;
            var expected = a + "x" + b;

            var grain = this.GrainFactory.GetGrain<ISimpleGenericGrain1<int>>(grainId++);

            // 显式使用 .Wait() 和 .Result，以确保客户端在这些情况下不会死锁。
#pragma warning disable xUnit1031 // Do not use blocking task operations in test method
            grain.SetA(a).Wait(cancellationToken);

            grain.SetB(b).Wait(cancellationToken);

            Task<string> stringPromise = grain.GetAxB();
            Assert.Equal(expected, stringPromise.Result);
#pragma warning restore xUnit1031 // Do not use blocking task operations in test method
        }

        [Fact]
        public async Task Generic_SimpleGrainDataFlow()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var a = Random.Shared.Next(100);
            var b = a + 1;
            var expected = a + "x" + b;

            var grain = this.GrainFactory.GetGrain<ISimpleGenericGrain1<int>>(grainId++);

            var setAPromise = grain.SetA(a);
            var setBPromise = grain.SetB(b);
            var stringPromise = Task.WhenAll(setAPromise, setBPromise).ContinueWith((_) => grain.GetAxB(), cancellationToken).Unwrap();

            var x = await stringPromise;
            Assert.Equal(expected, x);
        }

        [Fact]
        public async Task Generic_SimpleGrain2_GetGrain()
        {
            var g1 = this.GrainFactory.GetGrain<ISimpleGenericGrain1<int>>(grainId++);
            var g2 = this.GrainFactory.GetGrain<ISimpleGenericGrainU<int>>(grainId++);
            var g3 = this.GrainFactory.GetGrain<ISimpleGenericGrain2<int, int>>(grainId++);
            await g1.GetA();
            await g2.GetA();
            await g3.GetA();
        }

        [Fact]
        public async Task Generic_SimpleGrainGenericParameterWithMultipleArguments_GetGrain()
        {
            var g1 = this.GrainFactory.GetGrain<ISimpleGenericGrain1<Dictionary<int, int>>>(GetRandomGrainId());
            await g1.GetA();
        }

        [Fact]
        public async Task Generic_SimpleGrainControlFlow2_GetAB()
        {
            var a = Random.Shared.Next(100);
            var b = a + 1;
            var expected = a + "x" + b;

            var g1 = this.GrainFactory.GetGrain<ISimpleGenericGrain1<int>>(grainId++);
            var g2 = this.GrainFactory.GetGrain<ISimpleGenericGrainU<int>>(grainId++);
            var g3 = this.GrainFactory.GetGrain<ISimpleGenericGrain2<int, int>>(grainId++);

            string r1 = await g1.GetAxB(a, b);
            string r2 = await g2.GetAxB(a, b);
            string r3 = await g3.GetAxB(a, b);
            Assert.Equal(expected, r1);
            Assert.Equal(expected, r2);
            Assert.Equal(expected, r3);
        }

        [Fact]
        public async Task Generic_SimpleGrainControlFlow3()
        {
            ISimpleGenericGrain2<int, float> g = this.GrainFactory.GetGrain<ISimpleGenericGrain2<int, float>>(grainId++);
            await g.SetA(3);
            await g.SetB(1.25f);
            Assert.Equal("3x1.25", await g.GetAxB());
        }

        [Fact]
        public async Task Generic_BasicGrainControlFlow()
        {
            IBasicGenericGrain<int, float> g = this.GrainFactory.GetGrain<IBasicGenericGrain<int, float>>(0);
            await g.SetA(3);
            await g.SetB(1.25f);
            Assert.Equal("3x1.25", await g.GetAxB());
        }

        [Fact]
        public async Task GrainWithListFields()
        {
            string a = Random.Shared.Next(100).ToString(CultureInfo.InvariantCulture);
            string b = Random.Shared.Next(100).ToString(CultureInfo.InvariantCulture);

            var g1 = this.GrainFactory.GetGrain<IGrainWithListFields>(grainId++);

            var p1 = g1.AddItem(a);
            var p2 = g1.AddItem(b);
            await Task.WhenAll(p1, p2);

            var r1 = await g1.GetItems();

            Assert.True(
                (a == r1[0] && b == r1[1]) || (b == r1[0] && a == r1[1]), // 消息顺序不一定得到保留。
                string.Format("Result: r[0]={0}, r[1]={1}", r1[0], r1[1]));
        }

        [Fact]
        public async Task Generic_GrainWithListFields()
        {
            int a = Random.Shared.Next(100);
            int b = Random.Shared.Next(100);


            var g1 = this.GrainFactory.GetGrain<IGenericGrainWithListFields<int>>(grainId++);

            var p1 = g1.AddItem(a);
            var p2 = g1.AddItem(b);
            await Task.WhenAll(p1, p2);

            var r1 = await g1.GetItems();

            Assert.True(
                (a == r1[0] && b == r1[1]) || (b == r1[0] && a == r1[1]), // 消息顺序不一定得到保留。
                string.Format("Result: r[0]={0}, r[1]={1}", r1[0], r1[1]));
        }

        [Fact]
        public async Task Generic_GrainWithNoProperties_ControlFlow()
        {
            int a = Random.Shared.Next(100);
            int b = Random.Shared.Next(100);
            string expected = a + "x" + b;

            var g1 = this.GrainFactory.GetGrain<IGenericGrainWithNoProperties<int>>(grainId++);

            string r1 = await g1.GetAxB(a, b);
            Assert.Equal(expected, r1);
        }

        [Fact]
        public async Task GrainWithNoProperties_ControlFlow()
        {
            int a = Random.Shared.Next(100);
            int b = Random.Shared.Next(100);
            string expected = a + "x" + b;

            long grainId = GetRandomGrainId();
            var g1 = this.GrainFactory.GetGrain<IGrainWithNoProperties>(grainId);

            string r1 = await g1.GetAxB(a, b);
            Assert.Equal(expected, r1);
        }

        [Fact]
        public async Task Generic_ReaderWriterGrain1()
        {
            int a = Random.Shared.Next(100);
            var g = this.GrainFactory.GetGrain<IGenericReaderWriterGrain1<int>>(grainId++);
            await g.SetValue(a);
            var res = await g.GetValue();
            Assert.Equal(a, res);
        }

        [Fact]
        public async Task Generic_ReaderWriterGrain2()
        {
            int a = Random.Shared.Next(100);
            string b = "bbbbb";

            var g = this.GrainFactory.GetGrain<IGenericReaderWriterGrain2<int, string>>(grainId++);
            await g.SetValue1(a);
            await g.SetValue2(b);
            var r1 = await g.GetValue1();
            Assert.Equal(a, r1);
            var r2 = await g.GetValue2();
            Assert.Equal(b, r2);
        }

        [Fact]
        public async Task Generic_ReaderWriterGrain3()
        {
            int a = Random.Shared.Next(100);
            string b = "bbbbb";
            double c = 3.145;

            var g = this.GrainFactory.GetGrain<IGenericReaderWriterGrain3<int, string, double>>(grainId++);
            await g.SetValue1(a);
            await g.SetValue2(b);
            await g.SetValue3(c);
            var r1 = await g.GetValue1();
            Assert.Equal(a, r1);
            var r2 = await g.GetValue2();
            Assert.Equal(b, r2);
            var r3 = await g.GetValue3();
            Assert.Equal(c, r3);
        }

        /// <summary>
        /// 测试以 Guid 和字节数组等非基本类型为实参的泛型 Grain。
        /// 验证 Orleans 能根据复杂泛型类型实参正确区分 Grain 激活实例，
        /// 确保按类型正确路由。
        /// </summary>
        [Fact]
        public async Task Generic_Non_Primitive_Type_Argument()
        {
            IEchoHubGrain<Guid, string> g1 = this.GrainFactory.GetGrain<IEchoHubGrain<Guid, string>>(1);
            IEchoHubGrain<Guid, int> g2 = this.GrainFactory.GetGrain<IEchoHubGrain<Guid, int>>(1);
            IEchoHubGrain<Guid, byte[]> g3 = this.GrainFactory.GetGrain<IEchoHubGrain<Guid, byte[]>>(1);

            Assert.NotEqual((GrainReference)g1, (GrainReference)g2);
            Assert.NotEqual((GrainReference)g1, (GrainReference)g3);
            Assert.NotEqual((GrainReference)g2, (GrainReference)g3);

            await g1.Foo(Guid.Empty, "", 1);
            await g2.Foo(Guid.Empty, 0, 2);
            await g3.Foo(Guid.Empty, new byte[] { }, 3);

            Assert.Equal(1, await g1.GetX());
            Assert.Equal(2, await g2.GetX());
            Assert.Equal(3m, await g3.GetX());
        }

        [Fact]
        public async Task Generic_Echo_Chain_1()
        {
            const string msg1 = "Hello from EchoGenericChainGrain-1";

            IEchoGenericChainGrain<string> g1 = this.GrainFactory.GetGrain<IEchoGenericChainGrain<string>>(GetRandomGrainId());

            string received = await g1.Echo(msg1);
            Assert.Equal(msg1, received);
        }

        [Fact]
        public async Task Generic_Echo_Chain_2()
        {
            const string msg2 = "Hello from EchoGenericChainGrain-2";

            IEchoGenericChainGrain<string> g2 = this.GrainFactory.GetGrain<IEchoGenericChainGrain<string>>(GetRandomGrainId());

            string received = await g2.Echo2(msg2);
            Assert.Equal(msg2, received);
        }

        [Fact]
        public async Task Generic_Echo_Chain_3()
        {
            const string msg3 = "Hello from EchoGenericChainGrain-3";

            IEchoGenericChainGrain<string> g3 = this.GrainFactory.GetGrain<IEchoGenericChainGrain<string>>(GetRandomGrainId());

            string received = await g3.Echo3(msg3);
            Assert.Equal(msg3, received);
        }

        [Fact]
        public async Task Generic_Echo_Chain_4()
        {
            const string msg4 = "Hello from EchoGenericChainGrain-4";

            var g4 = this.GrainFactory.GetGrain<IEchoGenericChainGrain<string>>(GetRandomGrainId());

            string received = await g4.Echo4(msg4);
            Assert.Equal(msg4, received);
        }

        [Fact]
        public async Task Generic_Echo_Chain_5()
        {
            const string msg5 = "Hello from EchoGenericChainGrain-5";

            var g5 = this.GrainFactory.GetGrain<IEchoGenericChainGrain<string>>(GetRandomGrainId());

            string received = await g5.Echo5(msg5);
            Assert.Equal(msg5, received);
        }

        [Fact]
        public async Task Generic_Echo_Chain_6()
        {
            const string msg6 = "Hello from EchoGenericChainGrain-6";

            var g6 = this.GrainFactory.GetGrain<IEchoGenericChainGrain<string>>(GetRandomGrainId());

            string received = await g6.Echo6(msg6);
            Assert.Equal(msg6, received);
        }


        [Fact]
        public async Task Generic_1Argument_GenericCallOnly()
        {
            var grain = this.GrainFactory.GetGrain<IGeneric1Argument<string>>(Guid.NewGuid(), "UnitTests.Grains.Generic1ArgumentGrain");
            var s1 = Guid.NewGuid().ToString();
            var s2 = await grain.Ping(s1);
            Assert.Equal(s1, s2);
        }

        [Fact]
        public void Generic_1Argument_NonGenericCallFirst()
        {
            Assert.Throws<ArgumentException>(() => this.GrainFactory.GetGrain<INonGenericBase>(Guid.NewGuid(), "UnitTests.Grains.Generic1ArgumentGrain"));
        }

        [Fact]
        public async Task Generic_1Argument_GenericCallFirst()
        {
            var id = Guid.NewGuid();
            var grain = this.GrainFactory.GetGrain<IGeneric1Argument<string>>(id, "UnitTests.Grains.Generic1ArgumentGrain");
            var s1 = Guid.NewGuid().ToString();
            var s2 = await grain.Ping(s1);
            Assert.Equal(s1, s2);
            Assert.Throws<ArgumentException>(() => this.GrainFactory.GetGrain<INonGenericBase>(id, "UnitTests.Grains.Generic1ArgumentGrain"));
        }

        /// <summary>
        /// 验证具有不同泛型类型实参的 Grain 会创建独立激活实例。
        /// 这对于确保 IDbGrain&lt;int&gt; 和 IDbGrain&lt;string&gt; 被视为
        /// 具有独立状态的完全不同的 Grain 类型至关重要。
        /// </summary>
        [Fact]
        public async Task DifferentTypeArgsProduceIndependentActivations()
        {
            var grain1 = this.GrainFactory.GetGrain<IDbGrain<int>>(0);
            await grain1.SetValue(123);

            var grain2 = this.GrainFactory.GetGrain<IDbGrain<string>>(0);
            var v = await grain2.GetValue();
            Assert.Null(v);
        }

        /// <summary>
        /// 测试泛型 Grain 调用自身。
        /// 验证自引用调用在泛型 Grain 上下文中正常工作。
        /// </summary>
        [Fact, TestCategory("Echo")]
        public async Task Generic_PingSelf()
        {
            var id = Guid.NewGuid();
            var grain = this.GrainFactory.GetGrain<IGenericPingSelf<string>>(id);
            var s1 = Guid.NewGuid().ToString();
            var s2 = await grain.PingSelf(s1);
            Assert.Equal(s1, s2);
        }

        /// <summary>
        /// 测试泛型 Grain 调用同一类型的其他泛型 Grain。
        /// 验证泛型 Grain 类型间的 Grain 到 Grain 通信。
        /// </summary>
        [Fact, TestCategory("Echo")]
        public async Task Generic_PingOther()
        {
            var id = Guid.NewGuid();
            var targetId = Guid.NewGuid();
            var grain = this.GrainFactory.GetGrain<IGenericPingSelf<string>>(id);
            var target = this.GrainFactory.GetGrain<IGenericPingSelf<string>>(targetId);
            var s1 = Guid.NewGuid().ToString();
            var s2 = await grain.PingOther(target, s1);
            Assert.Equal(s1, s2);
        }

        /// <summary>
        /// 测试泛型 Grain 通过另一个 Grain 调用自身的复杂调用链。
        /// 验证 Grain 引用在泛型 Grain 之间传递时仍然有效。
        /// </summary>
        [Fact, TestCategory("Echo")]
        public async Task Generic_PingSelfThroughOther()
        {
            var id = Guid.NewGuid();
            var targetId = Guid.NewGuid();
            var grain = this.GrainFactory.GetGrain<IGenericPingSelf<string>>(id);
            var target = this.GrainFactory.GetGrain<IGenericPingSelf<string>>(targetId);
            var s1 = Guid.NewGuid().ToString();
            var s2 = await grain.PingSelfThroughOther(target, s1);
            Assert.Equal(s1, s2);
        }

        /// <summary>
        /// 测试泛型 Grain 的计划操作及停用。
        /// 验证 Orleans 的激活生命周期管理能正确处理泛型 Grain，
        /// 包括延迟操作与显式停用。
        /// </summary>
        [Fact, TestCategory("ActivateDeactivate")]
        public async Task Generic_ScheduleDelayedPingAndDeactivate()
        {
            var id = Guid.NewGuid();
            var targetId = Guid.NewGuid();
            var grain = this.GrainFactory.GetGrain<IGenericPingSelf<string>>(id);
            var target = this.GrainFactory.GetGrain<IGenericPingSelf<string>>(targetId);
            var s1 = Guid.NewGuid().ToString();
            await grain.ScheduleDelayedPingToSelfAndDeactivate(target, s1, TimeSpan.FromSeconds(5));
            string? s2 = null;
            await TestingUtils.WaitUntilAsync(
                async (lastTry, cancellationToken) =>
                {
                    s2 = await grain.GetLastValue(cancellationToken);
                    if (s2 == s1)
                    {
                        return true;
                    }

                    if (lastTry)
                    {
                        Assert.Fail($"Expected delayed ping to set value '{s1}', but saw '{s2 ?? "null"}'.");
                    }

                    return false;
                },
                timeout: TimeSpan.FromSeconds(30),
                delayOnFail: TimeSpan.FromMilliseconds(200),
                cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(s1, s2);
        }

        /// <summary>
        /// 测试状态中包含循环引用的泛型 Grain 的序列化。
        /// 验证 Orleans 序列化系统在泛型 Grain 上下文中能处理复杂对象图，
        /// 而不会进入无限循环。
        /// </summary>
        [Fact, TestCategory("Serialization")]
        public async Task SerializationTests_Generic_CircularReferenceTest()
        {
            var grainId = Guid.NewGuid();
            var grain = this.GrainFactory.GetGrain<ICircularStateTestGrain>(primaryKey: grainId, keyExtension: grainId.ToString("N"));
            _ = await grain.GetState();
        }

        /// <summary>
        /// 测试带类型约束（where T : class, new() 等）的泛型 Grain。
        /// 验证 Orleans 能正确实施并处理 C# 泛型类型约束，
        /// 包括 unmanaged 约束及引用类型约束。
        /// </summary>
        [Fact]
        public async Task Generic_GrainWithTypeConstraints()
        {
            var grainId = Guid.NewGuid().ToString();
            var grain = this.GrainFactory.GetGrain<IGenericGrainWithConstraints<List<int>, int, string>>(grainId);
            var result = await grain.GetCount();
            Assert.Equal(0, result);
            await grain.Add(42);
            result = await grain.GetCount();
            Assert.Equal(1, result);

            var unmanagedGrain = this.GrainFactory.GetGrain<IUnmanagedArgGrain<DateTime>>(Guid.NewGuid());
            {
                var echoInput = DateTime.UtcNow;
                var echoOutput = await unmanagedGrain.Echo(echoInput);
                Assert.Equal(echoInput, echoOutput);
                echoOutput = await unmanagedGrain.EchoValue(echoInput);
                Assert.Equal(echoInput, echoOutput);
            }
            {
                var echoInput = "hello";
                var echoOutput = await unmanagedGrain.EchoNonNullable(echoInput);
                Assert.Equal(echoInput, echoOutput);
                echoOutput = await unmanagedGrain.EchoReference(echoInput);
                Assert.Equal(echoInput, echoOutput);
            }
        }

        /// <summary>
        /// 测试具有值类型持久化状态的泛型 Grain。
        /// 验证 Orleans 持久化系统能正确处理泛型 Grain 上下文中
        /// 用作 Grain 状态的值类型（结构体）。
        /// </summary>
        [Fact, TestCategory("Persistence")]
        public async Task Generic_GrainWithValueTypeState()
        {
            Guid id = Guid.NewGuid();
            var grain = this.GrainFactory.GetGrain<IValueTypeTestGrain>(id);

            var initial = await grain.GetStateData();
            Assert.Equal(new ValueTypeTestData(0), initial);

            var expectedValue = new ValueTypeTestData(42);

            await grain.SetStateData(expectedValue);

            Assert.Equal(expectedValue, await grain.GetStateData());
        }

        /// <summary>
        /// 测试激活后将非泛型 Grain 引用转换为泛型接口。
        /// 验证 Orleans 对已激活 Grain 的后期绑定接口发现的支持。
        /// </summary>
        [Fact, TestCategory("Cast")]
        public async Task Generic_CastToGenericInterfaceAfterActivation()
        {
            var grain = this.GrainFactory.GetGrain<INonGenericCastableGrain>(Guid.NewGuid());
            await grain.DoSomething(); //此处激活原始 Grain 类型

            var castRef = grain.AsReference<ISomeGenericGrain<string>>();

            var result = await castRef.Hello();

            Assert.Equal("Hello!", result);
        }

        /// <summary>
        /// 测试激活前转换为具有不同类型实参的泛型接口。
        /// 验证 Orleans 能处理 Grain 激活之前涉及不同泛型类型
        /// 具体化版本的接口转换。
        /// </summary>
        [Fact, TestCategory("Cast")]
        public async Task Generic_CastToDifferentlyConcretizedGenericInterfaceBeforeActivation()
        {
            var grain = this.GrainFactory.GetGrain<INonGenericCastableGrain>(Guid.NewGuid());

            var castRef = grain.AsReference<IIndependentlyConcretizedGenericGrain<string>>();

            var result = await castRef.Hello();

            Assert.Equal("Hello!", result);
        }

        /// <summary>
        /// 测试独立具体化的泛型接口之间的转换。
        /// 验证 Orleans 处理复杂接口层次结构的能力，其中接口
        /// 独立于 Grain 的泛型参数进行具体化。
        /// </summary>
        [Fact, TestCategory("Cast")]
        public async Task Generic_CastToDifferentlyConcretizedInterfaceBeforeActivation()
        {
            var grain = this.GrainFactory.GetGrain<INonGenericCastableGrain>(Guid.NewGuid());

            var castRef = grain.AsReference<IIndependentlyConcretizedGrain>();

            var result = await castRef.Hello();

            Assert.Equal("Hello!", result);
        }

        /// <summary>
        /// 测试从泛型接口转换为非泛型接口。
        /// 验证 Orleans 支持跨泛型与非泛型边界的接口转换。
        /// </summary>
        [Fact, TestCategory("Cast")]
        public async Task Generic_CastGenericInterfaceToNonGenericInterfaceBeforeActivation()
        {
            var grain = this.GrainFactory.GetGrain<IGenericCastableGrain<string>>(Guid.NewGuid());

            var castRef = grain.AsReference<INonGenericCastGrain>();

            var result = await castRef.Hello();

            Assert.Equal("Hello!", result);
        }

        /// <summary>
        /// 测试泛型 Grain 能否拥有使用不同类型参数的泛型状态。
        /// 验证 Grain&lt;TState&gt; 的类型参数不必与 Grain 自身的
        /// 类型参数匹配，从而允许在泛型 Grain 实现中灵活建模状态。
        /// </summary>
        [Fact]
        public async Task GenericGrainStateParameterMismatchTest()
        {
            var grain = this.GrainFactory.GetGrain<IGenericGrainWithGenericState<int, List<Guid>, string>>(Guid.NewGuid());
            var result = await grain.GetStateType();
            Assert.Equal(typeof(List<Guid>), result);
        }
    }

    namespace Generic.EdgeCases
    {
        using UnitTests.GrainInterfaces.Generic.EdgeCases;
        using UnitTests.Grains.Generic.EdgeCases;

        /// <summary>
        /// 测试 Orleans 泛型 Grain 类型系统的边界情况。
        /// 验证复杂场景，包括类型的部分具体化、重复类型参数、
        /// 重新排列的泛型实参、继承层次结构中的类型参数约束，
        /// 以及挑战泛型类型解析器能力边界的高级类型推断场景。
        /// </summary>
        [TestCategory("BVT"), TestCategory("Generics")]
        [TestSuite("BVT")]
        [TestProvider("None")]
        [TestArea("Runtime")]
        public class GenericEdgeCaseTests : HostedTestClusterEnsureDefaultStarted
        {
            public GenericEdgeCaseTests(DefaultClusterFixture fixture) : base(fixture)
            {
            }

            private static async Task<Type?[]> GetConcreteGenArgs(IBasicGrain @this)
            {
                var genArgTypeNames = await @this.ConcreteGenArgTypeNames();
                return genArgTypeNames.Select(Type.GetType).ToArray();
            }

            /// <summary>
            /// 测试泛型 Grain 类型的部分具体化。
            /// 验证 Orleans 处理部分指定泛型参数的 Grain 的能力，
            /// 其余参数由接口实现推断。
            /// 注意：目前不支持此功能。
            /// </summary>
            [Fact(Skip = "Currently unsupported")]
            public async Task Generic_PartiallySpecifyingGenericGrainFulfilsInterface()
            {
                var grain = this.GrainFactory.GetGrain<IGrainWithTwoGenArgs<string, int>>(Guid.NewGuid());
                var concreteGenArgs = await GetConcreteGenArgs(grain);
                Assert.True(concreteGenArgs.SequenceEqual(new[] { typeof(int) }));
            }

            /// <summary>
            /// 测试多次使用同一泛型参数的 Grain。
            /// 验证对单个类型参数在 Grain 接口层次结构中
            /// 多次出现的场景的处理。
            /// 注意：当前不支持，会因泛型实参过多而失败。
            /// </summary>
            [Fact(Skip = "Currently unsupported")]
            public async Task Generic_GenericGrainCanReuseOwnGenArgRepeatedly()
            {
                // 解析正确，但无法激活：为具体类提供了过多泛型实参
                var grain = this.GrainFactory.GetGrain<IGrainReceivingRepeatedGenArgs<int, int>>(Guid.NewGuid());
                var concreteGenArgs = await GetConcreteGenArgs(grain);
                Assert.True(concreteGenArgs.SequenceEqual(new[] { typeof(int) }));
            }

            /// <summary>
            /// 测试部分指定的泛型接口的转换。
            /// 验证部分具体化的泛型接口能够
            /// 转换为其完全指定的对应接口。
            /// </summary>
            [Fact]
            public async Task Generic_PartiallySpecifyingGenericInterfaceIsCastable()
            {
                var grain = this.GrainFactory.GetGrain<IPartiallySpecifyingInterface<string>>(Guid.NewGuid());
                await grain.Hello();
                var castRef = grain.AsReference<IGrainWithTwoGenArgs<string, int>>();
                var response = await castRef.Hello();
                Assert.Equal("Hello!", response);
            }

            /// <summary>
            /// 测试激活前进行部分泛型具体化的转换。
            /// 验证即使 Grain 尚未激活，
            /// 转换机制仍能正确工作。
            /// </summary>
            [Fact]
            public async Task Generic_PartiallySpecifyingGenericInterfaceIsCastable_Activating()
            {
                var grain = this.GrainFactory.GetGrain<IPartiallySpecifyingInterface<string>>(Guid.NewGuid());
                var castRef = grain.AsReference<IGrainWithTwoGenArgs<string, int>>();
                var response = await castRef.Hello();
                Assert.Equal("Hello!", response);
            }


            /// <summary>
            /// 测试既重复又重新排列的泛型实参解析。
            /// 验证复杂的类型参数映射，其中实参多次出现在
            /// 不同位置。
            /// 注意：当前不支持，类型推断失败。
            /// </summary>
            [Fact(Skip = "Currently unsupported")]
            public async Task Generic_RepeatedRearrangedGenArgsResolved()
            {
                // 仍能解析为正确的泛型类型定义，但由于实参过多，激活时失败
                // 未能从匹配的具体类型中正确推断泛型实参
                var grain = this.GrainFactory.GetGrain<IReceivingRepeatedGenArgsAmongstOthers<int, string, int>>(Guid.NewGuid());
                var concreteGenArgs = await GetConcreteGenArgs(grain);
                Assert.True(concreteGenArgs.SequenceEqual(new[] { typeof(string), typeof(int) }));
            }

            /// <summary>
            /// 测试跨接口的重复泛型实参的类型解析。
            /// 验证 Orleans 能正确处理同一类型参数在层次结构的
            /// 不同接口中多次使用的场景。
            /// </summary>
            [Fact]
            public async Task Generic_RepeatedGenArgsWorkAmongstInterfacesInTypeResolution()
            {
                var grain = this.GrainFactory.GetGrain<IReceivingRepeatedGenArgsFromOtherInterface<bool, bool, bool>>(Guid.NewGuid());
                var concreteGenArgs = await GetConcreteGenArgs(grain);
                Assert.True(concreteGenArgs.SequenceEqual(Enumerable.Empty<Type>()));
            }

            /// <summary>
            /// 测试具有重复泛型实参的接口之间的转换。
            /// 验证 Grain 可以在定义中多次使用同一类型参数的
            /// 接口之间转换。
            /// </summary>
            [Fact]
            public async Task Generic_RepeatedGenArgsWorkAmongstInterfacesInCasting()
            {
                var grain = this.GrainFactory.GetGrain<IReceivingRepeatedGenArgsFromOtherInterface<bool, bool, bool>>(Guid.NewGuid());
                await grain.Hello();
                var castRef = grain.AsReference<ISpecifyingGenArgsRepeatedlyToParentInterface<bool>>();
                var response = await castRef.Hello();
                Assert.Equal("Hello!", response);
            }

            /// <summary>
            /// 测试激活前使用重复泛型实参进行转换。
            /// 验证具有重复类型参数的复杂泛型层次结构的
            /// 激活前转换行为。
            /// </summary>
            [Fact]
            public async Task Generic_RepeatedGenArgsWorkAmongstInterfacesInCasting_Activating()
            {
                var grain = this.GrainFactory.GetGrain<IReceivingRepeatedGenArgsFromOtherInterface<bool, bool, bool>>(Guid.NewGuid());
                var castRef = grain.AsReference<ISpecifyingGenArgsRepeatedlyToParentInterface<bool>>();
                var response = await castRef.Hello();
                Assert.Equal("Hello!", response);
            }

            /// <summary>
            /// 测试重新排列的泛型实参的解析。
            /// 验证 Orleans 能处理泛型参数的出现顺序不同于 Grain 实现中顺序的
            /// 接口。
            /// 注意：目前不支持此功能。
            /// </summary>
            [Fact(Skip = "Currently unsupported")]
            public async Task Generic_RearrangedGenArgsOfCorrectArityAreResolved()
            {
                var grain = this.GrainFactory.GetGrain<IReceivingRearrangedGenArgs<int, long>>(Guid.NewGuid());
                var concreteGenArgs = await GetConcreteGenArgs(grain);
                Assert.True(concreteGenArgs.SequenceEqual(new[] { typeof(long), typeof(int) }));
            }

            /// <summary>
            /// 测试具有重新排列的泛型实参的接口之间的转换。
            /// 验证泛型参数处于不同位置但总数相同时，
            /// 转换仍能正常工作。
            /// </summary>
            [Fact]
            public async Task Generic_RearrangedGenArgsOfCorrectNumberAreCastable()
            {
                var grain = this.GrainFactory.GetGrain<ISpecifyingRearrangedGenArgsToParentInterface<int, long>>(Guid.NewGuid());
                await grain.Hello();
                var castRef = grain.AsReference<IReceivingRearrangedGenArgsViaCast<long, int>>();
                var response = await castRef.Hello();
                Assert.Equal("Hello!", response);
            }

            /// <summary>
            /// 测试使用重新排列的泛型实参进行激活前转换。
            /// 验证在 Grain 激活前，参数重排不会破坏
            /// 转换。
            /// </summary>
            [Fact]
            public async Task Generic_RearrangedGenArgsOfCorrectNumberAreCastable_Activating()
            {
                var grain = this.GrainFactory.GetGrain<ISpecifyingRearrangedGenArgsToParentInterface<int, long>>(Guid.NewGuid());
                var castRef = grain.AsReference<IReceivingRearrangedGenArgsViaCast<long, int>>();
                var response = await castRef.Hello();
                Assert.Equal("Hello!", response);
            }

            public interface IFullySpecifiedGenericInterface<T> : IBasicGrain
            { }

            public interface IDerivedFromMultipleSpecializationsOfSameInterface : IFullySpecifiedGenericInterface<int>, IFullySpecifiedGenericInterface<long>
            { }

            public class GrainFulfillingMultipleSpecializationsOfSameInterfaceViaIntermediate : BasicGrain, IDerivedFromMultipleSpecializationsOfSameInterface
            { }

            /// <summary>
            /// 测试同一泛型接口的多个具体化版本之间的转换。
            /// 验证实现同一泛型接口多个具体版本的 Grain
            /// 能在这些具体化版本之间转换。
            /// </summary>
            [Fact]
            public async Task CastingBetweenFullySpecifiedGenericInterfaces()
            {
                var grain = this.GrainFactory.GetGrain<IDerivedFromMultipleSpecializationsOfSameInterface>(Guid.NewGuid());
                await grain.Hello();
                var castRef = grain.AsReference<IFullySpecifiedGenericInterface<int>>();
                await castRef.Hello();
                var castRef2 = castRef.AsReference<IFullySpecifiedGenericInterface<long>>();
                await castRef2.Hello();
            }

            /// <summary>
            /// 测试转换为带无关泛型参数的接口。
            /// 验证 Grain 可以转换为泛型参数与 Grain 的具体类型实参
            /// 无关的接口。
            /// </summary>
            [Fact]
            public async Task Generic_CanCastToFullySpecifiedInterfaceUnrelatedToConcreteGenArgs()
            {
                var grain = this.GrainFactory.GetGrain<IArbitraryInterface<int, long>>(Guid.NewGuid());
                await grain.Hello();
                _ = grain.AsReference<IInterfaceUnrelatedToConcreteGenArgs<float>>();
                var response = await grain.Hello();
                Assert.Equal("Hello!", response);
            }


            /// <summary>
            /// 测试激活前转换为无关的泛型接口。
            /// 验证转换为带独立泛型参数的接口在 Grain 激活之前
            /// 即可正常工作。
            /// </summary>
            [Fact]
            public async Task Generic_CanCastToFullySpecifiedInterfaceUnrelatedToConcreteGenArgs_Activating()
            {
                var grain = this.GrainFactory.GetGrain<IArbitraryInterface<int, long>>(Guid.NewGuid());
                _ = grain.AsReference<IInterfaceUnrelatedToConcreteGenArgs<float>>();
                var response = await grain.Hello();
                Assert.Equal("Hello!", response);
            }

            /// <summary>
            /// 测试对已经是泛型的类型实参进一步具体化。
            /// 验证 List&lt;T&gt; 等嵌套泛型的处理，其中 T 本身是
            /// 待解析的类型参数。
            /// 注意：目前不支持此功能。
            /// </summary>
            [Fact(Skip = "Currently unsupported")]
            public async Task Generic_GenArgsCanBeFurtherSpecialized()
            {
                var grain = this.GrainFactory.GetGrain<IInterfaceTakingFurtherSpecializedGenArg<List<int>>>(Guid.NewGuid());
                var concreteGenArgs = await GetConcreteGenArgs(grain);
                Assert.True(concreteGenArgs.SequenceEqual(new[] { typeof(int) }));
            }

            /// <summary>
            /// 测试将泛型参数具体化为数组类型。
            /// 验证 Orleans 能处理泛型参数具体化为数组类型（T[]）的
            /// 场景。
            /// 注意：目前不支持此功能。
            /// </summary>
            [Fact(Skip = "Currently unsupported")]
            public async Task Generic_GenArgsCanBeFurtherSpecializedIntoArrays()
            {
                var grain = this.GrainFactory.GetGrain<IInterfaceTakingFurtherSpecializedGenArg<long[]>>(Guid.NewGuid());
                var concreteGenArgs = await GetConcreteGenArgs(grain);
                Assert.True(concreteGenArgs.SequenceEqual(new[] { typeof(long) }));
            }

            /// <summary>
            /// 测试带嵌套泛型具体化版本的接口之间的转换。
            /// 验证接口使用 List&lt;T&gt; 等复杂泛型类型且具有不同但兼容的
            /// 具体化版本时的转换。
            /// 注意：目前不支持此功能。
            /// </summary>
            [Fact(Skip = "Currently unsupported")]
            public async Task Generic_CanCastBetweenInterfacesWithFurtherSpecializedGenArgs()
            {
                var grain = this.GrainFactory.GetGrain<IAnotherReceivingFurtherSpecializedGenArg<List<int>>>(Guid.NewGuid());
                await grain.Hello();
                _ = grain.AsReference<IYetOneMoreReceivingFurtherSpecializedGenArg<int[]>>();
                var response = await grain.Hello();
                Assert.Equal("Hello!", response);
            }

            /// <summary>
            /// 测试带嵌套泛型具体化版本的激活前转换。
            /// 验证复杂泛型转换场景在 Grain 激活之前即可正常工作。
            /// 注意：目前不支持此功能。
            /// </summary>
            [Fact(Skip = "Currently unsupported")]
            public async Task Generic_CanCastBetweenInterfacesWithFurtherSpecializedGenArgs_Activating()
            {
                var grain = this.GrainFactory.GetGrain<IAnotherReceivingFurtherSpecializedGenArg<List<int>>>(Guid.NewGuid());
                _ = grain.AsReference<IYetOneMoreReceivingFurtherSpecializedGenArg<int[]>>();

                var response = await grain.Hello();

                Assert.Equal("Hello!", response);
            }
        }
    }
}
