[Open Iconic v1.1.1](https://github.com/iconic/open-iconic)
===========

<a id="open-iconic-is-the-open-source-sibling-of-iconichttpsgithubcomiconicopen-iconic-it-is-a-hyper-legible-collection-of-223-icons-with-a-tiny-footprintmdashready-to-use-with-bootstrap-and-foundation-view-the-collectionhttpsgithubcomiconicopen-iconic"></a>

### Open Iconic 是 [Iconic](https://github.com/iconic/open-iconic) 的开源姊妹项目。它包含 223 个清晰易辨的图标，占用空间极小&mdash;可直接用于 Bootstrap 和 Foundation。[查看图标集](https://github.com/iconic/open-iconic)

<a id="whats-in-open-iconic"></a>

## Open Iconic 包含什么？

* 223 个图标，即使缩小至 8 像素也清晰可辨
* 超轻量 SVG 文件，整个图标集仅 61.8（上游原文未注明单位）
* SVG 精灵图&mdash;图标字体的现代替代方案
* Web 字体（EOT、OTF、SVG、TTF、WOFF）、PNG 及 WebP 格式
* CSS、LESS、SCSS 及 Stylus 格式的 Web 字体样式表（包括 Bootstrap 和 Foundation 版本）
* 8px、16px、24px、32px、48px 和 64px 的 PNG 与 WebP 光栅图像

<a id="getting-started"></a>

## 入门

<a id="for-code-samples-and-everything-else-you-need-to-get-started-with-open-iconic-check-out-our-iconshttpsgithubcomiconicopen-iconic-and-referencehttpsgithubcomiconicopen-iconic-sections"></a>

#### 如需代码示例和 Open Iconic 的其他入门资料，请查看我们的[图标](https://github.com/iconic/open-iconic)与[参考资料](https://github.com/iconic/open-iconic)章节。

<a id="general-usage"></a>

### 常规用法

<a id="using-open-iconics-svgs"></a>

#### 使用 Open Iconic 的 SVG 图标

我们喜欢 SVG，并认为它适合在 Web 上显示图标。Open Iconic 使用普通 SVG，因此建议像显示其他图像那样显示这些图标（不要忘记 `alt` 属性）。

```
<img src="/open-iconic/svg/icon-name.svg" alt="icon name">
```

<a id="using-open-iconics-svg-sprite"></a>

#### 使用 Open Iconic 的 SVG 精灵图

Open Iconic 也提供 SVG 精灵图，允许通过一次请求显示图标集中的所有图标。它类似于图标字体，但无须使用变通技巧。

从 SVG 精灵图添加图标与通常的方式略有不同，但仍很简单。*提示：为了便于设置图标样式，建议给* `<svg>` *标签添加通用类，并在* `<use>` *标签中为每种图标指定唯一的类名。*

```
<svg class="icon">
  <use xlink:href="open-iconic.svg#account-login" class="icon-account-login"></use>
</svg>
```

调整图标大小只需使用基本 CSS。所有图标均为正方形，因此只需为 `<svg>` 标签设置相等的宽度和高度。

```
.icon {
  width: 16px;
  height: 16px;
}
```

为图标着色更为简单。只需在 `<use>` 标签上设置 `fill` 样式规则。

```
.icon-account-login {
  fill: #f00;
}
```

如需了解更多 SVG 精灵图知识，请阅读 [Chris Coyier 的指南](http://css-tricks.com/svg-sprites-use-better-icon-fonts/)。

<a id="using-open-iconics-icon-font"></a>

#### 使用 Open Iconic 的图标字体……

<a id="with-bootstrap"></a>

##### …与 Bootstrap 一起使用

Bootstrap 样式表位于 `font/css/open-iconic-bootstrap.{css, less, scss, styl}`。

```
<link href="/open-iconic/font/css/open-iconic-bootstrap.css" rel="stylesheet">
```

```
<span class="oi oi-icon-name" title="icon name" aria-hidden="true"></span>
```

<a id="with-foundation"></a>

##### …与 Foundation 一起使用

Foundation 样式表位于 `font/css/open-iconic-foundation.{css, less, scss, styl}`。

```
<link href="/open-iconic/font/css/open-iconic-foundation.css" rel="stylesheet">
```

```
<span class="fi-icon-name" title="icon name" aria-hidden="true"></span>
```

<a id="on-its-own"></a>

##### …单独使用

默认样式表位于 `font/css/open-iconic.{css, less, scss, styl}`。

```
<link href="/open-iconic/font/css/open-iconic.css" rel="stylesheet">
```

```
<span class="oi" data-glyph="icon-name" title="icon name" aria-hidden="true"></span>
```

<a id="license"></a>

## 许可证

<a id="icons"></a>

### 图标

全部代码（包括 SVG 标记）均采用 [MIT 许可证](http://opensource.org/licenses/MIT)。

<a id="fonts"></a>

### 字体

全部字体均采用 [SIL 许可证](http://scripts.sil.org/cms/scripts/page.php?item_id=OFL_web)。
