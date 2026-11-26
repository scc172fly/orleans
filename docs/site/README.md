<a id="orleans-documentation-site"></a>

# Orleans 文件站点

这个天体+星光网站主机有 Orleans 文档。
概念的标记源生活在 `src/content/docs`之下;
`npm run prepare:docs` 忽略了排放 `.mdx` 兄弟姐妹 `/docs/` 星光
能够使导入的 Microsoft 学习语法。

使用 Node.js 24或24年后 .NET SDK 由存储库选择
`global.json`.

```powershell
npm install
npm run api:generate
npm run dev
```

生产API ,可建造公众的 Orleans 成套软件,需要几分钟时间。
生成的软件包 JSON 被 Git 忽略, 不应该被执行。 您可以
当本地预览不需要 API 参考页时跳过此步骤 。

开发服务器不包括页面定位搜索, 因为天体转换
预览确切的静态文物,包括其生产
搜索索引,运行 :

```powershell
npm run build
npm run preview
```

生产厂房出版 `/llms.txt`、 `/llms-small.txt`和
`/llms-full.txt`. 每个文件页还有一个标记相配的
URL 页面中的 `.md` 后缀, URL 的 URL 值。这些伴侣使用相同的已编制
以星光形式收集内容条目, 包括代码指令、 xrefs,
和 DocFX 变换已经应用。

运行重点转换测试, 天体类型检查, 严格的片段扩展,
项目政策审计、星光联系验证、
确定页码索引,并用以下方法建立生产:

```powershell
npm run validate
```

天体天体在 Windows 上默认生成一条路径, 以避免
跟踪到
[#10672](https://github.com/dotnet/orleans/issues/10672). 其他平台
设置 `ORLEANS_DOCS_BUILD_CONCURRENCY` 到 a
选择不同工人计数的正整数。
使用同时生成的 Node.js 24.20或24.20 以后,包括
次级的libuv停产主张上游解决。

来源审计报告有一条规则编号、档案、行和补救。

- Orleans 10份指南,除迁移网页外,并明确改写兼容区。
- 概念页每个输入一个 `toc.yml` 条目, `includes`,
  片段支持文件, 兼容路径标记 `navigation: hidden`
  排除; 建筑和内部以及事件保值仍为头等
  导航区。
- 对照源项目元数据编制成套和供应方清单,
  对照源/API 常量,有记录的
  `InstrumentNames.cs`的[量 和
  `samples/gallery.json`.
- 以 `:::code` 命名区域引用的C# 实例。
  围栏无法进行源审计;在共享源文件和
  提供显示区域外的隐藏脚架。
- 源的觉知和链接验证。 Microsoft Learn-relative
  链接必须使用 Canonic `https://learn.microsoft.com/...` URL; 已实现
  线路、编码路径、重定向和锚必须在 `/orleans/`下解决。
  来源诊断包括文件/线源。
- 生成到此仓库的链接会与本地检出检查进行核对 。
  其余的外部HTTP(S)验证用途
  超时、超时、重新试和主机主机总部领导到 GET 的倒回。
  明确报告利率限值和瞬时网络故障
  调用请求内容是不信任的:
  拒绝证书、非违约端口以及任何主机名/IP
  私密、回路、链接本地、元数据或其他非公共网络。
  校验并锁定在每个
  跳过重定向, 因此贡献者不能使用重定向、 重新约束或代理
  将链接验证转换为内部网络请求的环境变量。

`npm run build` 和 `npm run validate` 发现 `docs/` 下的每一项目,
`samples/`,要求确切的成员为 `docs/Docs.slnx` 和
`samples/Samples.slnx`,评估一个准确的 `net10.0` 目标,并要求每个
Orleans 确定为 `10.2.2`将文件包包作为项目
仅针对 `net11.0` 目标,确切的 `net11.0` 目标,并说明为什么在有意义的
`OrleansDocumentationTargetFrameworkException` 属性。历史包版本为
仅在移民项目中接受,且有使用过、有意义的
`OrleansDocumentationVersionException` 财产。
使用预先释放的 `10.2.2` ,具有相同财产和具体理由。
在当地建立并测试文档项目,运行:

```powershell
dotnet build ../Docs.slnx --configuration Release
dotnet test --solution ../Docs.slnx --configuration Release --framework net10.0 --no-build --minimum-expected-tests 1 --max-parallel-test-modules 1
```

建立样本 `samples/Build-Samples.ps1`,它包装了当前 Orleans
建筑前的本地种子源 `samples/Samples.slnx`。

只按顺序编辑已检查的文档片断项目,运行 :

```powershell
pwsh src/content/docs/validate-snippets.ps1
```

固定 `DOCS001` 保持当前版本文件的无版、移动
将特定版本的指导意见插入 `migration/` 或升级页,或与这些页面链接
修复 `DOCS002`/`DOCS003` 内 `toc.yml`. 用于 `DOCS004`移动C内行
编译的片段工程中命名区域并添加隐藏上下文的示例
区域外。 对于 `DOCS005`, 将作者引用与
指定源清单。
每个可包装的 Orleans 源包件必须有文件记录或有一个合理输入
`src/data/package-inventory-exclusions.json`中。
确定 `PROJECT001`-`PROJECT003` ,办法是在
`docs/Docs.slnx` 和 `samples/Samples.slnx` 中的样本项目,直到文件系统
准确匹配的发现 。 `PROJECT004` 仅以目标为目标 `net10.0`,或仅
`net11.0` ,并有有意义的 `OrleansDocumentationTargetFrameworkException`,并且加以修正
`PROJECT005` ,将有效的 Orleans 软件包版本更新为 `10.2.2`。
`PROJECT006` 识别无效、模糊或陈旧版本的例外。
重建前报告的小片项目`SNIPPET001`/`SNIPPET002` 。
添加工程时, 传递 `--include-references false` 并使用一个解决方案文件夹
符合 `docs/` 或 `samples/`下路径; 存储库源依赖性
过渡地建立,不是直接的 `Docs.slnx` 条目。
使用运河URL进行补救的固定 `LINK001` 。
能够绘制已创建链接时, 命名源文件/行, 否则
已经设定的路线。 `src/data/external-link-allowlist.json` 只接受准确的 URL;
每个条目记录一个狭窄的探测可靠性原因,并仍在积极调查。
审计报告的每个答复情况及其理由,包括成功
间断可获取目标的答复。
证明目标可靠地接受了被绑的自动探测器。
未参照条目的未参考条目失效, 且目的地安全检查适用于
生成 API 软件包,等待第一个
NuGet 出版物也需要临时进入
`src/data/unpublished-api-packages.json`;在公布后删除这两个条目。

选项仍是有意制定的最后最后名单,而不是生成的
目录; 审计要求该页面将生成的 API 参考文献确定为
计量义词具有类似的选择性,但每个指标都具有针对性。
身份识别符, 它名称必须存在运行时间源中 。

样本页面读取 `samples/gallery.json` 从仓库根根。当
缺少目录,发展建设使解释性状态变得空洞。

<a id="native-api-reference"></a>

## 原生 API 引用

下 `/docs/api/csharp/` 线条树使用与原生成像结构相同的建筑
[`microsoft/aspire.dev`](https://github.com/microsoft/aspire.dev):一个天体
`packages` 内容收藏读作 `src/data/pkgs/*.json`,星光页面
配有匹配的
`.md` 众朋友,
期望的 JSON 形状是阿斯皮尔的Roslyn/XML/PDB输出
`PackageJsonGenerator`,经 Orleans API 发电机工作流程调整。

生成生成 API 预览前的数据 API 页面或运行完整的本地构建 :

```powershell
npm run api:generate
```

GitHub 行动产生 API 需求数据,为每个单位建立 `docs/Docs.slnx`
项目的目标框架,包括诊断的书包、测试其试验项目;
建立整个工地,并部署 `dist` 至 GitHub 页面自 `main`.
请求拉拉接收可下载的现场文物,但从未得到部署
权限。

部署前的一组 **Settings → Pages → Source** 至 **GitHub
行动**。工作流程在按到 `main`后发布,晚上9:00世界协调时
和手动发送。

生产结构还释放出对两种遗留物的兼容性
`gh-pages` 场地图。 `src/data/legacy-jekyll-pages.json` 地图
预DocFX 文档页面,以显示其最佳的当前后续文件。 GitHub 提供页面服务
每个生成 `.html` 以历史无延伸别名
。hw. `src/data/legacy-pages.json` 捕捉最终 DocFX-era 站点。
当新网站拥有其时, 保留其准确的 URL; 留存文件
和博客 URL 重新定位到最近的当前文档入口。
以 `src/data/redirects.json` 并保存着船舶和船舶,
自动覆盖 DocFX-era 路径匹配。

`npm run audit:links` 验证源源作者外部链接和每条内容
内部路径和锁定。它自动运行,从 `npm run build` 和
`npm run validate`.

`npm run audit:output` 扫描完整完成的现场以复制或丢失
页面标题,泄漏 Microsoft 学习指令、错误 API 和签字国或
操作者名称、超大导航、缺失的遗留重置和 GitHub
页面大小限制回归。
