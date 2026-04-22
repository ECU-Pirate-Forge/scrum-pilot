window.mermaidHelper = {
    init: function () {
        if (window.mermaid) {
            mermaid.initialize({ startOnLoad: false, theme: 'default', securityLevel: 'loose' });
        }
    },
    render: async function (containerId, definition) {
        if (!window.mermaid) return;
        const container = document.getElementById(containerId);
        if (!container) return;
        try {
            container.innerHTML = '';
            const uniqueId = 'mermaid-' + Date.now();
            const { svg } = await mermaid.render(uniqueId, definition);
            container.innerHTML = svg;
        } catch (e) {
            container.innerHTML = '<p style="color:red; padding: 8px;">Failed to render chart: ' + e + '</p>';
        }
    }
};
