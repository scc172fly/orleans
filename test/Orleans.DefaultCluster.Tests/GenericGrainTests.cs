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
    /// 用于 Orleans支持通用 grains 和接口的全面测试。
    /// 验证 Orleans 能够正确处理 grain 类和与类型参数的接口,
    /// 包括复杂情景,如多类型参数、继承等级、类型限制、
    /// 和适当的 grain 激活/路由,基于通用类型参数。
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
        /// Orleans 能够即时反转多混凝解 grain 类型执行不同
        /// 同一通用界面的相同通用界面的专业化。 grain
        /// 执行程序根据通用类型参数得到正确解决和启动。
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
        /// 验证 grain 身份保存:多个 GetGrain 调用同一ID
        /// 返回同一 grain 激活的引用。这将确保 Orleans'单启动
        /// 通用 grains担保持有。
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
        /// 测试 Orleans' 具有用各种类型参数对通用 grain 专业进行即时处理的能力。
        /// 使用基于理论的测试来验证通用 grains 正确与不同
        /// 原始类型(浮点、字符串),显示型号参数灵活性。
        /// </summary>
        [Theory]
        [InlineData(1.2f)]
        [InlineData(3.4f)]
        [InlineData("5.6")]
        public async Task GenericGrainTests_SimpleGenericGrainGetGrain<T>(T setValue)
        {

            var grain = GetGrain<ISimpleGenericGrain<T>>();

            await grain.Set(setValue);

            // 通用 grain 执行不改变设定值:
            await grain.Transform();

            T result = await grain.Get();

            Assert.Equal(setValue, result);
        }

        /// <summary>
        /// 测试具有数组类型参数的通用 grains 。
        /// 验证 Orleans 可以处理复杂的类型参数, 如通用 grain 接口中的数组。
        /// </summary>
        [Fact]
        public async Task GenericGrainTests_SimpleGenericGrainGetGrain_ArrayTypeParameter()
        {
            var grain = GetGrain<ISimpleGenericGrain<int[]>>();

            var expected = new[] { 1, 2, 3 };
            await grain.Set(expected);

            // 通用 grain 执行不改变设定值:
            await grain.Transform();

            var result = await grain.Get();

            Assert.Equal(expected, result);
        }

        /// <summary>
        /// 测试通过继承方式与阵列一起工作的通用 grains 。
        /// 验证 Orleans处理登记和管理基于阵列状态的通用 grains 。
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
        /// 测试 grains 采用嵌套通用类型参数的通用界面。
        /// 验证 Orleans处理复杂通用情景的能力,例如将清单<T> 作为类型参数。
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
        /// 验证多个 GetGrain 要求的通用 grain 类型和ID
        /// 返回相同的激活。确保 Orleans的单动词义适用于通用 grains。
        /// </summary>
        [Fact]
        public async Task GenericGrainTests_SimpleGenericGrainMultiplicity()
        {
            var grainId = GetRandomGrainId();

            var grainRef1 = GetGrain<ISimpleGenericGrain<float>>(grainId);
            await grainRef1.Set(1.2f);
            await grainRef1.Transform();  // 通用 grain 类的NOP

            var grainRef2 = GetGrain<ISimpleGenericGrain<float>>(grainId);
            var floatResult = await grainRef2.Get();

            Assert.Equal(1.2f, floatResult);
        }

        /// <summary>
        /// 测试 Orleans' 型号分辨率偏重规则:当两者都具体实施时
        /// 通用界面一般实施, Orleans 应更倾向于
        /// 具体实施。 这验证了合适的类型匹配优先 。
        /// </summary>
        [Fact]
        public async Task GenericGrainTests_PreferConcreteGrainImplementationOfGenericInterface()
        {
            var grainOfDouble1 = GetGrain<ISimpleGenericGrain<double>>();
            var grainOfDouble2 = GetGrain<ISimpleGenericGrain<double>>();

            await grainOfDouble1.Set(1.0);
            await grainOfDouble2.Set(2.0);

            // 具体执行(SpecializedSimpleGenericGrain)使设定值翻倍:
            await grainOfDouble1.Transform();
            await grainOfDouble2.Transform();

            var result1 = await grainOfDouble1.Get();
            var result2 = await grainOfDouble2.Get();

            Assert.Equal(2.0, result1);
            Assert.Equal(4.0, result2);
        }

        /// <summary>
        /// 当 Orleans 选择具体实施时验证激活身份。
        /// 确保对具体实施的重视不会打破
        /// 单项活动保障。
        /// </summary>
        [Fact]
        public async Task GenericGrainTests_PreferConcreteGrainImplementationOfGenericInterfaceMultiplicity()
        {
            var grainId = GetRandomGrainId();

            var grainRef1 = GetGrain<ISimpleGenericGrain<double>>(grainId);
            await grainRef1.Set(1.0);
            await grainRef1.Transform();  // SpecializedSimpleGenericGrain 将通用 grain 类的值加倍

            // 以同一id点的第二处引用相同 grain:
            var grainRef2 = GetGrain<ISimpleGenericGrain<double>>(grainId);
            await grainRef2.Transform();
            var floatResult = await grainRef2.Get();

            Assert.Equal(4.0f, floatResult);
        }

        /// <summary>
        /// 测试 grains ,安装多通用界面。
        /// 验证 Orleans 正确处理 grains 且界面结构复杂
        /// 涉及多个通用界面。
        /// </summary>
        [Fact]
        public async Task GenericGrainTests_ConcreteGrainWithMultipleGenericInterfacesGetGrain()
        {
            var grain1 = GetGrain<ISimpleGenericGrain<int>>();
            var grain2 = GetGrain<ISimpleGenericGrain<int>>();

            await grain1.Set(1);
            await grain2.Set(2);

            // ConcreteGrainWith2GenericInterfaces 将设定值乘以 10:
            await grain1.Transform();
            await grain2.Transform();

            var result1 = await grain1.Get();
            var result2 = await grain2.Get();

            Assert.Equal(10, result1);
            Assert.Equal(20, result2);
        }

        /// <summary>
        /// 校验 grains 多个接口的单动静语义。
        /// 重复使用相同界面的 GetGrain 调用返回相同的激活
        /// 即使 grain 安装多个接口。
        /// </summary>
        [Fact]
        public async Task GenericGrainTests_ConcreteGrainWithMultipleGenericInterfacesMultiplicity1()
        {
            var grainId = GetRandomGrainId();

            var grainRef1 = GetGrain<ISimpleGenericGrain<int>>(grainId);
            await grainRef1.Set(1);

            // ConcreteGrainWith2GenericInterfaces 将设定值乘以 10:
            await grainRef1.Transform();

            //第二次引用界面将指同一 grain
            var grainRef2 = GetGrain<ISimpleGenericGrain<int>>(grainId);
            await grainRef2.Transform();
            var floatResult = await grainRef2.Get();

            Assert.Equal(100, floatResult);
        }

        /// <summary>
        /// 在不同界面引用中测试 grain 身份。
        /// 校验它通过所执行的不同接口访问相同的 grain
        /// 仍然引用相同的启动基础,保持州的一致性。
        /// </summary>
        [Fact]
        public async Task GenericGrainTests_ConcreteGrainWithMultipleGenericInterfacesMultiplicity2()
        {
            var grainId = GetRandomGrainId();

            var grainRef1 = GetGrain<ISimpleGenericGrain<int>>(grainId);
            await grainRef1.Set(1);
            await grainRef1.Transform();  // ConcreteGrainWith2GenericInterfaces 将设定值乘以 10:

            // 第二处提及 ConcreteGrainWith2GenericInterfaces所执行的不同接口
            // 将引用相同的 grain:
            var grainRef2 = GetGrain<IGenericGrain<int, string>>(grainId);
            // ConcreteGrainWith2GenericInterfaces 返回当前值乘以 10 的字符串表示法:
            var floatResult = await grainRef2.MapT2U();

            Assert.Equal("100", floatResult);
        }

        /// <summary>
        /// 测试 grains 使用 grain 用于创建引用其它通用 grains.
        /// 证实 grain 工厂作业在通用型号 grain 代码内正确工作。
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

            // 明确使用. Wait()和.Result,以确保客户在这些情况下不会陷入僵局。
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
                (a == r1[0] && b == r1[1]) || (b == r1[0] && a == r1[1]), // 电文订购不一定保存。
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
                (a == r1[0] && b == r1[1]) || (b == r1[0] && a == r1[1]), // 电文订购不一定保存。
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
        /// 测试通用 grains ,使用非原始类型参数,如“吉德”和字节阵列。
        /// 校验 Orleans 正确区分基于复杂特性的 grain 激活
        /// 通用类型参数,确保适当的类型路径。
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
        /// 验证 grains 使用不同通用类型参数创建独立启动。
        /// 这对于确保 IDbGrain<int> 和 IDbGrain<string> 作为
        /// 完全独立的 grain 类型,具有独立状态。
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
        /// 测试自呼自呼的通用 grains 。
        /// 在通用 grain 背景下验证自特调用是否正确。
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
        /// 测试通用 grains 调用同类的其他通用 grains 。
        /// 验证值 grain- 来...grain 通用信息交换 grain 类型。
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
        /// 测试通用 grain 通过另一个 grain调用链的复合调用链。
        /// 验证 grain 通用 grains.
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
        /// 测试通用 grains的预定操作和停用。
        /// 证明 Orleans的活化生命周期管理与通用 grains的正确工作有效,
        /// 包括延迟运行和明确解除功能。
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
        /// 测试通用 grains 序列,并按其状态提供循环参考。
        /// 验证 Orleans的序列化系统能够处理复杂的对象图
        /// 在通用 grain 上下文中,没有无限环。
        /// </summary>
        [Fact, TestCategory("Serialization")]
        public async Task SerializationTests_Generic_CircularReferenceTest()
        {
            var grainId = Guid.NewGuid();
            var grain = this.GrainFactory.GetGrain<ICircularStateTestGrain>(primaryKey: grainId, keyExtension: grainId.ToString("N"));
            _ = await grain.GetState();
        }

        /// <summary>
        /// 测试具有类型限制的通用 grains (T:类、新()等)。
        /// 证明 Orleans 正确执行和处理C#通用类型限制,
        /// 包括未加管理的制约和参考类型制约。
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
        /// 测试具有数值类型状态持久性的通用 grains 。
        /// 验证 Orleans'持久性系统正确处理价值类型(结构)
        /// 年为 grain 状态, 通用 grain 环境。
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
        /// 测试在激活后将非通用 grain 引文用于通用界面。
        /// 验证 Orleans对已激活的 grains上延迟关闭接口发现的支持。
        /// </summary>
        [Fact, TestCategory("Cast")]
        public async Task Generic_CastToGenericInterfaceAfterActivation()
        {
            var grain = this.GrainFactory.GetGrain<INonGenericCastableGrain>(Guid.NewGuid());
            await grain.DoSomething(); //在此激活原始 grain 类型

            var castRef = grain.AsReference<ISomeGenericGrain<string>>();

            var result = await castRef.Hello();

            Assert.Equal("Hello!", result);
        }

        /// <summary>
        /// 激活前测试会选择与不同类型参数的通用界面。
        /// 验证 Orleans 能够处理涉及不同通用的界面
        /// 激活 grain 之前的特性类型。
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
        /// 独立具体化通用界面之间的测试。
        /// 校验 Orleans在接口位置处理复杂界面等级的能力
        /// 独立于 grain通用参数之外的专门化。
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
        /// 从通用和非基因界面进行筛选试验。
        /// 验证 Orleans 支持跨越通用/非遗传边界的界面。
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
        /// 通用 grains 具有不同类型参数的通用状态的测试。
        /// 验证 Grain<TState> 的类型参数不需要与 grain的
        /// 允许在通用 grain 执行中采用灵活状态模型。
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
        /// 在 Orleans通用 grain 类型系统中测试边缘情况。
        /// 验证复杂假设情况,包括部分类型专业化、重复型号参数、
        /// 重新排列的通用参数,继承等级制度中的类型参数限制,
        /// 和推动通用类型解析器边界的高级类型的推断假设情景。
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
            /// 测试通用 grain 类型的一部分专业化。
            /// 验证 Orleans处理部分指定通用参数的 grains 能力,
            /// 还有一些留待从接口实施中推断。
            /// 注:目前未支持的特性 。
            /// </summary>
            [Fact(Skip = "Currently unsupported")]
            public async Task Generic_PartiallySpecifyingGenericGrainFulfilsInterface()
            {
                var grain = this.GrainFactory.GetGrain<IGrainWithTwoGenArgs<string, int>>(Guid.NewGuid());
                var concreteGenArgs = await GetConcreteGenArgs(grain);
                Assert.True(concreteGenArgs.SequenceEqual(new[] { typeof(int) }));
            }

            /// <summary>
            /// 测试 grains 重复使用同一通用参数多次。
            /// 验证单类型参数出现时的设想情景处理
            /// 在 grain界面结构中多次。
            /// 注:目前未支持 - 以太多的通用参数失败 。
            /// </summary>
            [Fact(Skip = "Currently unsupported")]
            public async Task Generic_GenericGrainCanReuseOwnGenArgRepeatedly()
            {
                // 正确解析正解, 但无法被激活: 混凝土类的 gen args 数量太多
                var grain = this.GrainFactory.GetGrain<IGrainReceivingRepeatedGenArgs<int, int>>(Guid.NewGuid());
                var concreteGenArgs = await GetConcreteGenArgs(grain);
                Assert.True(concreteGenArgs.SequenceEqual(new[] { typeof(int) }));
            }

            /// <summary>
            /// 带有部分指定的通用界面的测试铸造。
            /// 验证与部分通用专门化界面的
            /// 将其抛到完全指定的对应方身上。
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
            /// 启动前进行部分通用专业的铸造试验。
            /// 证明铸造机制即使在 grain时仍然正确运转
            /// 还没启动
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
            /// 反复重复和重新安排的通用论点的测试解析。
            /// 验证参数出现多次的复杂类型参数映射
            /// 职位不同。
            /// 注:目前未支持 - 类型推理失败 。
            /// </summary>
            [Fact(Skip = "Currently unsupported")]
            public async Task Generic_RepeatedRearrangedGenArgsResolved()
            {
                // 再次下定决心采用正确的通用类型定义,但未能像太多的参数那样启动
                // gen ars 并没有被正确推断出匹配的混凝土类型
                var grain = this.GrainFactory.GetGrain<IReceivingRepeatedGenArgsAmongstOthers<int, string, int>>(Guid.NewGuid());
                var concreteGenArgs = await GetConcreteGenArgs(grain);
                Assert.True(concreteGenArgs.SequenceEqual(new[] { typeof(string), typeof(int) }));
            }

            /// <summary>
            /// 测试类型分辨率,在界面之间反复使用通用参数。
            /// 验证 Orleans 正确处理相同类型参数的情景
            /// 用于等级体系中不同界面的多次。
            /// </summary>
            [Fact]
            public async Task Generic_RepeatedGenArgsWorkAmongstInterfacesInTypeResolution()
            {
                var grain = this.GrainFactory.GetGrain<IReceivingRepeatedGenArgsFromOtherInterface<bool, bool, bool>>(Guid.NewGuid());
                var concreteGenArgs = await GetConcreteGenArgs(grain);
                Assert.True(concreteGenArgs.SequenceEqual(Enumerable.Empty<Type>()));
            }

            /// <summary>
            /// 测试在与重复的通用参数界面之间进行切换。
            /// 校验 grains 能够被投放在使用相同
            /// 定义中键入参数多次。
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
            /// 激活前用重复的通用参数进行试铸。
            /// 校验复杂通用等级结构的激活前铸造行为
            /// 带有重复类型参数。
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
            /// 重新排列的通用参数的测试分辨率。
            /// 验证 Orleans 能够处理通用参数的界面
            /// 与 grain 执行中不同的顺序。
            /// 注:目前未支持的特性 。
            /// </summary>
            [Fact(Skip = "Currently unsupported")]
            public async Task Generic_RearrangedGenArgsOfCorrectArityAreResolved()
            {
                var grain = this.GrainFactory.GetGrain<IReceivingRearrangedGenArgs<int, long>>(Guid.NewGuid());
                var concreteGenArgs = await GetConcreteGenArgs(grain);
                Assert.True(concreteGenArgs.SequenceEqual(new[] { typeof(long), typeof(int) }));
            }

            /// <summary>
            /// 测试在与重新排列的通用参数界面之间进行切换。
            /// 校验通用参数不同时投铸起作用的
            /// 位置,但总数匹配。
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
            /// 测试预先引爆弹铸,并重新排列通用参数。
            /// 校验参数重新排列前不会中断抛出
            /// 启动 grain 。
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
            /// 在同一通用界面的多个专业之间进行测试。
            /// 证明a grain 执行通用的多混凝土版本
            /// 可以在这些专业之间进行定位。
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
            /// 测试与非相关通用参数接口的切换。
            /// 验证 grains 可投入通用参数的界面
            /// 与 grain的具体类型参数没有任何关系。
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
            /// 测试预激活性铸造到不相关的通用界面。
            /// 验证与独立通用参数接口的铸造
            /// 激活 grain 之前的工作。
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
            /// 测试已经具有遗传性的论点的进一步专业化。
            /// 验证对嵌套的通用名词的处理,如T本身所在的清单<T>
            /// 类型参数待解。
            /// 注:目前未支持的特性 。
            /// </summary>
            [Fact(Skip = "Currently unsupported")]
            public async Task Generic_GenArgsCanBeFurtherSpecialized()
            {
                var grain = this.GrainFactory.GetGrain<IInterfaceTakingFurtherSpecializedGenArg<List<int>>>(Guid.NewGuid());
                var concreteGenArgs = await GetConcreteGenArgs(grain);
                Assert.True(concreteGenArgs.SequenceEqual(new[] { typeof(int) }));
            }

            /// <summary>
            /// 测试通用参数在阵列类型中的专业化。
            /// 验证 Orleans 能够处理通用参数的情景
            /// 专门作为数组类型(T[])。
            /// 注:目前未支持的特性 。
            /// </summary>
            [Fact(Skip = "Currently unsupported")]
            public async Task Generic_GenArgsCanBeFurtherSpecializedIntoArrays()
            {
                var grain = this.GrainFactory.GetGrain<IInterfaceTakingFurtherSpecializedGenArg<long[]>>(Guid.NewGuid());
                var concreteGenArgs = await GetConcreteGenArgs(grain);
                Assert.True(concreteGenArgs.SequenceEqual(new[] { typeof(long) }));
            }

            /// <summary>
            /// 测试嵌套通用专业界面之间的切换。
            /// 当接口使用复杂的通用类型,如清单<T>时验证铸造
            /// 具有不同但相容的专业。
            /// 注:目前未支持的特性 。
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
            /// 测试使用嵌套式通用专业的预活性铸造。
            /// 在启动 grain 之前,验证复杂的通用投射设想方案的工作。
            /// 注:目前未支持的特性 。
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
