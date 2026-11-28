import { spawn } from 'node:child_process';
import { mkdtemp, readFile, readdir, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { setTimeout } from 'node:timers/promises';

const npmCli = process.env.npm_execpath;
if (!npmCli) {
  throw new Error('Run this installer through npm run restore.');
}

const logsDirectory = await mkdtemp(join(tmpdir(), 'orleans-npm-install-'));
try {
  for (let attempt = 1; attempt <= 3; attempt++) {
    const attemptLogs = join(logsDirectory, String(attempt));
    const result = await new Promise((resolve, reject) => {
      const child = spawn(process.execPath, [
        npmCli, 'ci', ...process.argv.slice(2),
        '--json', `--logs-dir=${attemptLogs}`, '--logs-max=1',
      ], { stdio: ['inherit', 'pipe', 'inherit'] });
      let stdout = '';
      child.stdout.setEncoding('utf8');
      child.stdout.on('data', chunk => {
        stdout += chunk;
        process.stdout.write(chunk);
      });
      child.on('error', reject);
      child.on('close', (code, signal) => resolve({ code, signal, stdout }));
    });
    if (result.code === 0) {
      break;
    }

    console.error(result.signal
      ? `npm ci terminated by signal ${result.signal}.`
      : `npm ci failed with exit code ${result.code} (attempt ${attempt}/3).`);
    try {
      for (const file of (await readdir(attemptLogs)).sort()) {
        console.error(`npm debug log (${file}):\n${await readFile(join(attemptLogs, file), 'utf8')}`);
      }
    } catch (error) {
      console.error(`Unable to read npm debug logs: ${error.message}`);
    }

    let npmError;
    try {
      npmError = JSON.parse(result.stdout)?.error;
    } catch (error) {
      if (!(error instanceof SyntaxError)) {
        throw error;
      }
      console.error('npm did not return a complete JSON error report; preserving the failure.');
    }

    // Fetch 的重试仅覆盖到收到响应头为止。npm 的 tarball 解包层
    // 在流式读取响应正文时遇到 ECONNRESET 不会重试，因此需要重新执行干净的
    // 安装。npm 使用脚本的数字退出码报告生命周期脚本失败。
    if (!result.signal && npmError?.code === 'ECONNRESET' && attempt < 3) {
      console.error('Retrying npm ci in 5 seconds after npm reported ECONNRESET.');
      await setTimeout(5000);
      continue;
    }

    process.exitCode = result.code ?? 1;
    break;
  }
} finally {
  await rm(logsDirectory, { recursive: true, force: true });
}
