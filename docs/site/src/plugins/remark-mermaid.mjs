const htmlEscapePattern = /[&<>"']/g;
const htmlEscapes = {
  '&': '&amp;',
  '<': '&lt;',
  '>': '&gt;',
  '"': '&quot;',
  "'": '&#39;',
};

function escapeHtml(value) {
  return value.replace(htmlEscapePattern, (character) => htmlEscapes[character]);
}

function transformMermaidBlocks(node) {
  if (node.type === 'code' && node.lang === 'mermaid') {
    const source = escapeHtml(node.value);
    node.type = 'html';
    // 回退内容直接使用 <pre>：Expressive Code 会将每个 <pre><code> 渲染为代码块，并将其
    // 样式表与页面的第一个代码块一同输出；若使用 <noscript> 回退方案，在启用脚本的页面上它就会被隐藏。
    node.value =
      `<div class="mermaid-diagram" data-mermaid-source="${source}" ` +
      'data-mermaid-status="loading" aria-label="Mermaid 图表">' +
      '<span class="sr-only">正在呈现图表。</span>' +
      `<noscript><pre class="mermaid-source">${source}</pre></noscript></div>`;
    delete node.lang;
    delete node.meta;
    return;
  }

  for (const child of node.children ?? []) {
    transformMermaidBlocks(child);
  }
}

export function remarkMermaid() {
  return (tree) => transformMermaidBlocks(tree);
}
