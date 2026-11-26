<a id="orleans-dashboard-app"></a>

# Orleans 仪表板

本目录包含用于 Orleans Dashboard的前端网络应用程序, 与 React and Vite 一起建造。 生产构建是在 `dist/`下进行, 并且将 Orleans.Dashboard C# 项目作为资源嵌入每个文件 `dist/` 。 .NET 建设不需要 Node.js 或 npm 。

<a id="structure"></a>

## 结构结构结构

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

## 发展

使用 Node.js 版本中的 `.node-version` 版本,使用 `package.json`中的 npm 版本。 从此目录中:

```shell
npm run restore
npm run dev
```

`npm run restore` 运行 `npm ci` 并重新启动清洁安装,最多三次尝试,当 npm 报告下载软件包时连接重置。 其他故障则立即报告。

开发服务器在 `http://localhost:5173`提供热舱替换。

<a id="updating-the-committed-build"></a>

## 更新承诺建设

在改变前端或其依赖性、重建和承诺 `dist/` 与源变化:

```shell
npm run restore
npm run build
```

构建书写了维特从捆绑的依附关系许可证中生成的包、源地图、字体、图像和 `THIRD-PARTY-NOTICES.md`。 NuGet 软件包还将通知作为独立文件列入。

当拉动请求或按下修改此目录时, `Dashboard` 工作流程会运行。它会运行相同的命令,如果输出与承诺的 `dist/`不同,就会失败。前端源在每个平台上使用LF线结尾,因此以 Windows、 Linux 和 macOS 为基础,生成相同文件。

<a id="lifecycle-diagrams"></a>

## 生命周期图

生命周期视图使定购的 HTML 级卡与 CSS 箭头连接。
启动后是不断增长的阶段秩序;关闭后倒转。
以文字和图纸滚动方式提供大生命周期和
长标签。卡使用选定的光或暗主题。

<a id="navigation-tests"></a>

## 导航测试

浏览器测试练习生命周期图、反刷新、慢要求、路线改变、窄屏导航、键盘解禁和桌面断点:

```shell
npx playwright install chromium
npm run test:ui
```

`npm run preview` 在当地为生产结构提供服务。
