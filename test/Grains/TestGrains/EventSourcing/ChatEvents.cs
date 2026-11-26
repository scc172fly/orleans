using System.Xml.Linq;

namespace TestGrains
{
    /// <summary>
    /// 所有聊天事件的聊天事件都执行此界面, 以定义每个事件如何修改 XML 文档
    /// </summary>
    public interface IChatEvent
    {
        void Update(XDocument document);
    }

    [Serializable]
    [Orleans.GenerateSerializer]
    public class CreatedEvent : IChatEvent
    {
        [Orleans.Id(0)]
        public DateTime Timestamp { get; set; }
        [Orleans.Id(1)]
        public string Origin { get; set; } = null!;

        public void Update(XDocument document)
        {
            document.Initialize(Timestamp, Origin);
        }
    }


    [Serializable]
    [Orleans.GenerateSerializer]
    public class PostedEvent : IChatEvent
    {
        [Orleans.Id(0)]
        public Guid Guid { get; set; }
        [Orleans.Id(1)]
        public string User { get; set; } = null!;
        [Orleans.Id(2)]
        public DateTime Timestamp { get; set; }
        [Orleans.Id(3)]
        public string Text { get; set; } = null!;

        public void Update(XDocument document)
        {
            var container = document.GetPostsContainer();
            container.Add(ChatFormat.MakePost(Guid, User, Timestamp, Text));
            document.EnforceLimit();
        }
    }

    [Serializable]
    [Orleans.GenerateSerializer]
    public class DeletedEvent : IChatEvent
    {
        [Orleans.Id(0)]
        public Guid Guid { get; set; }

        public void Update(XDocument document)
        {
            document.FindPost(Guid.ToString())?.Remove();
        }
    }

    [Serializable]
    [Orleans.GenerateSerializer]
    public class EditedEvent : IChatEvent
    {
        [Orleans.Id(0)]
        public Guid Guid { get; set; }
        [Orleans.Id(1)]
        public string Text { get; set; } = null!;

        public void Update(XDocument document)
        {
            document.FindPost(Guid.ToString())?.ReplaceText(Text);
        }
    }
}
