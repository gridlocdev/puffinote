import { buildRoot } from './index.ts';
import { isExpectedError } from './infra/errors.ts';
import { Console, type CliIo } from './infra/io.ts';
import { ParseError, parseArgs, renderHelp } from './spec.ts';

const TOOL = 'puffinote';

/**
 * The whole tool, in-process: parse and run one invocation and return the exit code. Tests call this
 * with a capturing `CliIo`. A user error prints `puffinote: <message>` and exits 1; a bug is rethrown.
 */
export function runCli(argv: readonly string[], io: CliIo, version = '0.0.0'): number {
  const root = buildRoot(version);
  const con = new Console(io);
  let invocation;
  try {
    invocation = parseArgs(root, argv);
  } catch (e) {
    if (e instanceof ParseError) {
      // A parse error: the error on stderr, the help of the command it was in on stdout.
      con.errLine(e.message);
      con.errLine();
      const path = [TOOL];
      let command = root;
      for (const token of argv) {
        const child = command.subcommands?.find((c) => c.name === token);
        if (!child) break;
        command = child;
        path.push(child.name);
      }
      con.out(renderHelp(command, path, command === root ? version : null));
      return 1;
    }
    throw e;
  }
  try {
    switch (invocation.kind) {
      case 'help':
        con.out(
          renderHelp(invocation.command, invocation.path, invocation.command === root ? version : null),
        );
        return 0;
      case 'version':
        con.outLine(version);
        return 0;
      case 'run':
        return invocation.parsed.command.run!(invocation.parsed, con, root);
    }
  } catch (e) {
    if (isExpectedError(e)) {
      con.errLine(`${TOOL}: ${e.message}`);
      return 1;
    }
    throw e;
  }
}
