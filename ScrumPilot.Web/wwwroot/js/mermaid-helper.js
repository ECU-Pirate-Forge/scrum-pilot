window.mermaidHelper = {
    _renders: new WeakMap(),
    _sequence: 0,
    init: function () {
        if (window.mermaid) {
            mermaid.initialize({ startOnLoad: false, theme: 'default', securityLevel: 'strict', flowchart: { htmlLabels: false } });
        }
    },
    render: async function (containerId, definition) {
        const container = document.getElementById(containerId);
        if (!container || !container.isConnected) return;
        const owner = {};
        this._renders.set(container, owner);
        const isCurrent = () => container.isConnected
            && document.getElementById(containerId) === container
            && this._renders.get(container) === owner;
        try {
            if (!window.mermaid) throw new Error('Mermaid is unavailable.');
            const uniqueId = 'mermaid-' + Date.now() + '-' + (++this._sequence);
            const { svg } = await mermaid.render(uniqueId, definition);
            if (!isCurrent()) return;
            const parsed = new DOMParser().parseFromString(svg, 'image/svg+xml');
            if (parsed.querySelector('parsererror')) throw new Error('Invalid graph SVG.');
            parsed.querySelectorAll('script, foreignObject, iframe, image, use, a').forEach(node => node.remove());
            parsed.querySelectorAll('*').forEach(node => {
                for (const attribute of [...node.attributes]) {
                    if (/^on/i.test(attribute.name) || /href$/i.test(attribute.name)) node.removeAttribute(attribute.name);
                }
            });
            container.replaceChildren(document.importNode(parsed.documentElement, true));
        } catch (e) {
            if (!isCurrent()) return;
            this._renders.delete(container);
            container.textContent = 'Failed to render chart. Retry or use the Fluent client dependency view.';
            throw e;
        }
    }
};
