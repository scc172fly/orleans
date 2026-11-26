using System.Xml.Linq;

namespace TestGrains
{
    /// <summary>
    /// 包含如何格式化聊天的 XML 文档( schema) 的选择。
    /// 由于每次装入事件日志时 grain 重播该事件日志,
    /// 有可能改变这种模式, 而不必写 XML 转换
    /// </summary>
    public static class ChatFormat
    {
        public static void Initialize(this XDocument document, DateTime timestamp, string origin)
        {
            if (!document.Nodes().Any())
            {
                document.Add(new XComment($"This chat room was created by {origin}"));
                document.Add(new XElement("root",
                    new XElement("created", timestamp.ToString("s", System.Globalization.CultureInfo.InvariantCulture)),
                    new XElement("posts")));
            }
        }

        public static XElement GetPostsContainer(this XDocument document)
        {
            return document.Elements().Single(x => x.Name.LocalName == "root").Elements().Single(x => x.Name.LocalName == "posts");
        }

        public static XElement MakePost(Guid guid, string user, DateTime timestamp, string text)
        {
            return new XElement("post", new XAttribute("id", guid.ToString()),
                 new XElement("user", user),
                 new XElement("timestamp", timestamp.ToString("s", System.Globalization.CultureInfo.InvariantCulture)),
                 new XElement("text", text)
            );
        }

        public static XElement? FindPost(this XDocument document, string guid)
        {
            return document.GetPostsContainer()
                       .Elements("post")
                       .FirstOrDefault(x => x.Attribute("id")!.Value == guid);
        }

        public static void ReplaceText(this XElement post, string text)
        {
            post.Element("text")!.ReplaceAll(text);
        }

        public static void EnforceLimit(this XDocument document)
        {
            var container = document.GetPostsContainer();
            if (container.Nodes().Count() > ChatFormat.MaxNumPosts)
                container.Nodes().First().Remove();
        }

        public const int MaxNumPosts = 100;
    }


}
