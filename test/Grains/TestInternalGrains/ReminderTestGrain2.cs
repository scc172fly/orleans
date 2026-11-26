using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Runtime;
using Orleans.Runtime.Services;
using Orleans.Timers;
using UnitTests.GrainInterfaces;


#pragma warning disable 612,618
namespace UnitTests.Grains
{
    // 注:如果在此做出任何修改,请将其复制到 ReminderTestCopyGrain
    public class ReminderTestGrain2 : Grain, IReminderTestGrain2, IRemindable
    {
        private readonly IReminderTable reminderTable;

        private readonly IReminderRegistry unvalidatedReminderRegistry;
        private Dictionary<string, ReminderState> allReminders = null!;
        private Dictionary<string, long> sequence = null!;
        private TimeSpan period;

        private static readonly long aCCURACY = 50 * TimeSpan.TicksPerMillisecond; // 当我们使用 滴数来计算序列编号时, 我们可能会得到错误的结果, 因为时间过错不会发生与 滴数精确性不发生... 我们保持它作为回旋余地

        private readonly IOptions<ReminderOptions> reminderOptions;

        private readonly ILogger logger;
        private string _id = null!; // 用于在调试期间区分同一 grain多个激活之间的调试

        private string filePrefix = null!;

        public ReminderTestGrain2(IServiceProvider services, IReminderTable reminderTable, ILoggerFactory loggerFactory)
        {
            this.reminderTable = reminderTable;
            this.unvalidatedReminderRegistry = new UnvalidatedReminderRegistry(services);
            this.logger = loggerFactory.CreateLogger($"{this.GetType().Name}-{this.IdentityString}");
            this.reminderOptions = services.GetService<IOptions<ReminderOptions>>()!;
        }

        public override Task OnActivateAsync(CancellationToken cancellationToken)
        {
            this._id = Guid.NewGuid().ToString();
            this.allReminders = new Dictionary<string, ReminderState>();
            this.sequence = new Dictionary<string, long>();
            this.period = GetDefaultPeriod(this.logger);
            this.logger.LogInformation("OnActivateAsync.");
            this.filePrefix = "g" + this.GrainId.ToString().Replace('/', '_') + "_";
            return GetMissingReminders();
        }

        public override Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
        {
            this.logger.LogInformation("OnDeactivateAsync");
            return Task.CompletedTask;
        }

        public Task<Dictionary<string, ReminderState>> GetReminderStates() => Task.FromResult(allReminders);

        public async Task<IGrainReminder> StartReminder(string reminderName, TimeSpan? p = null, bool validate = false)
        {
            TimeSpan usePeriod = p ?? this.period;
            TimeSpan dueTime;
            if (reminderOptions.Value.MinimumReminderPeriod < TimeSpan.FromSeconds(2))
                dueTime = TimeSpan.FromSeconds(2) - reminderOptions.Value.MinimumReminderPeriod;
            else dueTime = usePeriod - TimeSpan.FromSeconds(2);

            return await StartReminder(reminderName, dueTime, usePeriod, validate);
        }

        public Task<IGrainReminder> StartReminder(string reminderName, TimeSpan dueTime, TimeSpan period)
            => StartReminder(reminderName, dueTime, period, validate: false);

        private async Task<IGrainReminder> StartReminder(string reminderName, TimeSpan dueTime, TimeSpan period, bool validate)
        {
            this.logger.LogInformation("Starting reminder {ReminderName}.", reminderName);
            IGrainReminder r;
            if (validate)
                r = await this.RegisterOrUpdateReminder(reminderName, dueTime, period);
            else
                r = await this.unvalidatedReminderRegistry.RegisterOrUpdateReminder(GrainId, reminderName, dueTime, period);

            this.allReminders[reminderName] = new(r);
            this.sequence[reminderName] = 0;

            string fileName = GetFileName(reminderName);
            File.Delete(fileName); // 如果成功启动, 将删除任何旧数据
            this.logger.LogInformation("Started reminder {Reminder}", r);
            return r;
        }

        public async Task ReceiveReminder(string reminderName, TickStatus status)
        {
            // 可能发生的情况是,由于失败, 当新激活被创建时,
            // 不知哪些提醒通知被登记在 grain
            // 因此,这个激活可能会收到提醒 提醒它自己没有注册 但是
            // 之前的激活( grain注册了... 所以,要小心
            if (!this.sequence.ContainsKey(reminderName))
            {
                this.sequence.Add(reminderName, 0); // 处理此选中项时, 我们会更新最新序列编号
            }

            if (!this.allReminders.ContainsKey(reminderName))
            {
                await GetMissingReminders();
                if (!this.allReminders.ContainsKey(reminderName))
                {
                    throw new OrleansException($"Could not find reminder {reminderName} in grain {this.IdentityString}");
                }
            }

            allReminders[reminderName].Fired.Add(status.CurrentTickTime);

            // 计算秒序列数

            // 我们用长转换法来计算 DateTime 的计算,因为我们没有在 DateTime上进行分割操作
            // 使用 dateTime.Ticks 两次援引 ReceiveReminder(),也许(那里) < period.Ticks
            // 如果连续两个 ReceiveReminder() 之间的勾点大于 period.Ticks,则一切正常...问题在于小于
            // 因此,我们降低我们的准确度 由ACCURACY... 我们在这里准备所有使用的变量
            long now = status.CurrentTickTime.Ticks / aCCURACY; //DateTime.UtcNow.Ticks ACCACY; 反种族主义;
            long first = status.FirstTickTime.Ticks / aCCURACY;
            long per = status.Period.Ticks / aCCURACY;
            long sequenceNumber = 1 + ((now - first) / per);

            // 计算秒序列数结束

            // 在此切换
            if (sequenceNumber < this.sequence[reminderName])
            {
                this.logger.LogInformation("ReceiveReminder: {Reminder} Incorrect tick {ExpectedSequenceNumber} vs. {SequenceNumber} with status {Status}.", reminderName, this.sequence[reminderName], sequenceNumber, status);
                return;
            }
            this.sequence[reminderName] = sequenceNumber;
            this.logger.LogInformation("ReceiveReminder: {ReminderNAme} Sequence # {SequenceNumber} with status {Status}.", reminderName, this.sequence[reminderName], status);

            string fileName = GetFileName(reminderName);
            string counterValue = this.sequence[reminderName].ToString(CultureInfo.InvariantCulture);
            File.WriteAllText(fileName, counterValue);

            return;
        }

        public async Task StopReminder(string reminderName)
        {
            this.logger.LogInformation("Stopping reminder {ReminderName}.", reminderName);
            // 我们希望测试方法能够在停止提醒后也能读取它,所以我们不重置对称
            //return UnregisterReminder(allReminders[reminderName]);
            ReminderState? state;
            if (this.allReminders.TryGetValue(reminderName, out state))
            {
                await this.UnregisterReminder(state.Reminder);
                state.Log.Add((DateTime.UtcNow, $"Unregistering {reminderName}"));
                allReminders[reminderName] = state with { Unregistered = DateTime.UtcNow };
            }
            else
            {
                // 发生故障时,可能发生提前启动的提醒通知登记,我们没有在当地藏匿
                // 因此,我们需要更新本地的缓存
                await GetMissingReminders();
                if (this.allReminders.TryGetValue(reminderName, out state))
                {
                    await this.UnregisterReminder(state.Reminder);
                    state.Log.Add((DateTime.UtcNow, $"Unregistering {reminderName}"));
                    allReminders[reminderName] = state with { Unregistered = DateTime.UtcNow };
                }
                else
                {
                    //var reminders = await this.GetRemindersList();
                    throw new OrleansException(string.Format(
                        "Could not find reminder {0} in grain {1}", reminderName, this.IdentityString));
                }
            }
        }

        private async Task GetMissingReminders()
        {
            List<IGrainReminder> reminders = await this.GetReminders();
            this.logger.LogInformation("Got missing reminders {Reminders}", Utils.EnumerableToString(reminders));
            foreach (IGrainReminder l in reminders)
            {
                if (!this.allReminders.ContainsKey(l.ReminderName))
                {
                    this.allReminders.Add(l.ReminderName, new ReminderState(Reminder: l) { Registered = DateTime.UtcNow, Log = { (DateTime.UtcNow, $"Adding missing: {l.ReminderName}") } });
                }
            }
        }

        public async Task StopReminder(IGrainReminder reminder)
        {
            this.logger.LogInformation("Stopping reminder (using ref) {Reminder}.", reminder);
            // 我们希望测试方法能够在停止提醒后也能读取它,所以我们不重置对称
            await this.UnregisterReminder(reminder);
            var state = allReminders[reminder.ReminderName];
            state.Log.Add((DateTime.UtcNow, $"Stopping {reminder.ReminderName}"));
            allReminders[reminder.ReminderName] = state with { Unregistered = DateTime.UtcNow };
        }

        public Task<TimeSpan> GetReminderPeriod(string reminderName)
        {
            return Task.FromResult(this.period);
        }

        public Task<(TimeSpan DueTime, TimeSpan Period)> GetReminderDueTimeAndPeriod(string reminderName)
        {
            return Task.FromResult((this.period - TimeSpan.FromSeconds(2), this.period));
        }

        public Task<long> GetCounter(string name)
        {
            string fileName = GetFileName(name);
            string data = File.ReadAllText(fileName);
            long counterValue = long.Parse(data);
            return Task.FromResult(counterValue);
        }

        public Task<IGrainReminder?> GetReminderObject(string reminderName)
        {
            return this.GetReminder(reminderName);
        }

        public async Task<List<IGrainReminder>> GetRemindersList()
        {
            return await this.GetReminders();
        }

        private string GetFileName(string reminderName)
        {
            return string.Format("{0}{1}", this.filePrefix, reminderName);
        }

        public static TimeSpan GetDefaultPeriod(ILogger log)
        {
            int period = 12; // 秒数
            var reminderPeriod = TimeSpan.FromSeconds(period);
            log.LogInformation("Using reminder period of {Period} in ReminderTestGrain", reminderPeriod);
            return reminderPeriod;
        }

        public async Task EraseReminderTable()
        {
            await this.reminderTable.TestOnlyClearTable();
        }
    }

    // 注:此处不作更改.这是 ReminderTestGrain的副本。
    // 复制时要做的修改 :
    //      1. 将重命名日志器重命名为 ReminderCopyGrain
    //      2. filePrefix 应首先以“gc”而不是“g”开头。
    public class ReminderTestCopyGrain : Grain, IReminderTestCopyGrain, IRemindable
    {
        private readonly IReminderRegistry unvalidatedReminderRegistry;
        private Dictionary<string, IGrainReminder> allReminders = null!;
        private Dictionary<string, long> sequence = null!;
        private TimeSpan period;

        private static readonly long aCCURACY = 50 * TimeSpan.TicksPerMillisecond; // 当我们使用 滴数来计算序列编号时, 我们可能会得到错误的结果, 因为时间过错不会发生与 滴数精确性不发生... 我们保持它作为回旋余地

        private readonly ILogger logger;
        private long myId; // 用于在调试期间区分同一 grain多个激活之间的调试

        private string filePrefix = null!;

        public ReminderTestCopyGrain(IServiceProvider services, ILoggerFactory loggerFactory)
        {
            this.unvalidatedReminderRegistry = new UnvalidatedReminderRegistry(services);
            this.logger = loggerFactory.CreateLogger($"{this.GetType().Name}-{this.IdentityString}");
        }

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            this.myId = new Random().Next();
            this.allReminders = new Dictionary<string, IGrainReminder>();
            this.sequence = new Dictionary<string, long>();
            this.period = ReminderTestGrain2.GetDefaultPeriod(this.logger);
            this.logger.LogInformation("OnActivateAsync.");
            this.filePrefix = "gc" + this.GrainId.Key + "_";
            await GetMissingReminders();
        }

        public override Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
        {
            this.logger.LogInformation("OnDeactivateAsync.");
            return Task.CompletedTask;
        }

        public async Task<IGrainReminder> StartReminder(string reminderName, TimeSpan? p = null, bool validate = false)
        {
            TimeSpan usePeriod = p ?? this.period;
            this.logger.LogInformation("Starting reminder {ReminderName} for {GrainId}", reminderName, this.GrainId);
            IGrainReminder r;
            if (validate)
                r = await this.RegisterOrUpdateReminder(reminderName, usePeriod - TimeSpan.FromSeconds(2), usePeriod);
            else
                r = await this.unvalidatedReminderRegistry.RegisterOrUpdateReminder(
                    this.GrainId,
                    reminderName,
                    usePeriod - TimeSpan.FromSeconds(2),
                    usePeriod);
            if (this.allReminders.ContainsKey(reminderName))
            {
                this.allReminders[reminderName] = r;
                this.sequence[reminderName] = 0;
            }
            else
            {
                this.allReminders.Add(reminderName, r);
                this.sequence.Add(reminderName, 0);
            }
            File.Delete(GetFileName(reminderName)); // 如果成功启动, 将删除任何旧数据
            this.logger.LogInformation("Started reminder {Reminder}.", r);
            return r;
        }

        public async Task ReceiveReminder(string reminderName, TickStatus status)
        {
            // 可能发生的情况是,由于失败, 当新激活被创建时,
            // 不知哪些提醒通知被登记在 grain
            // 因此,这个激活可能会收到提醒 提醒它自己没有注册 但是
            // 之前的激活( grain注册了... 所以,要小心
            if (!this.sequence.ContainsKey(reminderName))
            {
                // allReminders.Add(reminderName, r);//目前未使用 allReminders
                //counters.Add(reminderName, 0);
                this.sequence.Add(reminderName, 0); // 处理此选中项时, 我们会更新最新序列编号
            }

            if (!this.allReminders.ContainsKey(reminderName))
            {
                await GetMissingReminders();
                if (!this.allReminders.ContainsKey(reminderName))
                {
                    throw new OrleansException($"Could not find reminder {reminderName} in grain {this.IdentityString}");
                }
            }

            // 计算秒序列数

            // 我们用长转换法来计算 DateTime 的计算,因为我们没有在 DateTime上进行分割操作
            // 使用 dateTime.Ticks 两次援引 ReceiveReminder(),也许(那里) < period.Ticks
            // 如果连续两个 ReceiveReminder() 之间的勾点大于 period.Ticks,则一切正常...问题在于小于
            // 因此,我们降低我们的准确度 由ACCURACY... 我们在这里准备所有使用的变量
            long now = status.CurrentTickTime.Ticks / aCCURACY; //DateTime.UtcNow.Ticks ACCACY; 反种族主义;
            long first = status.FirstTickTime.Ticks / aCCURACY;
            long per = status.Period.Ticks / aCCURACY;
            long sequenceNumber = 1 + ((now - first) / per);

            // 计算秒序列数结束

            // 在此切换
            if (sequenceNumber < this.sequence[reminderName])
            {
                this.logger.LogInformation("{ReminderName} Incorrect tick {ExpectedSequenceNumber} vs. {SequenceNumber} with status {Status}.", reminderName, this.sequence[reminderName], sequenceNumber, status);
                return;
            }

            this.sequence[reminderName] = sequenceNumber;
            this.logger.LogInformation("{ReminderName} Sequence # {SequenceNumber} with status {Status}.", reminderName, this.sequence[reminderName], status);
            File.WriteAllText(GetFileName(reminderName), this.sequence[reminderName].ToString());

            return;
        }

        public async Task StopReminder(string reminderName)
        {
            this.logger.LogInformation("Stopping reminder {Reminder}.", reminderName);
            // 我们希望测试方法能够在停止提醒后也能读取它,所以我们不重置对称
            //return UnregisterReminder(allReminders[reminderName]);
            IGrainReminder? reminder;
            if (this.allReminders.TryGetValue(reminderName, out reminder))
            {
                await this.UnregisterReminder(reminder);
            }
            else
            {
                // 发生故障时,可能发生提前启动的提醒通知登记,我们没有在当地藏匿
                // 因此,我们需要更新本地的缓存
                await GetMissingReminders();
                await this.UnregisterReminder(this.allReminders[reminderName]);
            }
        }

        private async Task GetMissingReminders()
        {
            List<IGrainReminder> reminders = await this.GetReminders();
            foreach (IGrainReminder l in reminders)
            {
                if (!this.allReminders.ContainsKey(l.ReminderName))
                {
                    this.allReminders.Add(l.ReminderName, l);
                }
            }
        }

        public async Task StopReminder(IGrainReminder reminder)
        {
            this.logger.LogInformation("Stopping reminder (using ref) {Reminder}.", reminder);
            // 我们希望测试方法能够在停止提醒后也能读取它,所以我们不重置对称
            await this.UnregisterReminder(reminder);
        }

        public Task<TimeSpan> GetReminderPeriod(string reminderName)
        {
            return Task.FromResult(this.period);
        }

        public Task<long> GetCounter(string name)
        {
            return Task.FromResult(long.Parse(File.ReadAllText(GetFileName(name))));
        }

        public async Task<IGrainReminder?> GetReminderObject(string reminderName)
        {
            return await this.GetReminder(reminderName);
        }
        public async Task<List<IGrainReminder>> GetRemindersList()
        {
            return await this.GetReminders();
        }

        private string GetFileName(string reminderName)
        {
            return string.Format("{0}{1}", this.filePrefix, reminderName);
        }
    }

    public class WrongReminderGrain : Grain, IReminderGrainWrong
    {
        private readonly ILogger logger;

        public WrongReminderGrain(ILoggerFactory loggerFactory)
        {
            this.logger = loggerFactory.CreateLogger($"{this.GetType().Name}-{this.IdentityString}");
        }

        public override Task OnActivateAsync(CancellationToken cancellationToken)
        {
            this.logger.LogInformation("OnActivateAsync.");
            return Task.CompletedTask;
        }

        public async Task<bool> StartReminder(string reminderName)
        {
            this.logger.LogInformation("Starting reminder {Reminder}.", reminderName);
            IGrainReminder r = await this.RegisterOrUpdateReminder(reminderName, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3));
            this.logger.LogInformation("Started reminder {Reminder}. It shouldn't have succeeded!", r);
            return true;
        }
    }


    internal sealed class UnvalidatedReminderRegistry : GrainServiceClient<IReminderService>, IReminderRegistry
    {
        public UnvalidatedReminderRegistry(IServiceProvider serviceProvider) : base(serviceProvider) { }

        public Task<IGrainReminder> RegisterOrUpdateReminder(GrainId callingGrainId, string reminderName, TimeSpan dueTime, TimeSpan period)
        {
            return GetGrainService(callingGrainId).RegisterOrUpdateReminder(callingGrainId, reminderName, dueTime, period);
        }

        public Task UnregisterReminder(GrainId callingGrainId, IGrainReminder reminder)
            => GetGrainService(callingGrainId).UnregisterReminder(reminder);

        public Task<IGrainReminder?> GetReminder(GrainId callingGrainId, string reminderName)
        {
            return GetGrainService(callingGrainId).GetReminder(callingGrainId, reminderName);
        }

        public Task<List<IGrainReminder>> GetReminders(GrainId callingGrainId)
        {
            return GetGrainService(callingGrainId).GetReminders(callingGrainId);
        }
    }
}
#pragma warning restore 612, 618
