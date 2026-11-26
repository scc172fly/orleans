using TestGrainInterfaces;

namespace TestGrains
{
    // 我们列出得到 JournaledPersonGrain支持的所有事件 

    // 我们选择让所有这些活动 都执行以下标记界面
    // (这是可选的,但让我们多打几下)
    public interface IPersonEvent { }

    [Serializable]
    [Orleans.GenerateSerializer]
    public class PersonRegistered : IPersonEvent
    {
        [Orleans.Id(0)]
        public string? FirstName { get; set; }
        [Orleans.Id(1)]
        public string? LastName { get; set; }
        [Orleans.Id(2)]
        public GenderType Gender { get; set; }

        public PersonRegistered(string? firstName, string? lastName, GenderType gender)
        {
            FirstName = firstName;
            LastName = lastName;
            Gender = gender;
        }
    }

    [Serializable]
    [Orleans.GenerateSerializer]
    public class PersonMarried : IPersonEvent
    {
        [Orleans.Id(0)]
        public Guid SpouseId { get; set; }
        [Orleans.Id(1)]
        public string? SpouseFirstName { get; set; }
        [Orleans.Id(2)]
        public string? SpouseLastName { get; set; }

        public PersonMarried(Guid spouseId, string? spouseFirstName, string? spouseLastName)
        {
            SpouseId = spouseId;
            SpouseFirstName = spouseFirstName;
            SpouseLastName = spouseLastName;
        }
    }

    [Serializable]
    [Orleans.GenerateSerializer]
    public class PersonLastNameChanged : IPersonEvent
    {
        [Orleans.Id(0)]
        public string? LastName { get; set; }

        public PersonLastNameChanged(string? lastName)
        {
            LastName = lastName;
        }
    }
}
