import { defineConfig } from 'vitepress'

// Served by the Proxytrace app at /docs (both the .NET kiosk wwwroot and the
// production nginx container). Keep cleanUrls=false so emitted .html files match
// request paths exactly and the SPA fallback never hijacks a docs deep link.
export default defineConfig({
  title: 'Proxytrace Manual',
  description: 'User and operator manual for Proxytrace — AI agent observability and evaluation.',
  base: '/docs/',
  cleanUrls: false,
  appearance: 'force-dark',
  lang: 'en-US',
  lastUpdated: false,
  head: [
    ['link', { rel: 'icon', type: 'image/svg+xml', href: '/favicon.svg' }],
    ['link', { rel: 'icon', type: 'image/png', sizes: '32x32', href: '/icon-32.png' }],
  ],
  themeConfig: {
    search: { provider: 'local' },
    nav: [
      { text: 'User Guide', link: '/guide/getting-started' },
      { text: 'Operations', link: '/admin/installation' },
    ],
    sidebar: {
      '/guide/': [
        {
          text: 'User Guide',
          items: [
            { text: 'Getting Started', link: '/guide/getting-started' },
            { text: 'Choosing Your Language', link: '/guide/language' },
            { text: 'Two-Factor Authentication', link: '/guide/two-factor-authentication' },
            { text: 'Proxy Setup', link: '/guide/proxy-setup' },
            { text: 'Capturing Traces', link: '/guide/capturing-traces' },
            { text: 'Sessions', link: '/guide/sessions' },
            { text: 'Finding Outliers', link: '/guide/outliers' },
            { text: 'Anomaly Dashboard', link: '/guide/anomaly-dashboard' },
            { text: 'Agents', link: '/guide/agents' },
            { text: 'Test Suites & Cases', link: '/guide/test-suites-and-cases' },
            { text: 'Evaluators', link: '/guide/evaluators' },
            { text: 'Running Tests', link: '/guide/running-tests' },
            { text: 'Optimization Theories', link: '/guide/optimization-theories' },
            { text: 'Optimization Proposals', link: '/guide/optimization-proposals' },
            { text: 'Dashboard', link: '/guide/dashboard' },
            { text: 'Notifications', link: '/guide/notifications' },
            { text: 'Tracey (AI Assistant)', link: '/guide/tracey' },
            { text: 'MCP Server', link: '/guide/mcp-server' },
          ],
        },
      ],
      '/admin/': [
        {
          text: 'Operations',
          items: [
            { text: 'Installation', link: '/admin/installation' },
            { text: 'Configuration', link: '/admin/configuration' },
            { text: 'Licensing', link: '/admin/licensing' },
            { text: 'Database', link: '/admin/database' },
            { text: 'Providers & API Keys', link: '/admin/providers-and-api-keys' },
            { text: 'User Management', link: '/admin/user-management' },
            { text: 'Error Log', link: '/admin/error-log' },
            { text: 'Audit Log', link: '/admin/audit-log' },
            { text: 'Email Notifications', link: '/admin/email' },
            { text: 'Outlier Detection', link: '/admin/outlier-detection' },
            { text: 'Deployment', link: '/admin/deployment' },
            { text: 'Upgrading', link: '/admin/upgrading' },
            { text: 'E2E Tests', link: '/admin/e2e-tests' },
          ],
        },
      ],
    },
    socialLinks: [],
    outline: { level: [2, 3] },
  },
})
