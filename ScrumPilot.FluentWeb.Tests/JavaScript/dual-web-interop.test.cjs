const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

function mermaidFixture(site) {
    function element(initial = '') {
        return {
            innerHTML: initial, isConnected: true, dataset: {}, graph: null,
            querySelector(selector) { return selector === 'svg' ? this.graph : null; },
            querySelectorAll() { return this.graph ? [this.graph.node] : []; },
            replaceChildren(graph) { this.graph = graph; this.innerHTML = graph.raw; }
        };
    }
    let container = element();
    const pending = [];
    const parsedGraphs = [];
    const downloads = [];
    const focusHandlers = [];
    const mermaid = {
        render(id) {
            return new Promise((resolve, reject) => pending.push({ id, resolve, reject }));
        }
    };
    const window = { mermaid, addEventListener(name, handler) { focusHandlers.push(handler); }, removeEventListener() {} };
    const document = {
        getElementById: () => container, importNode: node => node,
        createElement() { return { click() { downloads.push(this.download); } }; }
    };
    class DOMParser {
        parseFromString(raw) {
            const attributes = new Map([['viewBox', '0 0 100 100']]);
            const node = {
                id: 'flowchart-PBI7-0',
                attributes: [{ name: 'onclick' }, { name: 'xlink:href' }, { name: 'class' }],
                removedAttributes: [],
                removeAttribute(name) { this.removedAttributes.push(name); },
                setAttribute(name, value) { this[name] = value; }
            };
            const graph = {
                raw, node, style: {}, removedUnsafeNodes: false,
                getAttribute: name => attributes.get(name),
                setAttribute: (name, value) => attributes.set(name, value),
                cloneNode() { return this; },
                querySelectorAll() { return [node]; }
            };
            parsedGraphs.push(graph);
            return {
                documentElement: graph,
                querySelector: () => null,
                querySelectorAll(selector) {
                    if (selector === '*') return [node];
                    return [{ remove() { graph.removedUnsafeNodes = true; } }];
                }
            };
        }
    }
    vm.runInNewContext(
        fs.readFileSync(path.resolve(__dirname, '..', '..', site, 'wwwroot/js/mermaid-helper.js'), 'utf8'),
        {
            window, mermaid, document, Date, DOMParser, console, Blob,
            URL: { createObjectURL() { return 'blob:export'; }, revokeObjectURL() {} },
            XMLSerializer: class { serializeToString(graph) { return graph.raw; } },
            setTimeout(callback) { callback(); }
        }
    );
    return {
        helper: window.mermaidHelper, pending, parsedGraphs, downloads, focusHandlers,
        get container() { return container; },
        replaceContainer() {
            container = element('replacement');
        }
    };
}

function bootstrapBase(site, pathname) {
    const html = fs.readFileSync(path.resolve(__dirname, '..', '..', site, 'wwwroot/index.html'), 'utf8');
    const head = html.split('</head>')[0];
    const staticHref = /<base\s+href="([^"]+)"/.exec(head)[1];
    const base = {
        href: staticHref,
        setAttribute(name, value) { this[name] = value; }
    };
    for (const script of head.matchAll(/<script>([\s\S]*?)<\/script>/g)) {
        vm.runInNewContext(script[1], { location: { pathname }, document: { querySelector: () => base } });
    }
    return { html, head, staticHref, href: base.href };
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

    test(`${site}: speculative preloads resolve bootstrap assets under the gateway prefix`, () => {
        const app = site === 'ScrumPilot.Web' ? 'web' : 'fluentweb';
        const { html, staticHref } = bootstrapBase(site, `/${app}/organizations/1/backlog`);
        // Preload scanners resolve relative assets with the markup base, not the script-adjusted one.
        assert.equal(staticHref, `/${app}/`);
        const baseIndex = html.indexOf('<base');
        for (const asset of html.matchAll(/<(?:link|script)\b[^>]*\s(?:href|src)="(?!https?:)[^"]+"/g)) {
            assert.ok(baseIndex < asset.index, `${asset[0]} precedes <base>`);
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

    test(`${site}: successful SVG rendering strips unsafe elements and event/link attributes`, async () => {
        const fixture = mermaidFixture(site);
        const render = fixture.helper.render('chart', 'safe labels');
        fixture.pending[0].resolve({ svg: 'sanitized graph' });
        await render;
        const graph = fixture.parsedGraphs[0];
        assert.equal(graph.removedUnsafeNodes, true);
        assert.deepEqual(graph.node.removedAttributes, ['onclick', 'xlink:href']);
    });

    test(`${site}: retired failures cannot touch either a remounted same-ID chart or its old DOM`, async () => {
        const fixture = mermaidFixture(site);
        const oldContainer = fixture.container;
        const old = fixture.helper.render('chart', 'organization A');
        fixture.replaceContainer();
        const current = fixture.helper.render('chart', 'organization B');
        fixture.pending[1].resolve({ svg: 'organization B graph' });
        await current;
        oldContainer.innerHTML = 'retired';
        fixture.pending[0].reject(new Error('organization A denied'));
        await old;
        assert.equal(oldContainer.innerHTML, 'retired');
        assert.equal(fixture.container.innerHTML, 'organization B graph');
    });
}

test('Fluent Mermaid keyboard callbacks are owned by the current render and current DOM', async () => {
    const fixture = mermaidFixture('ScrumPilot.FluentWeb');
    const first = fixture.helper.render('chart', 'first');
    fixture.pending[0].resolve({ svg: 'first' });
    const firstToken = await first;
    const calls = [];
    const callback = { invokeMethodAsync(...args) { calls.push(args); return Promise.resolve(); } };
    fixture.helper.attachNodes('chart', callback, [7], firstToken);
    const oldNode = fixture.container.graph.node;
    let prevented = false;
    oldNode.onkeydown({ key: ' ', preventDefault() { prevented = true; } });
    assert.equal(prevented, true);
    assert.deepEqual(calls, [['OpenNodeAsync', 7, firstToken]]);
    const second = fixture.helper.render('chart', 'second');
    oldNode.onclick(); // Even while a replacement render is still pending.
    fixture.pending[1].resolve({ svg: 'second' });
    const secondToken = await second;
    fixture.helper.attachNodes('chart', callback, [7], firstToken); // Late interop completion.
    assert.equal(fixture.container.graph.node.onclick, undefined);
    fixture.helper.attachNodes('chart', callback, [7], secondToken);
    const currentNode = fixture.container.graph.node;
    currentNode.onkeydown({ key: 'Enter', preventDefault() {} });
    fixture.replaceContainer();
    currentNode.onclick();
    oldNode.onclick();
    assert.equal(calls.length, 2);
    assert.deepEqual(calls[1], ['OpenNodeAsync', 7, secondToken]);
});

test('Fluent viewport and exports cannot target a newer render through stale interop calls', async () => {
    const fixture = mermaidFixture('ScrumPilot.FluentWeb');
    let render = fixture.helper.render('chart', 'organization A');
    fixture.pending[0].resolve({ svg: 'A' });
    const retiredToken = await render;
    fixture.replaceContainer();
    render = fixture.helper.render('chart', 'organization B');
    fixture.pending[1].resolve({ svg: 'B' });
    const currentToken = await render;
    fixture.helper.viewport('chart', 'in', retiredToken);
    fixture.helper.download('chart', 'svg', retiredToken);
    assert.equal(fixture.container.graph.getAttribute('viewBox'), '0 0 100 100');
    assert.equal(fixture.downloads.length, 0);
    fixture.helper.viewport('chart', 'in', currentToken);
    assert.equal(Number(fixture.container.graph.getAttribute('viewBox').split(' ')[2]), 80);
    fixture.helper.viewport('chart', 'reset', currentToken);
    assert.equal(fixture.container.graph.getAttribute('viewBox'), '0 0 100 100');
    fixture.helper.download('chart', 'mmd', currentToken);
    fixture.helper.download('chart', 'svg', currentToken);
    assert.deepEqual(fixture.downloads, ['sprint-dependencies.mmd', 'sprint-dependencies.svg']);
});

test('Fluent retired focus observers cannot reconcile a replacement tenant subscription', async () => {
    const fixture = mermaidFixture('ScrumPilot.FluentWeb');
    const calls = [];
    const callback = { invokeMethodAsync(...args) { calls.push(args); return Promise.resolve(); } };
    fixture.helper.observeFocus('subscription', callback, 1);
    const old = fixture.focusHandlers[0];
    fixture.helper.observeFocus('subscription', callback, 2);
    old();
    fixture.focusHandlers[1]();
    fixture.helper.unobserveFocus('subscription');
    fixture.focusHandlers[1]();
    assert.deepEqual(calls, [['ReconcileOnFocus', 2]]);
});
