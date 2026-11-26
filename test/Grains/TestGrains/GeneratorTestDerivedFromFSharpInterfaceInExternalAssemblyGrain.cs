using UnitTests.GrainInterfaces;

namespace UnitTests.Grains
{
    // 取消以下等级的注释, 以验证 # 1349 正确的代码生成
    // (一旦代码生成成功,就这样做)
    // 注:在试验器/GeneratorGrainTests.cs中未加注相应的试验。

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
