import { Generation, ImageSet, effectFor, effectsStorageKey, effectsWanted, isAdminPage, isDocumentPageHide, isPlayback, isPublicOrPlayback, librarySlots, normalizeId, paletteVariables, readSession, request, sameSession, themeSignature } from './client-core.mjs';
import { createEffects } from './effects.mjs';
import { createPicker } from './theme-picker.mjs';

const pluginBase = new URL('../', import.meta.url);
const guard = new Generation();
let session = null, activeTheme = null, images = null, signature = '', libraryMap = new Map(), bannerChoices = '{}';
let nextRefresh = 0, inFlight = false, stopped = false, frame = 0;
const decorated = new Set();
const css = document.createElement('link'); css.rel = 'stylesheet'; css.href = new URL('./client.css', import.meta.url); css.dataset.themeManager = 'client';
document.head.append(css);
const current = () => readSession(window.ApiClient, pluginBase);
const effects = createEffects();
let effectsAllowed = false;
const reducedMotion = matchMedia('(prefers-reduced-motion: reduce)');
function effectsOff() { try { return localStorage.getItem(effectsStorageKey) === 'off'; } catch { return false; } }
function showEffect() { effects.set(activeTheme && !isHidden() && !isAdminPage(location.hash) && effectsWanted({ allowed: effectsAllowed, userOff: effectsOff(), reducedMotion: reducedMotion.matches }) ? effectFor(activeTheme) : null); }
const picker = createPicker(current, async () => { nextRefresh = 0; await sync(true); }, showEffect);
picker.available(false);

// Two layers: the palette (data-tm-accent, also in the player) and the page theme (data-tm-theme: background, cards, effects).
function clearPage() {
    delete document.documentElement.dataset.tmTheme;
    document.documentElement.style.removeProperty('--tm-background-image');
    for (const image of decorated) { delete image.dataset.tmCard; delete image.dataset.tmLabel; image.style.removeProperty('--tm-card-art'); image.style.removeProperty('--tm-card-x'); }
    decorated.clear();
    effects.set(null);
}
function clearDecoration() {
    clearPage();
    delete document.documentElement.dataset.tmAccent;
    for (const name of ['accent', 'secondary', 'surface', 'text', 'background', 'accent-contrast', 'accent-channel']) document.documentElement.style.removeProperty('--tm-' + name);
}
function reset() {
    guard.cancel(); clearDecoration(); images?.dispose(); images = null; activeTheme = null; signature = ''; libraryMap = new Map();
    inFlight = false; nextRefresh = 0; picker.reset();
}
function isHidden() { return isPublicOrPlayback(location.hash) || !!document.fullscreenElement; }
function isPlaying() { return isPlayback(location.hash) || !!document.fullscreenElement; }
function decorate() {
    if (!activeTheme || (isHidden() && !isPlaying())) { clearDecoration(); return; }
    const html = document.documentElement;
    for (const [key, value] of Object.entries(paletteVariables(activeTheme.palette))) html.style.setProperty(key, value);
    html.dataset.tmAccent = activeTheme.id;
    // Never anything over the video: in the player only the accent colour remains.
    if (isHidden()) { clearPage(); return; }
    html.dataset.tmTheme = activeTheme.id;
    if (images?.urls.background) html.style.setProperty('--tm-background-image', 'url("' + images.urls.background + '")');
    // Only the user's library/view IDs are changed. Posters for episodes and films are untouched.
    for (const card of document.querySelectorAll('.card[data-id]')) {
        const view = libraryMap.get(normalizeId(card.dataset.id));
        const container = card.querySelector('.cardImageContainer');
        if (!view || !container) continue;
        const art = view.slot && images?.urls[view.slot];
        // Without themed art (or for a theme with only a palette) there is no background to build from: keep Jellyfin's card.
        if (!art && !images?.urls.background) continue;
        container.dataset.tmCard = art ? view.slot : 'generic';
        container.style.setProperty('--tm-card-art', 'url("' + (art || images.urls.background) + '")');
        // The name is text in an attribute, rendered by CSS attr(); it is never parsed as HTML.
        if (art) delete container.dataset.tmLabel; else { container.dataset.tmLabel = view.name; container.style.setProperty('--tm-card-x', view.position); }
        decorated.add(container);
    }
    for (const node of decorated) if (!node.isConnected) decorated.delete(node);
    showEffect();
}
function scheduleDecoration() { if (!frame) frame = requestAnimationFrame(() => { frame = 0; decorate(); }); }

async function sync(force = false) {
    if (stopped) return;
    if (force) { guard.cancel(); inFlight = false; nextRefresh = 0; }
    const found = current();
    if (!sameSession(found, session)) { reset(); session = found; }
    picker.available(!!session && !isHidden());
    if (!session) return;
    if (isHidden()) { decorate(); return; }
    if ((!force && Date.now() < nextRefresh) || inFlight) { decorate(); return; }
    const captured = session, job = guard.begin(); inFlight = true;
    let pendingImages = null;
    try {
        const [effective, views] = await Promise.all([
            request(captured, 'Effective', { signal: job.signal }),
            request(captured, captured.viewsUrl, { signal: job.signal })
        ]);
        const nextSignature = themeSignature(effective.theme);
        if (effective.theme && nextSignature !== signature) pendingImages = await new ImageSet().load(captured, effective.theme, job.signal, ['collections', 'movies', 'tvShows']);
        if (!job.current() || !sameSession(captured, current())) { pendingImages?.dispose(); return; }
        if (nextSignature !== signature) {
            clearDecoration(); images?.dispose(); images = pendingImages; pendingImages = null;
        }
        // A library switched to "none" must lose the banner it already has.
        const nextChoices = JSON.stringify(effective.libraryBanners || {});
        if (nextChoices !== bannerChoices) { clearDecoration(); bannerChoices = nextChoices; }
        activeTheme = effective.theme; signature = nextSignature; libraryMap = librarySlots(views, effective.libraryBanners); effectsAllowed = effective.effects === true;
        nextRefresh = Date.now() + 60000; decorate();
    } catch (error) {
        pendingImages?.dispose();
        if (error.name !== 'AbortError' && job.current()) {
            clearDecoration(); nextRefresh = Date.now() + 30000;
            console.warn('Theme Manager could not refresh. The normal Jellyfin view remains available.');
            if (force) throw error;
        }
    } finally { if (job.current()) inFlight = false; }
}

function onView() { void sync(); scheduleDecoration(); }
function onVisibility() { if (!document.hidden) { nextRefresh = 0; void sync(); } }
const observer = new MutationObserver(scheduleDecoration);
observer.observe(document.body, { childList: true, subtree: true });
document.addEventListener('viewshow', onView);
document.addEventListener('visibilitychange', onVisibility);
document.addEventListener('fullscreenchange', onView);
window.addEventListener('hashchange', onView);
// Local identity check once per second; network requests are at most once per minute unless explicitly saving.
const timer = setInterval(() => { void sync(); }, 1000);
window.addEventListener('pagehide', event => {
    // Jellyfin also bubbles a custom pagehide for SPA views. It is not a document unload.
    if (!isDocumentPageHide(event, window)) return;
    reset(); session = null;
    if (!event.persisted) {
        stopped = true; effects.destroy(); clearInterval(timer); observer.disconnect(); cancelAnimationFrame(frame); picker.destroy(); css.remove();
        document.removeEventListener('viewshow', onView); document.removeEventListener('visibilitychange', onVisibility);
        document.removeEventListener('fullscreenchange', onView); window.removeEventListener('hashchange', onView);
    }
});
void sync();
