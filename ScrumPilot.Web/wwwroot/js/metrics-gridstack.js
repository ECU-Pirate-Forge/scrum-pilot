window.metricsGridstack = {
    _grid: null,
    _dotnet: null,
    _owner: null,

    init: function (dotnetRef, savedLayout, owner) {
        if (this._grid) {
            this._grid.destroy(false);
            this._grid = null;
        }
        this._dotnet = dotnetRef;
        this._owner = owner;
        // Restore desktop bounds before GridStack adapts them to the viewport.
        if (savedLayout && savedLayout.length > 0) {
            savedLayout.forEach(function (item) {
                const el = document.querySelector('.metrics-grid [gs-id="' + item.id + '"]');
                if (el) {
                    ['x', 'y', 'w', 'h'].forEach(function (key) {
                        el.setAttribute('gs-' + key, item[key]);
                    });
                }
            });
        }
        this._grid = GridStack.init({
            float: false,
            cellHeight: 80,
            margin: 8,
            animate: true,
            resizable: { handles: 'se' },
        }, '.metrics-grid');

        this._grid.on('change', function () {
            if (window.metricsGridstack._owner !== owner) return;
            const layout = window.metricsGridstack.getLayout(owner);
            dotnetRef.invokeMethodAsync('OnLayoutChanged', layout).catch(function () {
                // The routed dashboard may have been disposed while this callback was queued.
            });
        });
    },

    destroy: function (owner) {
        if (this._owner !== owner) return;
        if (this._grid) {
            this._grid.destroy(false);
            this._grid = null;
        }
        this._dotnet = null;
        this._owner = null;
    },

    getLayout: function (owner) {
        if (!this._grid || this._owner !== owner) return [];
        return this._grid.save(false).map(function (item) {
            return { id: item.id, x: item.x, y: item.y, w: item.w, h: item.h };
        });
    }
};
