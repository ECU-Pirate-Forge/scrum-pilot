window.mermaidHelper = {
    init: function () {
        if (window.mermaid) {
            mermaid.initialize({ startOnLoad: false, theme: 'default', securityLevel: 'strict', flowchart: { htmlLabels: false }, maxEdges: 5000 });
        }
    },
    render: async function (containerId, definition) {
        const container = document.getElementById(containerId);
        if (!container) throw new Error('Chart container is unavailable.');
        const requestId = crypto.randomUUID();
        container.dataset.renderRequest = requestId;
        try {
            if (!window.mermaid) throw new Error('Mermaid is unavailable. Use the dependency list.');
            const uniqueId = 'mermaid-' + requestId;
            const { svg } = await mermaid.render(uniqueId, definition);
            if (container.dataset.renderRequest !== requestId) return;
            const parsed = new DOMParser().parseFromString(svg, 'image/svg+xml');
            if (parsed.querySelector('parsererror')) throw new Error('Invalid graph SVG.');
            parsed.querySelectorAll('script, foreignObject, iframe, image, use, a').forEach(node => node.remove());
            parsed.querySelectorAll('*').forEach(node => {
                for (const attribute of [...node.attributes]) {
                    if (/^on/i.test(attribute.name) || /href$/i.test(attribute.name)) node.removeAttribute(attribute.name);
                }
            });
            const previousViewBox = container.querySelector('svg')?.getAttribute('viewBox');
            const keepViewport = previousViewBox && previousViewBox !== container.dataset.originalViewBox;
            container.replaceChildren(document.importNode(parsed.documentElement, true));
            const graph = container.querySelector('svg');
            graph.style.maxWidth = 'none';
            container.dataset.originalViewBox = graph.getAttribute('viewBox') || '';
            if (keepViewport) graph.setAttribute('viewBox', previousViewBox);
            container.dataset.definition = definition;
        } catch (e) {
            if (container.dataset.renderRequest !== requestId) return;
            container.textContent = 'Failed to render chart. The accessible dependency list remains available.';
            throw e;
        }
    },
    viewport: function (containerId, action) {
        const container = document.getElementById(containerId);
        const graph = container?.querySelector('svg');
        if (!graph) return;
        const original = container.dataset.originalViewBox;
        if (action === 'reset') { graph.setAttribute('viewBox', original); return; }
        const values = (graph.getAttribute('viewBox') || '').split(/[ ,]+/).map(Number);
        if (values.length !== 4 || values.some(value => !Number.isFinite(value))) return;
        let [x, y, width, height] = values;
        if (action === 'in' || action === 'out') {
            const factor = action === 'in' ? 0.8 : 1.25;
            x += width * (1 - factor) / 2; y += height * (1 - factor) / 2;
            width *= factor; height *= factor;
        } else {
            if (action === 'left') x -= width * 0.2;
            if (action === 'right') x += width * 0.2;
            if (action === 'up') y -= height * 0.2;
            if (action === 'down') y += height * 0.2;
        }
        graph.setAttribute('viewBox', [x, y, width, height].join(' '));
    },
    attachNodes: function (containerId, callback, ids) {
        const container = document.getElementById(containerId);
        const allowed = new Set(ids);
        container?.querySelectorAll('g.node').forEach(node => {
            const match = /^flowchart-PBI(\d+)-/.exec(node.id);
            const id = match ? Number(match[1]) : 0;
            if (!allowed.has(id)) return;
            node.setAttribute('tabindex', '0');
            node.setAttribute('role', 'button');
            const activate = () => callback.invokeMethodAsync('OpenNodeAsync', id).catch(error => console.warn('Graph details unavailable.', error));
            node.onclick = activate;
            node.onkeydown = event => {
                if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); activate(); }
            };
        });
    },
    focusObservers: new Map(),
    observeFocus: function (id, callback) {
        window.mermaidHelper.unobserveFocus(id);
        const handler = () => callback.invokeMethodAsync('ReconcileOnFocus').catch(error => console.warn('Graph focus reconciliation unavailable.', error));
        window.mermaidHelper.focusObservers.set(id, handler);
        window.addEventListener('focus', handler);
    },
    unobserveFocus: function (id) {
        const handler = window.mermaidHelper.focusObservers.get(id);
        if (handler) window.removeEventListener('focus', handler);
        window.mermaidHelper.focusObservers.delete(id);
    },
    downloadText: function (filename, text) {
        const url = URL.createObjectURL(new Blob([text], { type: 'text/plain;charset=utf-8' }));
        const link = document.createElement('a');
        link.href = url; link.download = filename; link.click();
        setTimeout(() => URL.revokeObjectURL(url), 1000);
    },
    download: function (containerId, format) {
        const container = document.getElementById(containerId);
        const graph = container?.querySelector('svg');
        if (!container || !graph) throw new Error('Render the chart before exporting.');
        const exported = graph.cloneNode(true);
        exported.querySelectorAll('[tabindex]').forEach(node => { node.removeAttribute('tabindex'); node.removeAttribute('role'); });
        const text = format === 'mmd' ? container.dataset.definition : new XMLSerializer().serializeToString(exported);
        const blob = new Blob([text], { type: format === 'mmd' ? 'text/plain;charset=utf-8' : 'image/svg+xml;charset=utf-8' });
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url; link.download = 'sprint-dependencies.' + format; link.click();
        setTimeout(() => URL.revokeObjectURL(url), 1000);
    }
};
