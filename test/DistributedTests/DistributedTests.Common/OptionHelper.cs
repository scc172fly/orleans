using System.CommandLine;
using System.CommandLine.Parsing;

namespace DistributedTests
{
    public static class OptionHelper
    {
        public static Option<T> CreateOption<T>(string alias, string? description = null, bool isRequired = false, T defaultValue = default!, Func<T, bool>? validator = null)
        {
            var options = new Option<T>(alias, description) { IsRequired = isRequired };
            if (!isRequired)
            {
                options.SetDefaultValue(defaultValue);
            }
            if (validator != null)
            {
                options.AddValidator(result => Validator(result, validator));
            }
            return options;
        }

        public static string Validator<T>(OptionResult result, Func<T, bool> validator)
        {
            var value = result.GetValueOrDefault<T>();
            // 验证器仍按照原有方式处理解析后的值；null 宽恕
            // 运算符仅抑制无约束泛型的警告，不会生成 IL 指令。
            if (!validator(value!))
            {
                return $"Option {result.Token?.Value} cannot be set to {value}";
            }
            return string.Empty;
        }

        public static bool OnlyStrictlyPositive(int value) => value > 0;
        public static bool OnlyPositiveOrZero(int value) => value >= 0;
    }
}
