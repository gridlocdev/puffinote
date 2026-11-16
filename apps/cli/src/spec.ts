import { CliError } from './infra/errors.ts';
import type { Console } from './infra/io.ts';

/**
 * The declarative command tree. Parsing and `--help` are both generated
 * from it, so a verb's documentation cannot drift from what it accepts.
 */

/**
 * - `flag`: a switch (`--fragment`); a following `true`/`false` is accepted, as System.CommandLine did.
 * - `value`: takes the next token (`--sheet Data`, `--sheet=Data`).
 * - `optionalValue`: a tri-state switch (`--bold`, `--bold false`); it takes the next token only when
 *   that is a yes/no word or every positional argument is already filled.
 * - `multi`: a repeatable value (`--values B2:B9 --values C2:C9`).
 */
export type OptionKind = 'flag' | 'value' | 'optionalValue' | 'multi';
export type ValueType = 'string' | 'int' | 'number';

export interface OptionSpec {
  readonly name: string;
  readonly aliases?: readonly string[];
  readonly description: string;
  readonly kind?: OptionKind;
  readonly type?: ValueType;
  readonly default?: string;
  readonly required?: boolean;
  /**
   * A `multi` option that also takes the tokens after its first value (`--values B2:B9 C2:C9`), as
   * System.CommandLine's AllowMultipleArgumentsPerToken did — once every positional is filled.
   */
  readonly greedy?: boolean;
}

export interface ArgumentSpec {
  readonly name: string;
  readonly description: string;
  /** `one` (default), `optional` (zero or one), or `many` (one or more; must be last). */
  readonly arity?: 'one' | 'optional' | 'many';
  readonly type?: ValueType;
}

export type RunCommand = (p: Parsed, con: Console, root: CommandSpec) => number;

export interface CommandSpec {
  readonly name: string;
  readonly description: string;
  readonly args?: readonly ArgumentSpec[];
  readonly options?: readonly OptionSpec[];
  readonly subcommands?: readonly CommandSpec[];
  readonly run?: RunCommand;
}

/** A parse problem (unknown option, missing argument): the user's to fix, like `CliError`. */
export class ParseError extends CliError {
  constructor(message: string) {
    super(message);
    this.name = 'ParseError';
  }
}

const YES_NO = new Set(['true', 'false', 'on', 'off', 'yes', 'no']);

/** The values one invocation carried, read by option name (`--sheet`) or argument name (`file`). */
export class Parsed {
  readonly command: CommandSpec;
  readonly path: readonly string[];
  readonly #args = new Map<string, string[]>();
  readonly #options = new Map<string, string[]>();

  constructor(command: CommandSpec, path: readonly string[]) {
    this.command = command;
    this.path = path;
  }

  setArg(name: string, values: string[]): void {
    this.#args.set(name, values);
  }

  addOption(name: string, value: string): void {
    const list = this.#options.get(name) ?? [];
    list.push(value);
    this.#options.set(name, list);
  }

  /** A required argument. */
  arg(name: string): string {
    const v = this.#args.get(name)?.[0];
    if (v === undefined)
      throw new ParseError(`Required argument missing for command: '${this.command.name}'.`);
    return v;
  }

  /** An optional argument (null when absent). */
  argOrNull(name: string): string | null {
    return this.#args.get(name)?.[0] ?? null;
  }

  /** A one-or-more argument. */
  args(name: string): string[] {
    return this.#args.get(name) ?? [];
  }

  /** An argument typed as a number. */
  argNumber(name: string): number {
    return parseNumber(this.arg(name), `argument '${name}'`, this.#argSpec(name)?.type ?? 'number');
  }

  /** Whether the option appeared on the command line. */
  has(option: string): boolean {
    return this.#options.has(option);
  }

  /** A value option's text: the last one given, else its default, else null. */
  str(option: string): string | null {
    const values = this.#options.get(option);
    if (values && values.length > 0) return values[values.length - 1]!;
    return this.#optionSpec(option)?.default ?? null;
  }

  /** A switch. */
  flag(option: string): boolean {
    const values = this.#options.get(option);
    if (!values || values.length === 0) return false;
    return values[values.length - 1]!.toLowerCase() !== 'false';
  }

  /** Every value of a repeatable option. */
  multi(option: string): string[] {
    return this.#options.get(option) ?? [];
  }

  /** A numeric option (int or number per its spec), or null when absent. */
  num(option: string): number | null {
    const text = this.str(option);
    if (text === null) return null;
    return parseNumber(text, `option '${option}'`, this.#optionSpec(option)?.type ?? 'number');
  }

  /**
   * A tri-state switch: absent → null (leave the attribute alone); `--bold`, `--bold true|on|yes` →
   * true; `--bold false|off|no` → false.
   */
  onOff(option: string): boolean | null {
    const values = this.#options.get(option);
    if (!values) return null;
    const value = (values[values.length - 1] ?? '').toLowerCase();
    if (value === '' || value === 'true' || value === 'on' || value === 'yes') return true;
    if (value === 'false' || value === 'off' || value === 'no') return false;
    throw new CliError(`'${values[values.length - 1]}' must be true or false.`);
  }

  #optionSpec(name: string): OptionSpec | undefined {
    return this.command.options?.find((o) => o.name === name);
  }

  #argSpec(name: string): ArgumentSpec | undefined {
    return this.command.args?.find((a) => a.name === name);
  }
}

function parseNumber(text: string, what: string, type: ValueType): number {
  const trimmed = text.trim();
  const n = trimmed === '' ? Number.NaN : Number(trimmed);
  if (!Number.isFinite(n) || (type === 'int' && !Number.isInteger(n))) {
    const expected = type === 'int' ? 'a whole number' : 'a number';
    throw new ParseError(`Cannot parse '${text}' for ${what}: expected ${expected}.`);
  }
  return n;
}

/** What the parser decided to do. */
export type Invocation =
  | { readonly kind: 'run'; readonly parsed: Parsed }
  | { readonly kind: 'help'; readonly command: CommandSpec; readonly path: readonly string[] }
  | { readonly kind: 'version' };

const HELP = new Set(['--help', '-h', '-?']);

function findOption(command: CommandSpec, token: string): OptionSpec | undefined {
  return command.options?.find((o) => o.name === token || (o.aliases?.includes(token) ?? false));
}

function looksLikeOption(token: string): boolean {
  return token.length > 1 && token.startsWith('-') && !/^-\d|^-\.\d/.test(token);
}

/** Parse argv (without the program name) against the tree rooted at `root`. */
export function parseArgs(root: CommandSpec, argv: readonly string[]): Invocation {
  let command = root;
  const path: string[] = [root.name];
  let i = 0;
  // Descend into subcommands while the next token names one.
  while (i < argv.length) {
    const token = argv[i]!;
    if (HELP.has(token)) return { kind: 'help', command, path };
    if (command === root && token === '--version') return { kind: 'version' };
    const child = command.subcommands?.find((c) => c.name === token);
    if (!child) break;
    command = child;
    path.push(child.name);
    i++;
  }

  const parsed = new Parsed(command, path);
  const positionals: string[] = [];
  const specs = command.args ?? [];
  const slots = specs.some((a) => a.arity === 'many') ? Infinity : specs.length;
  let literal = false;
  for (; i < argv.length; i++) {
    const token = argv[i]!;
    if (literal) {
      positionals.push(token);
      continue;
    }
    if (token === '--') {
      literal = true;
      continue;
    }
    if (HELP.has(token)) return { kind: 'help', command, path };
    let name = token;
    let inline: string | null = null;
    if (token.startsWith('--') && token.includes('=')) {
      name = token.slice(0, token.indexOf('='));
      inline = token.slice(token.indexOf('=') + 1);
    }
    const option = looksLikeOption(token) ? findOption(command, name) : undefined;
    if (!option) {
      if (looksLikeOption(token)) throw new ParseError(`Unrecognized command or argument '${token}'.`);
      positionals.push(token);
      continue;
    }
    const kind = option.kind ?? 'value';
    const next = argv[i + 1];
    switch (kind) {
      case 'flag': {
        if (inline !== null) parsed.addOption(option.name, inline);
        else if (next !== undefined && /^(true|false)$/i.test(next)) {
          parsed.addOption(option.name, next);
          i++;
        } else parsed.addOption(option.name, 'true');
        break;
      }
      case 'optionalValue': {
        if (inline !== null) parsed.addOption(option.name, inline);
        else if (
          next !== undefined &&
          !looksLikeOption(next) &&
          (YES_NO.has(next.toLowerCase()) || positionals.length >= slots)
        ) {
          parsed.addOption(option.name, next);
          i++;
        } else parsed.addOption(option.name, '');
        break;
      }
      case 'multi':
      case 'value': {
        if (inline !== null) parsed.addOption(option.name, inline);
        else {
          if (next === undefined)
            throw new ParseError(`Required argument missing for option: '${option.name}'.`);
          parsed.addOption(option.name, next);
          i++;
          if (kind === 'multi' && option.greedy && inline === null) {
            while (
              i + 1 < argv.length &&
              !looksLikeOption(argv[i + 1]!) &&
              argv[i + 1] !== '--' &&
              positionals.length >= slots
            ) {
              parsed.addOption(option.name, argv[++i]!);
            }
          }
        }
      }
    }
  }

  if (!command.run) {
    if (positionals.length > 0) throw new ParseError(`Unrecognized command or argument '${positionals[0]}'.`);
    throw new ParseError('Required command was not provided.');
  }

  let p = 0;
  for (const spec of specs) {
    const arity = spec.arity ?? 'one';
    if (arity === 'many') {
      if (p >= positionals.length) {
        throw new ParseError(`Required argument missing for command: '${command.name}'.`);
      }
      parsed.setArg(spec.name, positionals.slice(p));
      p = positionals.length;
    } else if (p < positionals.length) {
      parsed.setArg(spec.name, [positionals[p++]!]);
    } else if (arity === 'one') {
      throw new ParseError(`Required argument missing for command: '${command.name}'.`);
    }
  }
  if (p < positionals.length) throw new ParseError(`Unrecognized command or argument '${positionals[p]}'.`);

  for (const option of command.options ?? []) {
    if (option.required && !parsed.has(option.name))
      throw new ParseError(`Option '${option.name}' is required.`);
    if (parsed.has(option.name) && (option.type === 'int' || option.type === 'number'))
      parsed.num(option.name);
  }
  for (const spec of specs) {
    if ((spec.type === 'int' || spec.type === 'number') && parsed.argOrNull(spec.name) !== null) {
      parsed.argNumber(spec.name);
    }
  }
  return { kind: 'run', parsed };
}

// ---- Help ------------------------------------------------------------------

export function argumentLabel(a: ArgumentSpec): string {
  if (a.arity === 'optional') return `[<${a.name}>]`;
  if (a.arity === 'many') return `<${a.name}>...`;
  return `<${a.name}>`;
}

/** `-s, --sheet` — aliases shortest first, then the name. */
export function optionNames(o: OptionSpec): string {
  return [...(o.aliases ?? [])]
    .sort((a, b) => a.length - b.length)
    .concat(o.name)
    .join(', ');
}

export function isFlag(o: OptionSpec): boolean {
  return (o.kind ?? 'value') === 'flag';
}

function twoColumns(rows: readonly (readonly [string, string])[]): string {
  const width = Math.max(0, ...rows.map(([a]) => a.length));
  return rows.map(([a, b]) => `  ${a.padEnd(width)}  ${b}`.trimEnd()).join('\n');
}

/** `--help` for one command, in System.CommandLine's layout. */
export function renderHelp(command: CommandSpec, path: readonly string[], version: string | null): string {
  const out: string[] = [];
  out.push('Description:', `  ${command.description}`, '');
  const usage = [path.join(' ')];
  for (const a of command.args ?? []) usage.push(argumentLabel(a));
  if (command.subcommands && command.subcommands.length > 0) usage.push('[command]');
  usage.push('[options]');
  out.push('Usage:', `  ${usage.join(' ')}`, '');
  if (command.args && command.args.length > 0) {
    out.push('Arguments:', twoColumns(command.args.map((a) => [argumentLabel(a), a.description])), '');
  }
  const options: [string, string][] = (command.options ?? []).map((o) => {
    const names = optionNames(o);
    const spelled = isFlag(o) ? names : `${names} <${o.name.replace(/^-+/, '')}>`;
    const required = o.required ? ' (REQUIRED)' : '';
    const fallback = o.default !== undefined ? ` [default: ${o.default}]` : '';
    return [spelled + required, o.description + fallback];
  });
  options.push(['-?, -h, --help', 'Show help and usage information']);
  if (version !== null) options.push(['--version', 'Show version information']);
  out.push('Options:', twoColumns(options), '');
  if (command.subcommands && command.subcommands.length > 0) {
    out.push(
      'Commands:',
      twoColumns(
        command.subcommands.map((c) => [
          [c.name, ...(c.args ?? []).map(argumentLabel)].join(' '),
          c.description,
        ]),
      ),
      '',
    );
  }
  return out.join('\n') + '\n';
}
