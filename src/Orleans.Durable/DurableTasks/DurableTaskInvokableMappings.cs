using Orleans.Durable.Tasks.Protocol;
using Orleans;
using Orleans.Durable.Tasks;
using Orleans.Runtime;

[assembly: InvokableBaseType(typeof(GrainReference), typeof(DurableTask), typeof(DurableTaskRequest))]
[assembly: InvokableBaseType(typeof(GrainReference), typeof(DurableTask<>), typeof(DurableTaskRequest<>))]

// 抽象层声明其持久化类型的别名；此程序集生成这些类型的元数据。
[assembly: GenerateCodeForDeclaringAssembly(typeof(DurableTaskResponse))]
