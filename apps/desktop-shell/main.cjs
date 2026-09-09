const { spawn, spawnSync } = require('node:child_process');
const { appendFileSync, existsSync, mkdirSync } = require('node:fs');
const path = require('node:path');
const { app, BrowserWindow, dialog, session } = require('electron');

const { resolveHostCommand, waitForHostReady } = require('./lib/host-process.cjs');
const { startStaticServer } = require('./lib/static-server.cjs');

const APP_ID = 'com.nerdlife.workspaceenvironment';
const repoRoot = path.resolve(__dirname, '..', '..');

let mainWindow = null;
let hostProcess = null;
let rendererServer = null;
let rendererOrigin = null;
let hostStartupCompleted = false;
let hostStartupFailure = null;
let cleanupPromise = null;
let cleanupFinished = false;
let isQuitting = false;
let logPath = null;

if (!app.requestSingleInstanceLock()) {
  app.quit();
} else {
  app.setAppUserModelId(APP_ID);

  app.on('second-instance', () => {
    if (!mainWindow) return;
    if (mainWindow.isMinimized()) mainWindow.restore();
    mainWindow.show();
    mainWindow.focus();
  });

  app.on('before-quit', (event) => {
    isQuitting = true;
    if (cleanupFinished) return;
    event.preventDefault();
    void cleanup().finally(() => {
      cleanupFinished = true;
      app.quit();
    });
  });

  app.on('window-all-closed', () => app.quit());

  void app.whenReady()
    .then(startApplication)
    .catch((error) => failStartup(error));
}

async function startApplication() {
  initializeLogging();
  log(`Starting Workspace Environment ${app.getVersion()} (${app.isPackaged ? 'packaged' : 'development'}).`);

  const rendererRoot = app.isPackaged
    ? path.join(process.resourcesPath, 'spatial-client')
    : path.join(repoRoot, 'apps', 'spatial-client', 'dist');
  const command = resolveHostCommand({
    isPackaged: app.isPackaged,
    resourcesPath: process.resourcesPath,
    repoRoot,
    parentPid: process.pid,
  });

  if (app.isPackaged && !existsSync(command.executable)) {
    throw new Error(`The bundled Windows host is missing: ${command.executable}`);
  }
  if (!existsSync(rendererRoot)) {
    throw new Error(`The spatial client build is missing: ${rendererRoot}`);
  }

  hostProcess = spawn(command.executable, command.args, {
    cwd: command.cwd,
    windowsHide: true,
    shell: false,
    stdio: ['ignore', 'pipe', 'pipe'],
    env: {
      ...process.env,
      DOTNET_CLI_TELEMETRY_OPTOUT: '1',
      DOTNET_NOLOGO: '1',
    },
  });
  hostProcess.stdout.on('data', (chunk) => log(`[host] ${chunk.toString('utf8').trimEnd()}`));
  hostProcess.stderr.on('data', (chunk) => log(`[host:error] ${chunk.toString('utf8').trimEnd()}`));
  hostProcess.once('exit', (code, signal) => {
    log(`Workspace Host exited (code ${code ?? 'none'}, signal ${signal ?? 'none'}).`);
    if (!hostStartupCompleted) {
      hostStartupFailure = new Error(`Workspace Host exited during startup with code ${code ?? 'none'}.`);
      return;
    }
    if (!isQuitting) {
      failStartup(new Error('The Windows Workspace Host stopped unexpectedly.'));
    }
  });

  await waitForHostReady(hostProcess);
  hostStartupCompleted = true;
  if (hostStartupFailure || hostProcess.exitCode !== null) {
    throw hostStartupFailure ?? new Error('Workspace Host exited during startup.');
  }

  rendererServer = await startStaticServer(rendererRoot);
  rendererOrigin = rendererServer.origin;
  log(`Renderer available at ${rendererOrigin}.`);

  session.defaultSession.setPermissionRequestHandler((_webContents, _permission, callback) => {
    callback(false);
  });
  await createMainWindow();
}

async function createMainWindow() {
  if (!rendererOrigin) throw new Error('Renderer origin is not available.');

  const window = new BrowserWindow({
    width: 1440,
    height: 960,
    minWidth: 960,
    minHeight: 640,
    show: false,
    title: 'Workspace Environment',
    backgroundColor: '#080b0c',
    autoHideMenuBar: true,
    webPreferences: {
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: true,
      webSecurity: true,
      devTools: !app.isPackaged,
    },
  });
  mainWindow = window;

  window.setMenuBarVisibility(false);
  window.webContents.setWindowOpenHandler(() => ({ action: 'deny' }));
  window.webContents.on('will-navigate', (event, targetUrl) => {
    let targetOrigin = null;
    try {
      targetOrigin = new URL(targetUrl).origin;
    } catch {
      // Invalid navigation is denied below.
    }
    if (targetOrigin !== rendererOrigin) event.preventDefault();
  });
  window.webContents.on('render-process-gone', (_event, details) => {
    log(`Renderer process exited (${details.reason}, code ${details.exitCode}).`);
    if (!isQuitting) failStartup(new Error('The spatial renderer stopped unexpectedly.'));
  });
  window.once('ready-to-show', () => window.show());
  window.on('closed', () => {
    if (mainWindow === window) mainWindow = null;
  });

  await window.loadURL(rendererOrigin);
}

function initializeLogging() {
  const directory = path.join(app.getPath('userData'), 'logs');
  mkdirSync(directory, { recursive: true });
  logPath = path.join(directory, 'desktop-shell.log');
}

function log(message) {
  if (!logPath) return;
  try {
    appendFileSync(logPath, `${new Date().toISOString()} ${message}\n`, 'utf8');
  } catch {
    // Logging must never replace the original startup or shutdown failure.
  }
}

function failStartup(error) {
  if (isQuitting) return;
  isQuitting = true;
  log(`Fatal desktop-shell error: ${error?.stack ?? error}`);
  const detail = error instanceof Error ? error.message : String(error);
  dialog.showErrorBox(
    'Workspace Environment could not start',
    `${detail}\n\nDesktop log: ${logPath ?? 'unavailable'}`);
  app.quit();
}

function cleanup() {
  if (cleanupPromise) return cleanupPromise;
  cleanupPromise = (async () => {
    if (rendererServer) {
      try {
        await rendererServer.close();
      } catch (error) {
        log(`Renderer server shutdown failed: ${error.message}`);
      }
      rendererServer = null;
    }

    if (hostProcess && hostProcess.exitCode === null) {
      const ownedPid = hostProcess.pid;
      hostProcess.kill();
      await waitForExit(hostProcess, 2_000);
      if (hostProcess.exitCode === null && Number.isSafeInteger(ownedPid)) {
        spawnSync('taskkill.exe', ['/PID', String(ownedPid), '/T', '/F'], {
          windowsHide: true,
          stdio: 'ignore',
        });
        await waitForExit(hostProcess, 2_000);
      }
    }
    hostProcess = null;
  })();
  return cleanupPromise;
}

function waitForExit(child, timeoutMs) {
  if (child.exitCode !== null) return Promise.resolve();
  return new Promise((resolve) => {
    const timer = setTimeout(finish, timeoutMs);
    child.once('exit', finish);
    function finish() {
      clearTimeout(timer);
      child.off('exit', finish);
      resolve();
    }
  });
}
