import { unified } from '@astrojs/markdown-remark';
import starlight from '@astrojs/starlight';
import { defineConfig } from 'astro/config';
import remarkDirective from 'remark-directive';
import starlightDotMd from 'starlight-dot-md';
import starlightLinksValidator from 'starlight-links-validator';
import starlightLlmsTxt from 'starlight-llms-txt';
import { getBuildConcurrency } from './scripts/lib/build-settings.mjs';
import { createSidebar } from './scripts/lib/docfx.mjs';
import { cleanMarkdownOutput } from './src/plugins/clean-markdown-output.mjs';
import { remarkMermaid } from './src/plugins/remark-mermaid.mjs';
import { remarkVersionZones } from './src/plugins/remark-version-zones.mjs';

const sidebar = await createSidebar(new URL('./src/content/docs/toc.yml', import.meta.url));
const buildConcurrency = getBuildConcurrency();
const markdownPageExample =
  'https://scc172fly.github.io/orleans/docs/implementation/streams-implementation.md';
sidebar.unshift({ label: '文档', link: '/docs/' });

export default defineConfig({
  site: 'https://scc172fly.github.io',
  base: '/orleans/',
  integrations: [
    starlight({
      title: 'Microsoft Orleans',
      locales: { root: { label: '简体中文', lang: 'zh-CN' } },
      description: '使用 .NET 和 Orleans 构建可伸缩的云原生分布式应用。',
      logo: {
        src: './src/assets/orleans-logo.png',
        alt: 'Orleans 标志',
      },
      favicon: '/favicon.svg',
      customCss: ['./src/styles/custom.css'],
      // 视觉工作室代码主题在 C# 中给出了不同颜色的类型、成员、本地人和关键词。
      expressiveCode: {
        themes: ['dark-plus', 'light-plus'],
      },
      components: {
        MarkdownContent: './src/components/MarkdownContent.astro',
      },
      editLink: {
        baseUrl: 'https://github.com/scc172fly/orleans/edit/main/docs/site/',
      },
      head: [
        {
          tag: 'meta',
          attrs: {
            name: 'theme-color',
            content: '#512bd4',
          },
        },
      ],
      social: [
        {
          icon: 'github',
          label: 'Orleans 的 GitHub 仓库',
          href: 'https://github.com/scc172fly/orleans',
        },
        {
          icon: 'discord',
          label: 'Orleans 的 Discord 社区',
          href: 'https://aka.ms/orleans-discord',
        },
      ],
      sidebar,
      plugins: [
        starlightLinksValidator({
          errorOnRelativeLinks: false,
          exclude: ['/orleans/docs/api/csharp/**'],
        }),
        starlightDotMd({
          includePatterns: ['docs', 'docs/**'],
        }),
        starlightLlmsTxt({
          details:
            'Every documentation page is available as Markdown. Replace the trailing `/` in a page URL with `.md` to retrieve it. ' +
            `For example, https://scc172fly.github.io/orleans/docs/implementation/streams-implementation/ becomes ${markdownPageExample}. ` +
            'The Documentation Overview section below links to the first three levels of conceptual documentation.',
        }),
      ],
    }),
    cleanMarkdownOutput(),
  ],
  markdown: {
    processor: unified({
      remarkPlugins: [remarkDirective, remarkVersionZones, remarkMermaid],
    }),
  },
  build: {
    concurrency: buildConcurrency,
  },
});
