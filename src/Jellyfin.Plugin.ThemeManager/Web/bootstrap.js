// Keep initialization failure isolated from Jellyfin's own startup.
(function () {
    // Loaded once even if both File Transformation and /web/theme-manager.html add this script.
    if (document.documentElement.dataset.themeManagerClient) return;
    const source = document.currentScript.src;
    document.documentElement.dataset.themeManagerClient = 'loading';
    import(new URL('./client.mjs', source).href).then(function () {
        document.documentElement.dataset.themeManagerClient = 'ready';
    }).catch(function (error) {
        document.documentElement.dataset.themeManagerClient = 'unavailable';
        console.error('Theme Manager initialization failed:', error.message);
    });
}());
