[开阔图标 v1.1.1](https://github.com/iconic/open-iconic)
===========

<a id="open-iconic-is-the-open-source-sibling-of-iconichttpsgithubcomiconicopen-iconic-it-is-a-hyper-legible-collection-of-223-icons-with-a-tiny-footprintmdashready-to-use-with-bootstrap-and-foundation-view-the-collectionhttpsgithubcomiconicopen-iconic"></a>

### 开放图标是 [图标](https://github.com/iconic/open-iconic)的开源比方。它是一个由223个图标组成的超可读集,有小足迹&mdash;,可以随布茨陷阱和基础使用。 [查看收藏](https://github.com/iconic/open-iconic)



<a id="whats-in-open-iconic"></a>

## 开阔图象里是什么?

* 设计为可读到 8 像素的223 个图标
* 超光 SVG 文件 - 整个集61.8
* SVG 文示&mdash;图标字体的现代替换
* 网络组合(EOT、OTF、SVG、TTF、WOFF)、巴布亚新几内亚和 WebP 格式
* 以 CSS、 LESS、 SSCSS 和 Stylus 格式提供的网络组合样式表(包括布设和基础的版本)
* PNG和 WebP 光栅图像,以8px、16px、24px、32px、48px和64px。


<a id="getting-started"></a>

## 入门

<a id="for-code-samples-and-everything-else-you-need-to-get-started-with-open-iconic-check-out-our-iconshttpsgithubcomiconicopen-iconic-and-referencehttpsgithubcomiconicopen-iconic-sections"></a>

#### 对于代码样本和所有其它您需要的 以 Open 图标开始, 请检查我们的 [图标图标](https://github.com/iconic/open-iconic) 和 [参考参考资料](https://github.com/iconic/open-iconic) 部分。

<a id="general-usage"></a>

### 一般使用

<a id="using-open-iconics-svgs"></a>

#### 使用打开图标的 SVG

我们喜欢SVG, 我们认为它们就是在网上显示图标的方式。 因为开放的图标只是基本的 SVG, 我们建议您展示它们就像任何其它图像一样( 不要忘记 `alt` 属性 )。

```
<img src="/open-iconic/svg/icon-name.svg" alt="icon name">
```

<a id="using-open-iconics-svg-sprite"></a>

#### 使用打开图标的 SVG 斯布丽特

打开图标还以 SVG 图示形式出现, 它允许您使用单一请求来显示集中的所有图标。 它就像一个图标字体, 而不是黑客 。

从 SVG 图示中添加图标与您习惯的图标略有不同, 但还是一块蛋糕。 *Tip: 要让您的图标能够容易地样式, 我们建议在* `<svg>` *标签上添加一个普通类, 并为* `<use>` *标记中的每个不同的图标添加一个独有的类别名称。*

```
<svg class="icon">
  <use xlink:href="open-iconic.svg#account-login" class="icon-account-login"></use>
</svg>
```

大小图标只需要基本的 CSS。 所有图标都以平方格式, 所以只要设置 `<svg>` 标记, 其宽度和高度尺寸相等 。

```
.icon {
  width: 16px;
  height: 16px;
}
```

颜色图标更加简单。您只需要设置 `fill` 规则的程序问题的裁定 `<use>` 标签。

```
.icon-account-login {
  fill: #f00;
}
```

要了解更多有关 SVG 斯普赖特的更多信息,请读 [克里斯·科伊耶的指南](http://css-tricks.com/svg-sprites-use-better-icon-fonts/)。

<a id="using-open-iconics-icon-font"></a>

#### 使用 Open 图标的图标字体...


<a id="with-bootstrap"></a>

##### ...带靴子的

您可以在 `font/css/open-iconic-bootstrap.{css, less, scss, styl}`中找到我们的“启动陷阱”样式表


```
<link href="/open-iconic/font/css/open-iconic-bootstrap.css" rel="stylesheet">
```


```
<span class="oi oi-icon-name" title="icon name" aria-hidden="true"></span>
```

<a id="with-foundation"></a>

##### 基金会

您可以在 `font/css/open-iconic-foundation.{css, less, scss, styl}`中找到我们的基金会样式表

```
<link href="/open-iconic/font/css/open-iconic-foundation.css" rel="stylesheet">
```


```
<span class="fi-icon-name" title="icon name" aria-hidden="true"></span>
```

<a id="on-its-own"></a>

##### ...独自

您可以在 `font/css/open-iconic.{css, less, scss, styl}`找到我们的默认样式表

```
<link href="/open-iconic/font/css/open-iconic.css" rel="stylesheet">
```

```
<span class="oi" data-glyph="icon-name" title="icon name" aria-hidden="true"></span>
```


<a id="license"></a>

## 许可证

<a id="icons"></a>

### 图标图标

所有代码(包括 SVG 标记)都在 [麻省理工学院许可证](http://opensource.org/licenses/MIT)之下。

<a id="fonts"></a>

### 字体

所有字体都在 [许可证](http://scripts.sil.org/cms/scripts/page.php?item_id=OFL_web)之下。
