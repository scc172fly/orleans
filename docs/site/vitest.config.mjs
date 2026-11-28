import { defineConfig } from 'vitest/config';

export default defineConfig({
  test: {
    // 为全仓解析器及由进程执行的策略检查设置确定的工作线程数量上限。
    fileParallelism: false,
  },
});
