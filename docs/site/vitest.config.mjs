import { defineConfig } from 'vitest/config';

export default defineConfig({
  test: {
    // 给全库的执业者和流程支持的政策检查人员一个确定性工人预算。
    fileParallelism: false,
  },
});
