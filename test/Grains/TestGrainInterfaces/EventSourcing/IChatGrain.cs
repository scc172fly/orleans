using System.Xml.Linq;

namespace TestGrainInterfaces
{
    /// <summary>
    /// 聊天 Grain 的接口。
    /// </summary>
    public interface IChatGrain : IGrainWithStringKey
    {
        /// <summary> 返回聊天室的当前内容。 </summary>
        Task<XDocument> GetChat();

        /// <summary> 添加一条新消息。 </summary>
        Task Post(Guid guid, string user, string text);

        /// <summary> 删除指定消息。 </summary>
        Task Delete(Guid guid);

        /// <summary> 编辑指定消息。 </summary>
        Task Edit(Guid guid, string text);
    }
}
