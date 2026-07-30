import js from '@eslint/js'
import globals from 'globals'
import reactHooks from 'eslint-plugin-react-hooks'
import reactRefresh from 'eslint-plugin-react-refresh'
import lingui from 'eslint-plugin-lingui'
import tseslint from 'typescript-eslint'
import { defineConfig, globalIgnores } from 'eslint/config'

export default defineConfig([
  globalIgnores(['dist']),
  {
    files: ['**/*.{ts,tsx}'],
    extends: [
      js.configs.recommended,
      tseslint.configs.recommended,
      reactHooks.configs.flat.recommended,
      reactRefresh.configs.vite,
    ],
    languageOptions: {
      globals: globals.browser,
    },
  },
  {
    // Ban raw styled control elements — use the components/ui primitives instead.
    // (DESIGN.md §3.1). Primitive leaf files that legitimately render the raw
    // element are exempted below. Currently 'warn' during migration; flipped to
    // 'error' once the codebase is clean.
    files: ['src/**/*.{ts,tsx}'],
    ignores: [
      // The components/ui/* primitive layer renders raw control elements on
      // purpose — it IS the implementation behind the primitives.
      'src/components/ui/**',
      // Bespoke command palette (own keyboard/focus model) + range slider:
      'src/components/search/UnifiedSearch.tsx',
      'src/features/playground/components/ParameterSlider.tsx',
    ],
    rules: {
      'no-restricted-syntax': [
        'error',
        {
          selector: "JSXOpeningElement[name.name='button']",
          message:
            'Use <Button>/<IconButton> from components/ui — never a raw <button>. (DESIGN.md §3.1) Escape hatch: // eslint-disable-next-line no-restricted-syntax -- <reason>',
        },
        {
          selector: "JSXOpeningElement[name.name='input']",
          message:
            'Use <Input>/<Checkbox>/<Radio>/<Switch> from components/ui — not a raw <input>. (type="range"/"file" are exceptions; disable with justification.)',
        },
        {
          selector: "JSXOpeningElement[name.name='select']",
          message: 'Use <Select> or <Combobox> from components/ui — not a raw <select>.',
        },
        {
          selector: "JSXOpeningElement[name.name='textarea']",
          message: 'Use <Textarea> from components/ui — not a raw <textarea>.',
        },
      ],
    },
  },
  {
    // i18n guardrail: every user-facing string must go through the Lingui macros
    // (<Trans>, t``, plural, msg). `no-unlocalized-strings` flags raw strings so they don't slip
    // back in. Now an 'error' — the big-bang migration is complete and the codebase is clean; the
    // ignore lists + scoped `// eslint-disable-next-line lingui/no-unlocalized-strings -- <reason>`
    // hatches below cover the non-copy strings the rule cannot classify (enum tokens, keys, etc.).
    // Scope to .tsx (where UI text lives). Plain .ts files are data/config/query-keys whose
    // string literals are not user copy; UI strings that live in .ts label maps are wrapped with
    // the `msg` macro and caught at use sites.
    files: ['src/**/*.tsx'],
    ignores: [
      'src/**/*.spec.tsx',
      'src/**/*.test.tsx',
    ],
    languageOptions: {
      // Type-aware linting (scoped to this block only) so `no-unlocalized-strings` can recognise
      // string-literal-union-typed values — enum props like <Badge tone="success">, toast('error'),
      // DOM event names — as non-copy by their type, instead of hand-listing every callsite.
      parserOptions: { projectService: true, tsconfigRootDir: import.meta.dirname },
    },
    plugins: { lingui },
    rules: {
      // Catch a <Trans> wrapping a single child that needs no translation, nested <Trans>, etc.
      'lingui/no-trans-inside-trans': 'warn',
      'lingui/no-single-tag-to-translate': 'warn',
      'lingui/t-call-in-function': 'error',
      'lingui/no-unlocalized-strings': [
        'error',
        {
          ignore: [
            // Strings with no letters at all (numbers, punctuation, css-ish tokens) aren't copy.
            '^[^A-Za-z]*$',
            // CSS values: var()/color-mix() expressions, custom-property names and inline-style
            // fragments. A string starting with these tokens is a style value, never UI copy.
            '^var\\(', '^color-mix\\(', '^--[a-z]', '^\\d+px ',
            // Intl.DateTimeFormat / NumberFormat option values (object-literal values, not copy).
            '^(2-digit|numeric|long|short|narrow|digital)$',
            // Route paths / query-string fragments handed to the router (to=, navigate()).
            '^/[a-z]', '^\\?[a-z]',
            // External URLs and email-ish tokens (links, demo data) — never UI copy.
            '^https?://', '^mailto:', '@[a-z0-9.-]+\\.[a-z]{2,}$',
            // Technical identifiers that surface as-is and are never translated:
            // snake_case API field names (input_tokens, finish_reason), dotted storage / query
            // keys (dashboard.range, trace.id, chat.completion). Both shapes are machine tokens.
            '^[a-z][a-z0-9]*(_[a-z0-9]+)+$', '^[a-z][a-zA-Z0-9]*(\\.[a-z][a-zA-Z0-9]*)+$',
            // CSS grid track sizes, percentile/metric tags and HTTP status buckets.
            '^\\d+(\\.\\d+)?fr$', '^p\\d{1,3}$', '^\\dxx$',
          ],
          // Enum-ish props on our own components carry tokens, not user copy, plus structural/DOM
          // attributes that are never translatable (CSS classes, ids, router targets, SVG geometry)
          // and the property keys of our style-token maps (their values are Tailwind class strings).
          // `ignoreNames` matches JSX attribute names, object-property keys and ignored variable
          // names. (DOM text attributes like aria-label / placeholder / title are intentionally NOT
          // listed — they ARE UI text.)
          ignoreNames: [
            // component enum props
            'variant', 'tone', 'side', 'align', 'placement', 'color', 'size', 'icon',
            'status', 'kind', 'mode', 'type', 'role', 'testId', 'autoComplete', 'key',
            'as', 'padding', 'direction', 'justify', 'severity', 'tab', 'layout', 'shape',
            'elevation', 'trend', 'trendDirection', 'trendDir', 'language', 'lang', 'level',
            'density', 'weight', 'format', 'state', 'connection',
            // structural / non-copy attributes
            'className', 'class', 'headerClassName', 'contentClassName',
            'to', 'path', 'data-testid', 'railTestId', 'listTestId',
            'htmlFor', 'id', 'name',
            'htmlType', 'inputMode', 'enterKeyHint', 'autoCapitalize', 'spellCheck',
            // style-token-map keys: values are Tailwind class lists / CSS colours, not copy
            'bg', 'fg', 'border', 'accentText', 'accentBg', 'bodyBg', 'hover', 'dot', 'ring',
            'outline', 'cursor', 'chip', 'track', 'fillClass', 'textClass', 'barClass',
            // inline-SVG geometry (never copy)
            'd', 'fill', 'stroke', 'viewBox', 'points', 'transform', 'gradientUnits',
            'gradientTransform', 'stopColor', 'clipPath', 'fillRule', 'clipRule',
            'strokeWidth', 'strokeLinecap', 'strokeLinejoin', 'cx', 'cy', 'x1', 'x2', 'y1', 'y2',
            'preserveAspectRatio', 'xmlns',
          ],
          // Calls whose string args are class names / log lines / storage keys / DOM plumbing /
          // navigation targets / formatter options — never user copy.
          ignoreFunctions: [
            'cn', 'clsx', 'cva', 'console.*', 'localStorage.*', 'sessionStorage.*',
            'navigate', '*.addEventListener', '*.removeEventListener', 'addEventListener',
            'removeEventListener', '*.matchMedia', 'matchMedia', '*.getItem', '*.setItem',
            '*.removeItem', '*.querySelector', '*.querySelectorAll', '*.getAttribute',
            '*.setAttribute', '*.getElementById', 'getComputedStyle', 'Intl.*',
          ],
          useTsTypes: true,
        },
      ],
    },
  },
])
