using Xunit.Sdk;
using Xunit.v3;

// 由于测试会重新创建共享的提供程序数据库，因此按顺序执行测试。
[assembly: Parallelization(Mode = ParallelMode.None)]
