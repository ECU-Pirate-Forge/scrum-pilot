window.mermaidHelper = {
    init: function () {
        if (window.mermaid) {
            mermaid.initialize({ startOnLoad: false, theme: 'default', securityLevel: 'strict', flowchart: { htmlLabels: false } });
        }
    },
    render: async function (containerId, definition) {
        const container = document.getElementById(containerId);
        if (!container) return;
        const requestId = crypto.randomUUID();
        container.dataset.renderRequest = requestId;
        try {
            if (!window.mermaid) throw new Error('Mermaid is unavailable.');
            const uniqueId = 'mermaid-' + requestId;
            const { svg } = await mermaid.render(uniqueId, definition);
            if (container.dataset.renderRequest !== requestId) return;
            container.innerHTML = svg;
        } catch (e) {
            if (container.dataset.renderRequest !== requestId) return;
            container.textContent = 'Failed to render chart. Retry or use the Fluent client dependency view.';
            throw e;
        }
    }
};
