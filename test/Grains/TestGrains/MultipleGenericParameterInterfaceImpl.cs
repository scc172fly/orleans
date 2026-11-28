namespace UnitTests.Grains
{
    public class CodeGenTestPoco
    {
        public int SomeProperty { get; set; }
    }

    // 此类构成一个代码生成测试用例。
    // 如果代码生成器为 Product 方法中的异步状态机生成任何代码，
    // 就必须生成有效的 C# 语法。参见 https://github.com/dotnet/orleans/pull/3639
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
