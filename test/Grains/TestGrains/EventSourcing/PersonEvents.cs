using TestGrainInterfaces;

namespace TestGrains
{
    // 列出 JournaledPersonGrain 支持的全部事件

    // 我们选择让所有这些事件实现以下标记接口
    // （此操作可选，但能够提供更多类型检查）
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
