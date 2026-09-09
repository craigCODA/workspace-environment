const { createReadStream } = require('node:fs');
const { stat } = require('node:fs/promises');
const http = require('node:http');
const path = require('node:path');

const CONTENT_SECURITY_POLICY = [
  "default-src 'self'",
  "script-src 'self'",
  "style-src 'self'",
  "img-src 'self' data: blob:",
  "connect-src 'self' ws://127.0.0.1:41771",
  "font-src 'self'",
  "object-src 'none'",
  "base-uri 'none'",
  "frame-ancestors 'none'",
].join('; ');

const MIME_TYPES = new Map([
  ['.css', 'text/css; charset=utf-8'],
  ['.html', 'text/html; charset=utf-8'],
  ['.ico', 'image/x-icon'],
  ['.jpeg', 'image/jpeg'],
  ['.jpg', 'image/jpeg'],
  ['.js', 'text/javascript; charset=utf-8'],
  ['.json', 'application/json; charset=utf-8'],
  ['.map', 'application/json; charset=utf-8'],
  ['.png', 'image/png'],
  ['.svg', 'image/svg+xml'],
  ['.wasm', 'application/wasm'],
  ['.webp', 'image/webp'],
]);

async function startStaticServer(rootDirectory) {
  const root = path.resolve(rootDirectory);
  const rootInfo = await stat(root);
  if (!rootInfo.isDirectory()) {
    throw new Error(`Renderer root is not a directory: ${root}`);
  }

  const server = http.createServer((request, response) => {
    void handleRequest(root, request, response).catch(() => {
      if (!response.headersSent) sendText(response, 500, 'Internal server error');
      else response.destroy();
    });
  });

  await new Promise((resolve, reject) => {
    server.once('error', reject);
    server.listen(0, '127.0.0.1', () => {
      server.off('error', reject);
      resolve();
    });
  });

  const address = server.address();
  if (!address || typeof address === 'string') {
    server.close();
    throw new Error('Renderer server did not bind to a TCP address.');
  }

  return {
    origin: `http://127.0.0.1:${address.port}`,
    close: () => new Promise((resolve, reject) => {
      server.close((error) => error ? reject(error) : resolve());
      server.closeAllConnections();
    }),
  };
}

async function handleRequest(root, request, response) {
  setSecurityHeaders(response);

  let pathname;
  try {
    pathname = decodeURIComponent(new URL(request.url ?? '/', 'http://127.0.0.1').pathname);
  } catch {
    sendText(response, 400, 'Bad request');
    return;
  }

  if (pathname.includes('\0')) {
    sendText(response, 400, 'Bad request');
    return;
  }

  const relativePath = pathname.replace(/^\/+/, '') || 'index.html';
  const candidate = path.resolve(root, relativePath);
  if (!isWithin(root, candidate)) {
    sendText(response, 403, 'Forbidden');
    return;
  }

  if (await isFile(candidate)) {
    streamFile(candidate, response);
    return;
  }

  const acceptsHtml = (request.headers.accept ?? '').includes('text/html');
  if (acceptsHtml && path.extname(relativePath) === '') {
    const indexPath = path.join(root, 'index.html');
    if (await isFile(indexPath)) {
      streamFile(indexPath, response);
      return;
    }
  }

  sendText(response, 404, 'Not found');
}

function isWithin(root, candidate) {
  const relative = path.relative(root, candidate);
  return relative === '' || (!relative.startsWith('..') && !path.isAbsolute(relative));
}

async function isFile(candidate) {
  try {
    return (await stat(candidate)).isFile();
  } catch (error) {
    if (error?.code === 'ENOENT' || error?.code === 'ENOTDIR') return false;
    throw error;
  }
}

function streamFile(filePath, response) {
  response.statusCode = 200;
  response.setHeader(
    'Content-Type',
    MIME_TYPES.get(path.extname(filePath).toLowerCase()) ?? 'application/octet-stream');
  const stream = createReadStream(filePath);
  stream.on('error', () => {
    if (!response.headersSent) sendText(response, 500, 'Internal server error');
    else response.destroy();
  });
  stream.pipe(response);
}

function setSecurityHeaders(response) {
  response.setHeader('Content-Security-Policy', CONTENT_SECURITY_POLICY);
  response.setHeader('Cross-Origin-Opener-Policy', 'same-origin');
  response.setHeader('X-Content-Type-Options', 'nosniff');
  response.setHeader('Referrer-Policy', 'no-referrer');
}

function sendText(response, statusCode, body) {
  response.statusCode = statusCode;
  response.setHeader('Content-Type', 'text/plain; charset=utf-8');
  response.end(body);
}

module.exports = {
  CONTENT_SECURITY_POLICY,
  startStaticServer,
};
