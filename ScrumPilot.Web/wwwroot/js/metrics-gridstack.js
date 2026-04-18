window.metricsGridstack = {
    _grid: null,
    _dotnet: null,

    init: function (dotnetRef, savedLayout) {
        if (this._grid) {
            this._grid.destroy(false);
            this._grid = null;
        }
        this._dotnet = dotnetRef;
        this._grid = GridStack.init({
            float: false,
            cellHeight: 80,
            margin: 8,
            animate: true,
            resizable: { handles: 'se' },
        }, '.metrics-grid');

        if (savedLayout && savedLayout.length > 0) {
            savedLayout.forEach(function (item) {
                const el = document.querySelector('.metrics-grid [gs-id="' + item.id + '"]');
                if (el) {
                    window.metricsGridstack._grid.update(el, { x: item.x, y: item.y, w: item.w, h: item.h });
                }
            });
        }

        this._grid.on('change', function () {
            if (!window.metricsGridstack._dotnet) return;
            const layout = window.metricsGridstack.getLayout();
            window.metricsGridstack._dotnet.invokeMethodAsync('OnLayoutChanged', layout);
        });
    },

    destroy: function () {
        if (this._grid) {
            this._grid.destroy(false);
            this._grid = null;
        }
        this._dotnet = null;
    },

    getLayout: function () {
        if (!this._grid) return [];
        return this._grid.save(false).map(function (item) {
            return { id: item.id, x: item.x, y: item.y, w: item.w, h: item.h };
        });
    }
};
