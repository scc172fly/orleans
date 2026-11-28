using System.Xml.Linq;

namespace TestGrains
{
    /// <summary>
    /// 封装有关聊天 XML 文档格式（架构）的选择。
    /// 由于 Grain 每次加载时都会重放事件日志，
    /// 所以可以更改此架构，而无需编写 XML 转换逻辑
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
