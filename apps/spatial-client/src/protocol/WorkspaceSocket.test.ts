import assert from 'node:assert/strict';
import test from 'node:test';
import { WorkspaceSocket, type SocketLike } from './WorkspaceSocket.ts';

class FakeSocket implements SocketLike {
  readonly sent: string[] = [];
  readyState = 1;
  onmessage: ((event: { data: string }) => void) | null = null;
  onclose: (() => void) | null = null;
  onerror: (() => void) | null = null;
  sendError: Error | null = null;
  closeCalled = false;

  send(data: string): void {
    if (this.sendError) throw this.sendError;
    this.sent.push(data);
  }

  receive(message: unknown): void {
    this.onmessage?.({ data: JSON.stringify(message) });
  }

  close(): void {
    this.closeCalled = true;
    this.readyState = 3;
  }
}

test('uses the loopback-only V0 endpoint', () => {
  let requestedUrl = '';
  new WorkspaceSocket((url) => {
    requestedUrl = url;
    return new FakeSocket();
  });

  assert.equal(requestedUrl, 'ws://127.0.0.1:41771/workspace');
});

test('resolves out-of-order command results by correlation id', async () => {
  const socket = new FakeSocket();
  const workspace = new WorkspaceSocket(() => socket);
  const first = workspace.sendCommand('application.list');
  const second = workspace.sendCommand('window.focus', 'pc.window:pc.application:notepad');
  const [firstCommand, secondCommand] = socket.sent.map((value) => JSON.parse(value));

  socket.receive({ protocol: 1, type: 'result', id: secondCommand.id, success: true, payload: 'focused' });
  socket.receive({ protocol: 1, type: 'result', id: firstCommand.id, success: true, payload: ['Notepad'] });

  assert.equal(await second, 'focused');
  assert.deepEqual(await first, ['Notepad']);
});

test('rejects a correlated host error exactly once', async () => {
  const socket = new FakeSocket();
  const workspace = new WorkspaceSocket(() => socket);
  const pending = workspace.sendCommand('application.launch', undefined, { applicationId: 'missing' });
  const command = JSON.parse(socket.sent[0]!);

  socket.receive({
    protocol: 1,
    type: 'error',
    id: command.id,
    code: 'application_not_found',
    message: 'Application was not found.',
  });

  await assert.rejects(pending, /Application was not found/);
});

test('rejects malformed protocol results instead of leaving commands pending', async () => {
  const socket = new FakeSocket();
  const workspace = new WorkspaceSocket(() => socket);
  const pending = workspace.sendCommand('application.list');

  socket.receive({ protocol: 1, type: 'result' });

  await assert.rejects(
    Promise.race([
      pending,
      new Promise((_, reject) => setTimeout(() => reject(new Error('command remained pending')), 50)),
    ]),
    /invalid workspace protocol/i,
  );
});

test('returns a rejected promise when the socket send throws', async () => {
  const socket = new FakeSocket();
  socket.sendError = new Error('send failed');
  const workspace = new WorkspaceSocket(() => socket);

  const pending = workspace.sendCommand('application.list');

  await assert.rejects(pending, /send failed/);
});

test('closes the underlying connection when the client is disposed', () => {
  const socket = new FakeSocket();
  const workspace = new WorkspaceSocket(() => socket);

  workspace.close();

  assert.equal(socket.closeCalled, true);
});
