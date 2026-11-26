using Xunit.Sdk;
using Xunit.v3;

// 测试在重建共享的提供者数据库时按顺序运行 。
[assembly: Parallelization(Mode = ParallelMode.None)]
