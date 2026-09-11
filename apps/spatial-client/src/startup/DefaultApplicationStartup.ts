import type { WorkspaceCommandResult } from '../navigation/WorkspaceCommandController.ts';

export type WorkspaceCommandHandler = Readonly<{
  handle(value: unknown): Promise<WorkspaceCommandResult>;
}>;

export type DefaultApplicationStartupResult = Readonly<{
  status: 'opened' | 'unavailable' | 'failed';
}>;

function record(value: unknown): Record<string, unknown> | null {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
    ? value as Record<string, unknown>
    : null;
}

export async function openDefaultChatGpt(
  handler: WorkspaceCommandHandler,
): Promise<DefaultApplicationStartupResult> {
  try {
    const search = await handler.handle({
      id: 'startup-chatgpt-search',
      command: 'application.search',
      args: { query: 'ChatGPT', limit: 5 },
    });
    if (!search.ok) return { status: 'failed' };

    const searchPayload = record(search.payload);
    if (!searchPayload) return { status: 'failed' };
    if (searchPayload.status !== 'resolved') return { status: 'unavailable' };

    const application = record(searchPayload.application);
    const applicationId = application?.id;
    if (typeof applicationId !== 'string' || applicationId.trim().length === 0) {
      return { status: 'failed' };
    }

    const opened = await handler.handle({
      id: 'startup-chatgpt-open',
      command: 'application.open',
      args: { applicationId, launchPolicy: 'reuseOrLaunch' },
    });
    return { status: opened.ok ? 'opened' : 'failed' };
  } catch {
    return { status: 'failed' };
  }
}
