import './styles.css';
import { createWorkspaceApp } from './app/createWorkspaceApp.ts';

const root = document.querySelector<HTMLElement>('#app');
if (!root) {
  throw new Error('Workspace Environment root was not found.');
}

createWorkspaceApp(root);
