export function createNpmInvocation({ nodeExecutable, npmCliPath, args }) {
  if (typeof nodeExecutable !== 'string' || nodeExecutable.length === 0) {
    throw new TypeError('A Node executable path is required.');
  }
  if (typeof npmCliPath !== 'string' || npmCliPath.length === 0) {
    throw new TypeError('The npm CLI path is unavailable. Run desktop preparation through npm.');
  }
  if (!Array.isArray(args)) {
    throw new TypeError('npm arguments must be an array.');
  }
  return {
    executable: nodeExecutable,
    args: [npmCliPath, ...args],
  };
}
