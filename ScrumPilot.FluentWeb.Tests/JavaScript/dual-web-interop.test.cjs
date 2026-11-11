const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

function mermaidFixture(site) {
    let container = { innerHTML: '', isConnected: true };
    const pending = [];
    const mermaid = {
        render(id) {
            return new Promise((resolve, reject) => pending.push({ id, resolve, reject }));
        }
    };
    const window = { mermaid };
    const document = { getElementById: () => container };
    vm.runInNewContext(
        fs.readFileSync(path.resolve(__dirname, '..', '..', site, 'wwwroot/js/mermaid-helper.js'), 'utf8'),
        { window, mermaid, document, Date }
    );
    return {
        helper: window.mermaidHelper, pending,
        get container() { return container; },
        replaceContainer() {
            container = { innerHTML: 'replacement', isConnected: true };
        }
    };
}

function bootstrapBase(site, pathname) {
    const html = fs.readFileSync(path.resolve(__dirname, '..', '..', site, 'wwwroot/index.html'), 'utf8');
    const head = html.split('</head>')[0];
    const base = {
        href: /<base\s+href="([^"]+)"/.exec(head)[1],
        setAttribute(name, value) { this[name] = value; }
    };
    for (const script of head.matchAll(/<script>([\s\S]*?)<\/script>/g)) {
        vm.runInNewContext(script[1], { location: { pathname }, document: { querySelector: () => base } });
    }
    return { html, head, href: base.href };
}

test('Fluent shell: narrow tenant pickers have compact labels without hiding header actions', () => {
    const root = path.resolve(__dirname, '..', '..', 'ScrumPilot.FluentWeb');
    const shell = fs.readFileSync(path.join(root, 'Layout/MainLayout.razor'), 'utf8');
    const css = fs.readFileSync(path.join(root, 'wwwroot/css/app.css'), 'utf8');
    assert.equal((shell.match(/class="sp-selection-label"/g) ?? []).length, 2);
    assert.match(shell, /aria-label="@\(\$"Select organization:/);
    assert.match(shell, /aria-label="@\(\$"Select project:/);
    assert.match(css, /@media\s*\(max-width:\s*600px\)/);
    assert.match(css, /\.sp-header\s+\.sp-selection-label\s*\{\s*display:\s*none;/);
    assert.match(css, /\.sp-header-button--selection\s*\{[^}]*width:\s*44px/s);
    assert.match(css, /\.sp-header\s*\{[^}]*box-sizing:\s*border-box/s);
});

test('Mud shell: narrow tenant controls and brand preserve space for every header action', () => {
    const root = path.resolve(__dirname, '..', '..', 'ScrumPilot.Web');
    const shell = fs.readFileSync(path.join(root, 'Layout/MainLayout.razor'), 'utf8');
    const css = fs.readFileSync(path.join(root, 'Layout/MainLayout.razor.css'), 'utf8');
    assert.equal((shell.match(/class="appbar-selection-label"/g) ?? []).length, 2);
    assert.match(shell, /aria-label="@\(\$"Select organization:/);
    assert.match(shell, /aria-label="@\(\$"Select project:/);
    assert.match(css, /@media\s*\(max-width:\s*600px\)/);
    assert.match(css, /\.appbar-selection-label\s*\{\s*display:\s*none;/);
    assert.match(css, /\.appbar-tenant-button\s*\{[^}]*width:\s*44px/s);
    assert.match(css, /\.mud-toolbar\s*\{[^}]*padding:\s*0 8px/s);
    assert.match(css, /\.main-logo\s*\{[^}]*width:\s*44px/s);
});

for (const site of ['ScrumPilot.Web', 'ScrumPilot.FluentWeb']) {
    test(`${site}: active shell and login assets are app-base-relative`, () => {
        for (const file of ['Layout/MainLayout.razor', 'Pages/Login.razor']) {
            const source = fs.readFileSync(path.resolve(__dirname, '..', '..', site, file), 'utf8');
            assert.doesNotMatch(source, /(?:src|href)\s*=\s*["']\/(?:images|css|js|_content|_framework)\//i);
        }
    });

    test(`${site}: gateway landing and deep links select the app base before bootstrap assets`, () => {
        const app = site === 'ScrumPilot.Web' ? 'web' : 'fluentweb';
        for (const pathname of [`/${app}`, `/${app}/`, `/${app}/organizations/1/backlog`]) {
            const result = bootstrapBase(site, pathname);
            assert.equal(result.href, `/${app}/`);
            assert.ok(result.head.indexOf('<script>') < result.head.indexOf('<link'));
            const baseUri = new URL(result.href, 'https://gateway.test');
            assert.equal(new URL('_blazor/_configuration', baseUri).pathname, `/${app}/_blazor/_configuration`);
            assert.equal(new URL('_framework/blazor.webassembly.js', baseUri).pathname, `/${app}/_framework/blazor.webassembly.js`);
        }
    });

    test(`${site}: direct-root routes and similar path segments never acquire a gateway base`, () => {
        const app = site === 'ScrumPilot.Web' ? 'web' : 'fluentweb';
        for (const pathname of ['/', '/organizations/1/backlog', `/${app}-other/organizations/1/backlog`, `/organizations/1/${app}/backlog`]) {
            assert.equal(bootstrapBase(site, pathname).href, '/');
        }
    });

    test(`${site}: saved desktop coordinates are applied before responsive GridStack initialization`, () => {
        const attributes = new Map();
        const element = { setAttribute(name, value) { attributes.set(name, value); } };
        let initialized = false;
        const GridStack = {
            init() {
                assert.equal(attributes.get('gs-x'), 6);
                assert.equal(attributes.get('gs-y'), 3);
                assert.equal(attributes.get('gs-w'), 6);
                assert.equal(attributes.get('gs-h'), 4);
                initialized = true;
                return {
                    on() {},
                    update() { assert.fail('Desktop coordinates must not overwrite responsive nodes.'); }
                };
            }
        };
        const window = {};
        vm.runInNewContext(
            fs.readFileSync(path.resolve(__dirname, '..', '..', site, 'wwwroot/js/metrics-gridstack.js'), 'utf8'),
            { window, GridStack, document: { querySelector() { return element; } } }
        );
        window.metricsGridstack.init({}, [{ id: 'burndown', x: 6, y: 3, w: 6, h: 4 }], 'current');
        assert.equal(initialized, true);
    });

    test(`${site}: retired GridStack callbacks and disposal cannot affect the current dashboard`, () => {
        const grids = [];
        const GridStack = {
            init() {
                const grid = {
                    destroyed: 0,
                    on(_, callback) { this.change = callback; },
                    destroy() { this.destroyed++; },
                    save() { return [{ id: 'current', x: 0, y: 0, w: 1, h: 1 }]; }
                };
                grids.push(grid);
                return grid;
            }
        };
        const window = {};
        vm.runInNewContext(
            fs.readFileSync(path.resolve(__dirname, '..', '..', site, 'wwwroot/js/metrics-gridstack.js'), 'utf8'),
            { window, GridStack }
        );
        let oldCallbacks = 0;
        let currentCallbacks = 0;
        window.metricsGridstack.init({ invokeMethodAsync() { oldCallbacks++; return Promise.resolve(); } }, [], 'old');
        window.metricsGridstack.init({ invokeMethodAsync() { currentCallbacks++; return Promise.resolve(); } }, [], 'current');
        grids[0].change();
        window.metricsGridstack.destroy('old');
        assert.equal(oldCallbacks, 0);
        assert.equal(currentCallbacks, 0);
        assert.equal(window.metricsGridstack.getLayout('old').length, 0);
        assert.equal(grids[1].destroyed, 0);
        grids[1].change();
        assert.equal(currentCallbacks, 1);
        window.metricsGridstack.destroy('current');
        assert.equal(grids[1].destroyed, 1);
    });

    test(`${site}: gateway runtime configuration is app-relative and excludes private settings`, async () => {
        const app = site === 'ScrumPilot.Web' ? 'web' : 'fluentweb';
        let fetched;
        const context = {
            URL,
            document: { baseURI: `https://gateway.test/${app}/` },
            fetch: async url => {
                fetched = url.href;
                return {
                    ok: true,
                    headers: { get: () => 'application/json' },
                    json: async () => ({ webAssembly: { environment: {
                        services__webapi__http__0: `https://gateway.test/${app}/_api/webapi`,
                        OTEL_SERVICE_NAME: app,
                        OTEL_EXPORTER_OTLP_ENDPOINT: `https://gateway.test/${app}/_otlp`,
                        OTEL_EXPORTER_OTLP_HEADERS: 'must-not-copy',
                        ConnectionStrings__control: 'must-not-copy',
                        Jwt__SigningKey: 'must-not-copy'
                    } } })
                };
            }
        };
        vm.createContext(context);
        vm.runInContext(fs.readFileSync(path.resolve(__dirname, '..', '..', site, `wwwroot/${site}.lib.module.js`), 'utf8')
            .replace('export async function', 'async function'), context);
        const config = { environmentVariables: { existing: 'preserved' } };
        await context.onRuntimeConfigLoaded(config);
        assert.equal(fetched, `https://gateway.test/${app}/_blazor/_configuration`);
        assert.equal(config.environmentVariables.services__webapi__http__0, `https://gateway.test/${app}/_api/webapi`);
        assert.equal(config.environmentVariables.OTEL_SERVICE_NAME, app);
        assert.equal(config.environmentVariables.existing, 'preserved');
        assert.equal(config.environmentVariables.OTEL_EXPORTER_OTLP_HEADERS, undefined);
        assert.equal(config.environmentVariables.ConnectionStrings__control, undefined);
        assert.equal(config.environmentVariables.Jwt__SigningKey, undefined);
    });

    test(`${site}: a direct static run tolerates missing or non-JSON gateway configuration`, async () => {
        for (const mode of ['offline', 'html']) {
            const context = {
                URL,
                document: { baseURI: 'https://static.test/' },
                fetch: async () => {
                    if (mode === 'offline') throw new Error('No gateway');
                    return { ok: true, headers: { get: () => 'text/html' }, json: () => assert.fail('Must not parse static fallback HTML') };
                }
            };
            vm.createContext(context);
            vm.runInContext(fs.readFileSync(path.resolve(__dirname, '..', '..', site, `wwwroot/${site}.lib.module.js`), 'utf8')
                .replace('export async function', 'async function'), context);
            const config = { environmentVariables: { existing: 'preserved' } };
            await context.onRuntimeConfigLoaded(config);
            assert.deepEqual(config.environmentVariables, { existing: 'preserved' });
        }
    });

    test(`${site}: an older Mermaid success cannot replace the latest chart`, async () => {
        const fixture = mermaidFixture(site);
        const old = fixture.helper.render('chart', 'old');
        const current = fixture.helper.render('chart', 'current');
        fixture.pending[1].resolve({ svg: 'current' });
        await current;
        fixture.pending[0].resolve({ svg: 'old' });
        await old;
        assert.equal(fixture.container.innerHTML, 'current');
    });

    test(`${site}: an older Mermaid failure cannot replace the latest chart`, async () => {
        const fixture = mermaidFixture(site);
        const old = fixture.helper.render('chart', 'old');
        const current = fixture.helper.render('chart', 'current');
        fixture.pending[1].resolve({ svg: 'current' });
        await current;
        fixture.pending[0].reject(new Error('old render failed'));
        await old;
        assert.equal(fixture.container.innerHTML, 'current');
    });

    test(`${site}: a detached organization chart is never updated`, async () => {
        const fixture = mermaidFixture(site);
        const pending = fixture.helper.render('chart', 'old');
        fixture.container.isConnected = false;
        fixture.container.innerHTML = 'detached';
        fixture.pending[0].resolve({ svg: 'old' });
        await pending;
        assert.equal(fixture.container.innerHTML, 'detached');
    });

    test(`${site}: a replaced chart element cannot receive an old completion`, async () => {
        const fixture = mermaidFixture(site);
        const oldContainer = fixture.container;
        const pending = fixture.helper.render('chart', 'old');
        fixture.replaceContainer();
        oldContainer.innerHTML = 'retired';
        fixture.pending[0].resolve({ svg: 'old' });
        await pending;
        assert.equal(oldContainer.innerHTML, 'retired');
        assert.equal(fixture.container.innerHTML, 'replacement');
    });
}
