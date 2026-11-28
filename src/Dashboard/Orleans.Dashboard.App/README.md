<a id="orleans-dashboard-app"></a>

# Orleans Dashboard 前端应用

此目录包含使用 React 和 Vite 构建的 Orleans Dashboard 前端 Web 应用程序。生产构建产物提交在 `dist/` 下，构建 Orleans.Dashboard C# 项目时，会将 `dist/` 中的每个文件作为资源嵌入。.NET 构建不需要 Node.js 或 npm。

<a id="structure"></a>

## 目录结构

```
Orleans.Dashboard.App/
├── src/              # React application source code
├── public/           # Static assets copied to the build output
├── scripts/          # npm installation helper and its tests
├── dist/             # Committed production build, embedded by Orleans.Dashboard
├── screenshots/      # Dashboard screenshots for documentation
├── tests/            # Playwright navigation tests
├── index.html        # HTML template
├── package.json      # npm dependencies and scripts
├── vite.config.ts    # Vite build configuration
└── tsconfig.json     # TypeScript configuration
```

<a id="development"></a>

## 开发

请使用 `.node-version` 中指定的 Node.js 版本及 `package.json` 中指定的 npm 版本。在此目录中执行：

```shell
npm run restore
npm run dev
```

`npm run restore` 执行 `npm ci`；当 npm 在下载包时报告连接重置，该命令会重新开始全新安装，最多尝试三次。其他失败会立即报告。

开发服务器在 `http://localhost:5173` 提供热模块替换功能。

<a id="updating-the-committed-build"></a>

## 更新已提交的构建产物

修改前端或其依赖项后，请重新构建，并将 `dist/` 与源码变更一起提交：

```shell
npm run restore
npm run build
```

构建会生成打包产物、源码映射、字体、图片和 `THIRD-PARTY-NOTICES.md`；其中声明文件由 Vite 根据打包依赖项的许可证生成。NuGet 包还会将该声明作为独立文件包含在内。

当拉取请求或推送修改此目录时，会运行 `Dashboard` 工作流。它执行相同命令，在产物与已提交的 `dist/` 不一致时失败，并运行导航测试。前端源码在所有平台上都使用 LF 换行符，因此 Windows、Linux 和 macOS 上的构建会生成相同文件。

<a id="lifecycle-diagrams"></a>

## 生命周期图

生命周期视图呈现按顺序排列的 HTML 阶段卡片，并使用 CSS 箭头连接。
启动按阶段顺序递增执行；关闭顺序相反。观察者名称及
方法均以文本呈现；对于较长的生命周期及
较长的标签，图表支持滚动。卡片采用所选的浅色或深色主题。

<a id="navigation-tests"></a>

## 导航测试

浏览器测试覆盖生命周期图、计数器刷新、慢请求、路由变更、窄屏导航、键盘关闭及桌面布局断点：

```shell
npx playwright install chromium
npm run test:ui
```

`npm run preview` 在本地提供生产构建产物的预览服务。
