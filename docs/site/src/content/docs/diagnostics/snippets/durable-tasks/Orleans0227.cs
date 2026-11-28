namespace Documentation.Diagnostics.DurableTasks;

public interface IEnrollmentGrain : IGrainWithStringKey
{
    DurableTask<int> EnrollAsync(string student);

    DurableTask<int> EnrollAndWelcomeAsync(string student);
}

public sealed class EnrollmentGrain : DurableGrain, IEnrollmentGrain
{
    private static readonly DurableVersion Welcomes = new("welcomes", minSupported: 0, maxSupported: 1);

    private readonly IDurableValue<int> _enrolled;

    public EnrollmentGrain() => _enrolled = StateManager.GetOrAddValue<int>("enrolled");

#pragma warning disable ORLEANS0227
    // <orleans0227_violation>
    public async DurableTask<int> EnrollAsync(string student)
    {
        var seat = DurableTask.RunInline("seat", () =>
        {
            var number = ++_enrolled.Value;

            // 版本检查抛出 InvalidOperationException，效果将该异常记录为其结果。
            if (Welcomes.IsAtLeast(1))
            {
                _ = GrainFactory.GetGrain<IStudentMail>(student).WelcomeAsync(student, number).WithId("welcome").Detached().Post();
            }

            return number;
        });

        await Confirm(student, seat).WithId("confirm");
        return seat;
    }
    // </orleans0227_violation>
#pragma warning restore ORLEANS0227

    // <orleans0227_fix>
    public async DurableTask<int> EnrollAndWelcomeAsync(string student)
    {
        // 效果返回座位，任务体检查版本并启动欢迎通知。
        var seat = DurableTask.RunInline("seat", () => ++_enrolled.Value);
        if (Welcomes.IsAtLeast(1))
        {
            _ = GrainFactory.GetGrain<IStudentMail>(student).WelcomeAsync(student, seat).WithId("welcome").Detached().Post();
        }

        await Confirm(student, seat).WithId("confirm");
        return seat;
    }
    // </orleans0227_fix>

    private static async DurableTask<bool> Confirm(string student, int seat) => student.Length > 0 && seat > 0;
}
