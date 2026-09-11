import path from 'node:path';

export function resolveElectronProvisioning({
  packageDirectory,
  platform,
  binaryExists,
}) {
  if (typeof packageDirectory !== 'string' || packageDirectory.length === 0) {
    throw new TypeError('Electron package directory is required.');
  }
  const paths = platform === 'win32' ? path.win32 : path.posix;
  return {
    installerPath: paths.join(packageDirectory, 'install.js'),
    executablePath: paths.join(
      packageDirectory,
      'dist',
      platform === 'win32' ? 'electron.exe' : 'electron'),
    installRequired: !binaryExists,
  };
}
