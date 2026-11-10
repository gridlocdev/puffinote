import { existsSync, statSync } from 'node:fs';
import path from 'node:path';
import { cat, html, outline, stats, tokens } from './commands/markdown.ts';
import { info, search, tree } from './commands/notebook.ts';
import { CliError } from './infra/errors.ts';
import { renderHelp, type CommandSpec } from './spec.ts';

export const ROOT_DESCRIPTION = 'Puffinote — Markdown notebook tools for scripts and CI.';

/**
 * The command tree. `puffinote <path>` with no verb opens the notebook in the app (a file opens the
 * folder it is in); bare `puffinote` prints help.
 */
export function buildRoot(version: string): CommandSpec {
  const root: CommandSpec = {
    name: 'puffinote',
    description: ROOT_DESCRIPTION,
    args: [
      {
        name: 'path',
        description: 'Notebook folder to open in Puffinote (a file opens the folder it is in)',
        arity: 'optional',
      },
    ],
    subcommands: [tree, search, info, html, cat, tokens, stats, outline],
    run(p, con) {
      const target = p.argOrNull('path');
      if (target === null) {
        con.out(renderHelp(root, ['puffinote'], version));
        return 0;
      }
      const full = path.resolve(target);
      if (!existsSync(full)) throw new CliError(`'${target}' does not exist.`);
      return con.io.launch(statSync(full).isDirectory() ? full : path.dirname(full));
    },
  };
  return root;
}
