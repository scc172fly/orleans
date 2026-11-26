using System.Xml.Linq;

namespace TestGrainInterfaces
{
    /// <summary>
    /// 下 grain 聊天对话框的界面 grain.
    /// </summary>
    public interface IChatGrain : IGrainWithStringKey
    {
        /// <summary> 返回聊天室当前内容 。 </summary>
        Task<XDocument> GetChat();

        /// <summary> 增加一个新的员额。 </summary>
        Task Post(Guid guid, string user, string text);

        /// <summary> 删除具体员额。 </summary>
        Task Delete(Guid guid);

        /// <summary> 编辑一个特定职位。 </summary>
        Task Edit(Guid guid, string text);
    }
}
