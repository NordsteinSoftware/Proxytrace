// Resolves a fixture entry against the arguments of the call it is answering.
//
// A constant is a fine answer for a read of a world that already exists. It is the wrong answer the
// moment a scenario WRITES: a model that posts a correction and reads back `isCorrection: false`
// concludes its own correct write did not land, retries, and burns the step budget — a harness
// artifact that reads in the report as a prompt regression. Same for a by-id read that answers about
// the same entity whatever id it was handed: probing two suites then looks like one duplicated suite.
//
// So a fixture entry may echo the request back the way a real API does. Everything here is
// declarative — the fixture file stays JSON, with no functions in it — and there are only three
// shapes plus a small token vocabulary:
//
//   `_byArg`   pick a branch by the value of an argument      → by-id worlds, and `notFound` misses
//   `_forEach` one output entry per element of an array arg   → per-item echo (added cases, handles)
//   `_like`    start from another tool's entry, then override → keeps one suite body, not five copies
//
// Tokens (whole-string values only, so JSON types survive):
//
//   `$args.x`      the argument (dot paths allowed); missing → null
//   `$item`        the current `_forEach` element; `$item.x` a field of it
//   `$has.args.x`  / `$has.item.x` → boolean "was it sent?" (undefined and null are both false)
//   `$count.args.x` / `$count.item.x` → length of an array argument (0 when absent)
//   `$index`       0-based position within the current `_forEach`
//   `$uuid`        a stable id derived from the tool + item + position (same input → same id)
//   `$$…`          a literal string that happens to start with `$`
//
// Keys starting with `_` are dropped from resolved objects, so `_comment` works anywhere.

/** Matches a string that is a token rather than literal text. */
const TOKEN = /^\$(args|item|has|count|index|uuid)(\.|$)/;

const isPlainObject = (value) => value !== null && typeof value === 'object' && !Array.isArray(value);

function readPath(source, path) {
  let value = source;
  for (const key of path) {
    if (value === null || typeof value !== 'object') return undefined;
    value = value[key];
  }
  return value;
}

/**
 * A deterministic, UUID-shaped id. Ids minted by a fixture have to be stable across the two variants
 * of an A/B (and across `--repeat`), or every diff in the report would be dominated by fresh ids —
 * which also rules out randomness and the clock.
 */
function stableUuid(seed) {
  const hex = [0x811c9dc5, 0x01000193, 0x85ebca6b, 0xc2b2ae35]
    .map((offset) => {
      let hash = offset >>> 0;
      for (let i = 0; i < seed.length; i++) {
        hash = Math.imul(hash ^ seed.charCodeAt(i), 0x01000193) >>> 0;
      }
      return hash.toString(16).padStart(8, '0');
    })
    .join('');
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-4${hex.slice(13, 16)}-8${hex.slice(17, 20)}-${hex.slice(20, 32)}`;
}

function resolveToken(token, args, scope, ctx) {
  if (token === '$index') return scope.index ?? 0;
  if (token === '$uuid') {
    return stableUuid(JSON.stringify([ctx.toolName ?? '', scope.path ?? '', scope.index ?? 0, scope.item ?? args ?? null]));
  }
  const [head, ...path] = token.slice(1).split('.');
  if (head === 'has') {
    const value = readSource(path[0], args, scope, path.slice(1));
    return value !== undefined && value !== null;
  }
  if (head === 'count') {
    const value = readSource(path[0], args, scope, path.slice(1));
    return Array.isArray(value) ? value.length : 0;
  }
  const value = readSource(head, args, scope, path);
  return value === undefined ? null : value;
}

function readSource(name, args, scope, path) {
  if (name === 'args') return readPath(args, path);
  if (name === 'item') return readPath(scope.item, path);
  return undefined;
}

/** A selector is either a token (`$item.kind`) or an argument path (`suiteId`, `handle.id`). */
function readSelector(selector, args, scope, ctx) {
  return selector.startsWith('$')
    ? resolveToken(selector, args, scope, ctx)
    : readPath(args, selector.split('.'));
}

/** The sibling keys of a `_`-prefixed directive, resolved — the overrides applied on top of it. */
function resolveSiblings(node, args, scope, ctx) {
  const out = {};
  for (const [key, value] of Object.entries(node)) {
    if (key.startsWith('_')) continue;
    out[key] = resolve(value, args, scope, ctx);
  }
  return out;
}

function resolveByArg(node, args, scope, ctx) {
  const value = readSelector(node._byArg, args, scope, ctx);
  const key = value === undefined || value === null ? '' : String(value);
  const cases = node._cases ?? {};
  if (Object.hasOwn(cases, key)) return resolve(cases[key], args, scope, ctx);
  if (Object.hasOwn(node, '_default')) return resolve(node._default, args, scope, ctx);
  // No branch and no default: the world does not contain that entity. Answer the way every by-id
  // tool answers a 404 (`ignore404` in tools/shared.ts), so the model can re-list or ask rather than
  // read another entity's data as if it were the one it asked for.
  return { notFound: value ?? null };
}

function resolveForEach(node, args, scope, ctx) {
  const list = readSelector(node._forEach, args, scope, ctx);
  if (!Array.isArray(list) || list.length === 0) {
    return Object.hasOwn(node, '_empty') ? resolve(node._empty, args, scope, ctx) : [];
  }
  return list.map((item, index) =>
    resolve(node._item, args, { item, index, path: `${scope.path ?? ''}/${node._forEach}` }, ctx));
}

function resolveLike(node, args, scope, ctx) {
  const name = node._like;
  if (ctx.seen?.includes(name)) {
    throw new Error(`fixture "${ctx.toolName}" has a circular _like chain through "${name}"`);
  }
  const target = ctx.fixtures?.[name];
  if (target === undefined) {
    throw new Error(`fixture "${ctx.toolName}" references unknown entry "${name}" via _like`);
  }
  const base = resolve(target, args, scope, {
    ...ctx,
    toolName: name,
    seen: [...(ctx.seen ?? []), ctx.toolName],
  });
  // A miss stays a miss: the real write tools look the entity up first and return `{ notFound }`
  // without mutating, so decorating it with `addedCases` would invent a write that never happened.
  if (!isPlainObject(base) || Object.hasOwn(base, 'notFound')) return base;
  return { ...base, ...resolveSiblings(node, args, scope, ctx) };
}

function resolve(node, args, scope, ctx) {
  if (typeof node === 'string') {
    if (node.startsWith('$$')) return node.slice(1);
    return TOKEN.test(node) ? resolveToken(node, args, scope, ctx) : node;
  }
  if (Array.isArray(node)) return node.map((entry) => resolve(entry, args, scope, ctx));
  if (isPlainObject(node)) {
    if (Object.hasOwn(node, '_byArg')) return resolveByArg(node, args, scope, ctx);
    if (Object.hasOwn(node, '_forEach')) return resolveForEach(node, args, scope, ctx);
    if (Object.hasOwn(node, '_like')) return resolveLike(node, args, scope, ctx);
    return resolveSiblings(node, args, scope, ctx);
  }
  return node;
}

/**
 * The canned result for one tool call, or `undefined` when the world has no entry for that tool
 * (which the runner reports as a gap rather than papering over).
 *
 * @param {Record<string, unknown>} fixtures the merged fixture world (shared file + scenario overrides)
 * @param {string} toolName the tool being answered
 * @param {Record<string, unknown>} args the arguments the model sent
 */
export function fixtureResult(fixtures, toolName, args) {
  if (!fixtures || !Object.hasOwn(fixtures, toolName)) return undefined;
  return resolve(fixtures[toolName], args ?? {}, {}, { toolName, fixtures, seen: [] });
}
