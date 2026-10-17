// Starts and stops the servers the tests run against. Each runs as a child process on its own port.
import { spawn, execFileSync } from 'node:child_process';
import { createWriteStream, existsSync, mkdirSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

export const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..', '..');
export const publishedWwwroot = path.join(repoRoot, 'artifacts', 'publish', 'wwwroot');

/** `dotnet run` on the client: the development experience (no-op service worker). Built in Release so it doesn't
 *  touch the Debug output a Visual Studio session may be serving. */
export function startDevServer(port) {
  return start(
    'dotnet',
    ['run', '--project', path.join(repoRoot, 'src', 'OfflineCrud.Client'), '-c', 'Release', '--no-launch-profile'],
    port,
    { ASPNETCORE_URLS: `http://localhost:${port}`, ASPNETCORE_ENVIRONMENT: 'Development' });
}

/** tools/serve.cs over the publish output: the real, caching service worker. Run `npm run publish` first. */
export function startPublishedServer(port) {
  if (!existsSync(path.join(publishedWwwroot, 'index.html'))) {
    throw new Error(`No published build at ${publishedWwwroot}. Run "npm run publish" (or "npm run test:published") first.`);
  }

  return start('dotnet', ['run', path.join(repoRoot, 'tools', 'serve.cs'), '--', publishedWwwroot, String(port)], port, {});
}

async function start(command, args, port, env) {
  const url = `http://localhost:${port}/`;
  const child = spawn(command, args, { cwd: repoRoot, env: { ...process.env, ...env }, stdio: ['ignore', 'pipe', 'pipe'] });

  // Keep the server's output: in memory for error messages, and in a log file for diagnosing slow or failed starts.
  const logPath = path.join(repoRoot, 'tests', 'e2e', 'test-results', `server-${port}.log`);
  mkdirSync(path.dirname(logPath), { recursive: true });
  const log = createWriteStream(logPath);
  let output = '';
  const record = chunk => { output += chunk; log.write(chunk); };
  child.stdout.on('data', record);
  child.stderr.on('data', record);
  child.on('error', error => record(`\nspawn error: ${error.message}\n`));
  child.on('exit', code => record(`\n[exited with code ${code}]\n`));

  const server = {
    url,
    // Kill the whole tree: `dotnet run` starts the real server as a child process.
    async stop() {
      if (child.exitCode !== null) return;
      if (process.platform === 'win32') {
        try { execFileSync('taskkill', ['/pid', String(child.pid), '/T', '/F'], { stdio: 'ignore' }); } catch { /* already gone */ }
      } else {
        child.kill('SIGTERM');
      }
      await waitUntil(async () => !(await isUp(url)), 15_000, `server on ${url} to stop`);
    },
  };

  try {
    // First run includes a build, so allow plenty of time.
    await waitUntil(async () => {
      if (child.exitCode !== null) throw new Error(`server exited early (code ${child.exitCode}):\n${output}`);
      return isUp(url);
    }, 180_000, `server on ${url}`);
  } catch (error) {
    await server.stop();
    throw error;
  }

  return server;
}

async function isUp(url) {
  try {
    const response = await fetch(url, { signal: AbortSignal.timeout(2_000) });
    return response.ok;
  } catch {
    return false;
  }
}

async function waitUntil(condition, timeoutMs, what) {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    if (await condition()) return;
    await new Promise(resolve => setTimeout(resolve, 500));
  }
  throw new Error(`Timed out after ${timeoutMs / 1000}s waiting for ${what}.`);
}
