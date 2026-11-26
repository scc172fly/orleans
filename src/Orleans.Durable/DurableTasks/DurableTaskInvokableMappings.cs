using Orleans.Durable.Tasks.Protocol;
using Orleans;
using Orleans.Durable.Tasks;
using Orleans.Runtime;

[assembly: InvokableBaseType(typeof(GrainReference), typeof(DurableTask), typeof(DurableTaskRequest))]
[assembly: InvokableBaseType(typeof(GrainReference), typeof(DurableTask<>), typeof(DurableTaskRequest<>))]

// 抽象数据显示它们已经存在的类型的化名; 这个组装生成了它们的元数据。
[assembly: GenerateCodeForDeclaringAssembly(typeof(DurableTaskResponse))]
