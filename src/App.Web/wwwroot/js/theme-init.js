// Applies the saved (or system-preferred) theme before first paint to avoid a flash. Externalized from
// AppRoot so the app can ship a strict Content-Security-Policy (no inline scripts).
(function () {
    window.dmsTheme = {
        get: function () {
            try {
                return localStorage.getItem('dms_theme')
                    || (window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light');
            } catch (e) { return 'light'; }
        },
        apply: function (t) {
            try {
                document.documentElement.setAttribute('data-theme', t);
                localStorage.setItem('dms_theme', t);
            } catch (e) { }
        }
    };
    document.documentElement.setAttribute('data-theme', window.dmsTheme.get());
})();
