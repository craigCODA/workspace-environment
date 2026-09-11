const path = require('node:path');

const READY_MESSAGE = 'Workspace Host listening at ws://127.0.0.1:41771/workspace';
const MAX_DIAGNOSTIC_LENGTH = 8_192;

function resolveHostCommand({ isPackaged, resourcesPath, repoRoot, parentPid }) {
  if (!Number.isSafeInteger(parentPid) || parentPid <= 0) {
    throw new TypeError('parentPid must be a positive integer.');
  }

  if (isPackaged) {
    const cwd = path.resolve(resourcesPath, 'host');
    return {
      executable: path.resolve(cwd, 'Workspace.Host.exe'),
      args: ['--parent-pid', String(parentPid)],
      cwd,
    };
  }

  return {
    executable: 'dotnet',
    args: [
      'run',
      '--project',
      path.resolve(
        repoRoot,
        'apps',
        'host-windows',
        'src',
        'Workspace.Host',
        'Workspace.Host.csproj'),
      '--configuration',
      'Release',
      '--no-launch-profile',
      '--',
      '--parent-pid',
      String(parentPid),
    ],
    cwd: path.resolve(repoRoot),
  };
}

function waitForHostReady(child, { timeoutMs = 15_000 } = {}) {
  return new Promise((resolve, reject) => {
    let diagnostics = '';
    let settled = false;

    const appendDiagnostics = (chunk) => {
      diagnostics = `${diagnostics}${chunk.toString('utf8')}`.slice(-MAX_DIAGNOSTIC_LENGTH);
    };
    const onStdout = (chunk) => {
      appendDiagnostics(chunk);
      if (diagnostics.includes(READY_MESSAGE)) finish(resolve);
    };
    const onStderr = (chunk) => appendDiagnostics(chunk);
    const onError = (error) => finish(
      reject,
      new Error(`Workspace Host failed to start: ${error.message}`));
    const onExit = (code, signal) => finish(
      reject,
      new Error([
        `Workspace Host exited before it became ready (code ${code ?? 'none'}, signal ${signal ?? 'none'}).`,
        diagnostics.trim(),
      ].filter(Boolean).join('\n')));
    const timer = setTimeout(() => finish(
      reject,
      new Error(`Workspace Host did not become ready within ${timeoutMs} ms.\n${diagnostics.trim()}`.trim())), timeoutMs);

    function finish(callback, value) {
      if (settled) return;
      settled = true;
      clearTimeout(timer);
      child.stdout?.off('data', onStdout);
      child.stderr?.off('data', onStderr);
      child.off('error', onError);
      child.off('exit', onExit);
      callback(value);
    }

    child.stdout?.on('data', onStdout);
    child.stderr?.on('data', onStderr);
    child.on('error', onError);
    child.on('exit', onExit);

    if (child.exitCode !== null && child.exitCode !== undefined) {
      onExit(child.exitCode, child.signalCode);
    }
  });
}

module.exports = {
  READY_MESSAGE,
  resolveHostCommand,
  waitForHostReady,
};
