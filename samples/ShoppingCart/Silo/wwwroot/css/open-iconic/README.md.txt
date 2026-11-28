[Open Iconic v1.1.1](http://useiconic.com/open)
===========

### Open Iconic 是 [Iconic](http://useiconic.com) 的开源姊妹项目。它包含 223 个清晰易辨、体积小巧的图标&mdash;可直接与 Bootstrap 和 Foundation 配合使用。[查看图标集](http://useiconic.com/open#icons)



## Open Iconic 包含哪些内容？

* 223 个图标，缩小到 8 像素仍然清晰易辨
* 超轻量 SVG 文件：整套大小为 61.8
* SVG 精灵&mdash;图标字体的现代替代方案
* Web 字体（EOT、OTF、SVG、TTF、WOFF）、PNG 和 WebP 格式
* CSS、LESS、SCSS 和 Stylus 格式的 Web 字体样式表（包含适用于 Bootstrap 和 Foundation 的版本）
* 尺寸为 8px、16px、24px、32px、48px 和 64px 的 PNG 和 WebP 栅格图像。


## 入门

#### 如需代码示例以及开始使用 Open Iconic 所需的其他信息，请查看[图标](http://useiconic.com/open#icons)和[参考](http://useiconic.com/open#reference)部分。

### 常规用法

#### 使用 Open Iconic 的 SVG 图像

我们喜欢 SVG，并认为它是网页显示图标的合适方式。Open Iconic 的图像都是基本的 SVG，因此建议像显示其他图像一样使用它们（别忘了 `alt` 属性）。

```
<img src="/open-iconic/svg/icon-name.svg" alt="icon name">
```

#### 使用 Open Iconic 的 SVG 精灵

Open Iconic 还提供 SVG 精灵，只需一次请求即可显示图标集中的所有图标。它类似于图标字体，但无需采用变通手段。

从 SVG 精灵中添加图标的方式与您习惯的做法略有不同，不过依然很简单。*提示：为了便于设置图标样式，建议为* `<svg>` *标签添加一个通用类，并在* `<use>` *标签上为每个不同的图标指定唯一的类名。*

```
<svg class="icon">
  <use xlink:href="open-iconic.svg#account-login" class="icon-account-login"></use>
</svg>
```

只需基本的 CSS 即可设置图标大小。所有图标均为正方形，因此只要为 `<svg>` 标签设置相等的宽度和高度即可。

```
.icon {
  width: 16px;
  height: 16px;
}
```

为图标设置颜色更加简单，只需将 `fill` 规则设置在 `<use>` 标签上。

```
.icon-account-login {
  fill: #f00;
}
```

如需深入了解 SVG 精灵，请阅读 [Chris Coyier 的指南](http://css-tricks.com/svg-sprites-use-better-icon-fonts/)。

#### 使用 Open Iconic 的图标字体……


##### ……配合 Bootstrap 使用

可在 `font/css/open-iconic-bootstrap.{css, less, scss, styl}` 中找到适用于 Bootstrap 的样式表。


```
<link href="/open-iconic/font/css/open-iconic-bootstrap.css" rel="stylesheet">
```


```
<span class="oi oi-icon-name" title="icon name" aria-hidden="true"></span>
```

##### ……配合 Foundation 使用

可在 `font/css/open-iconic-foundation.{css, less, scss, styl}` 中找到适用于 Foundation 的样式表。

```
<link href="/open-iconic/font/css/open-iconic-foundation.css" rel="stylesheet">
```


```
<span class="fi-icon-name" title="icon name" aria-hidden="true"></span>
```

##### ……独立使用

可在 `font/css/open-iconic.{css, less, scss, styl}` 中找到默认样式表。

```
<link href="/open-iconic/font/css/open-iconic.css" rel="stylesheet">
```

```
<span class="oi" data-glyph="icon-name" title="icon name" aria-hidden="true"></span>
```


## License

### Icons

All code (including SVG markup) is under the [MIT License](http://opensource.org/licenses/MIT).

### Fonts

All fonts are under the [SIL Licensed](http://scripts.sil.org/cms/scripts/page.php?item_id=OFL_web).
