<a id="streaming-custom-data-adapter"></a>

# 流：自定义数据适配器

本示例演示如何将 Orleans Streams 与非 Orleans 发布者一起使用。外部发布者将消息推送到流中，Grain 借助*自定义数据适配器*消费该流；适配器告知 Orleans 如何解释流消息。
