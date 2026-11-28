using UnitTests.GrainInterfaces;

namespace UnitTests.Grains
{
    // 取消以下类的注释，以验证 #1349 的代码生成是否正确
    // （在代码生成成功后执行此操作）
    // 注意：还需要取消 Tester/GeneratorGrainTests.cs 中相应测试的注释

    public class GeneratorTestDerivedFromFSharpInterfaceInExternalAssemblyGrain : Grain, IGeneratorTestDerivedFromFSharpInterfaceInExternalAssemblyGrain
    {
        public Task<int> Echo(int x)
        {
            return Task.FromResult(x);
        }

        public Task<Tuple<string, int>> MultipleParameterEcho(string s, int x)
        {
            return Task.FromResult(new Tuple<string, int>(s, x));
        }
    }
}
