// Applies the ScrumPilot theme preference ("light" | "dark" | "system") to <html data-theme>.
// Returns true when the effective theme is dark.
window.scrumPilotTheme = (function () {
    const media = window.matchMedia ? window.matchMedia('(prefers-color-scheme: dark)') : null;
    let current = 'system';

    function resolve(pref) {
        if (pref === 'dark') return 'dark';
        if (pref === 'light') return 'light';
        return media && media.matches ? 'dark' : 'light';
    }

    function apply(pref) {
        current = pref || 'system';
        const effective = resolve(current);
        document.documentElement.setAttribute('data-theme', effective);
        document.documentElement.style.colorScheme = effective;
        return effective === 'dark';
    }

    if (media && media.addEventListener) {
        media.addEventListener('change', function () {
            if (current === 'system') apply('system');
        });
    }

    apply('system');
    return { apply: apply };
})();
