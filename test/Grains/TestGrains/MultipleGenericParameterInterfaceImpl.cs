namespace UnitTests.Grains
{
    public class CodeGenTestPoco
    {
        public int SomeProperty { get; set; }
    }

    // 此类形成代码生成测试案例 。
    // 如果代码生成器在产品中为同步状态机器生成任何代码
    // 方法,它必须生成有效的 C# 语法。 见: https://github.com/dotnet/orleans/pull/3639
    public class FeaturePopulatorCodeGenTestClass : CodeGenTestPoco, FeaturePopulatorCodeGenTestClass.IFactory<int, double>
    {
        public interface IFactory<TInput, TOutput>
        {
            Task<TOutput> Product(TInput input);
        }

        async Task<double> IFactory<int, double>.Product(int input)
        {
            await Task.Delay(100);
            return input;
        }
    }
}
