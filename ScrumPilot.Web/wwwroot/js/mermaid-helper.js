window.mermaidHelper = {
    _renders: new WeakMap(),
    _sequence: 0,
    init: function () {
        if (window.mermaid) {
            mermaid.initialize({ startOnLoad: false, theme: 'default', securityLevel: 'loose' });
        }
    },
    render: async function (containerId, definition) {
        if (!window.mermaid) return;
        const container = document.getElementById(containerId);
        if (!container || !container.isConnected) return;
        const owner = {};
        this._renders.set(container, owner);
        const isCurrent = () => container.isConnected
            && document.getElementById(containerId) === container
            && this._renders.get(container) === owner;
        try {
            container.innerHTML = '';
            const uniqueId = 'mermaid-' + Date.now() + '-' + (++this._sequence);
            const { svg } = await mermaid.render(uniqueId, definition);
            if (!isCurrent()) return;
            container.innerHTML = svg;
        } catch (e) {
            if (!isCurrent()) return;
            container.innerHTML = '<p style="color:red; padding: 8px;">Failed to render chart.</p>';
        }
    }
};
