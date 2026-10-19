// Pure helpers also exercised by Node's built-in test runner. No token persistence.
export const slots = ['background', 'collections', 'movies', 'tvShows'];
export const normalizeId = value => String(value || '').replaceAll('-', '').toLowerCase();
export const isDocumentPageHide = (event, browserWindow) => event.target === browserWindow && typeof event.persisted === 'boolean';
export function sameSession(a, b) { return !!a && !!b && a.key === b.key; }

export function readSession(api, pluginBase) {
    try {
        if (!api || !api.accessToken() || !api.getCurrentUserId()) return null;
        const base = new URL(pluginBase);
        const endpoint = new URL(api.getUrl('ThemeManager/Me'));
        if (endpoint.href !== new URL('Me', base).href) return null;
        const token = String(api.accessToken()), userId = normalizeId(api.getCurrentUserId());
        if (!/^[a-f0-9]{32}$/.test(userId) || /["\\\r\n]/.test(token)) return null;
        const device = String(api.deviceId()).replace(/["\\\r\n]/g, '');
        return {
            key: base.href + '|' + userId + '|' + token, userId, base,
            headers: { Authorization: 'MediaBrowser Client="ThemeManager", Device="Web", DeviceId="' + device + '", Version="0.2.0", Token="' + token + '"' },
            viewsUrl: new URL('../Users/' + userId + '/Views', base).href
        };
    } catch { return null; }
}

export async function request(session, path, { method = 'GET', body, signal } = {}) {
    const url = new URL(path, session.base);
    // The caller may ask only for this plugin or this user's views on the same server.
    if (url.origin !== session.base.origin || (!url.href.startsWith(session.base.href) && url.href !== session.viewsUrl)) throw new Error('Refusing a cross-server request.');
    const headers = { ...session.headers };
    if (body !== undefined) headers['Content-Type'] = 'application/json';
    const response = await fetch(url, { method, headers, signal, cache: 'no-store', credentials: 'same-origin', body: body === undefined ? undefined : JSON.stringify(body) });
    if (!response.ok) {
        // Prefer the server's own short explanation (validation messages); never echo arbitrary HTML.
        let detail = null; try { detail = await response.json(); } catch { }
        const reason = typeof detail?.error === 'string' ? detail.error : typeof detail?.detail === 'string' ? detail.detail : null;
        const error = new Error(reason ? reason.slice(0, 300) : 'Theme request failed (' + response.status + '). Try again or use normal Jellyfin.'); error.status = response.status; throw error;
    }
    return response.status === 204 ? null : response.json();
}

const bannerTypes = { movies: 'movies', tvshows: 'tvShows', boxsets: 'collections' };
// Names that mean "the" library of a type. The theme banners carry that title, so they suit only such a library.
const typeNames = {
    movies: ['movies', 'movie', 'film', 'films', 'filmer', 'spillefilm'],
    tvShows: ['tvshows', 'tv', 'series', 'serier', 'shows', 'tvserier'],
    collections: ['collections', 'samlinger', 'boxsets'],
};
const plainName = name => String(name ?? '').toLocaleLowerCase().replace(/[^\p{L}\p{N}]/gu, '');

// Every library gets an entry. One library per banner type uses the theme's banner: the one whose name matches
// the type (e.g. "Film" rather than "Børnefilm"), otherwise the first. Every other library, including a second
// movie library, gets a generic banner made from the theme background with its own name.
export function librarySlots(views) {
    const items = (views?.Items || []).map(item => ({ item, id: normalizeId(item.Id) })).filter(entry => /^[a-f0-9]{32}$/.test(entry.id));
    const owner = new Map();
    for (const { item, id } of items) {
        const slot = bannerTypes[item.CollectionType];
        if (slot && (!owner.has(slot) || (typeNames[slot].includes(plainName(item.Name)) && !owner.get(slot).named))) owner.set(slot, { id, named: typeNames[slot].includes(plainName(item.Name)) });
    }
    const map = new Map();
    for (const { item, id } of items) {
        const slot = bannerTypes[item.CollectionType];
        // Spread generic cards across the background so neighbouring libraries do not look identical.
        map.set(id, { slot: slot && owner.get(slot).id === id ? slot : null, name: String(item.Name ?? '').trim().slice(0, 60), position: parseInt(id.slice(0, 4), 16) % 101 + '%' });
    }
    return map;
}

export function paletteVariables(palette) {
    const defaults = { accent: '#00a4dc', secondary: '#aa5cc3', surface: '#101722', text: '#ffffff', background: '#080b12' };
    const result = {};
    for (const [key, fallback] of Object.entries(defaults)) result['--tm-' + key] = /^#[a-f0-9]{6}$/i.test(palette?.[key]) ? palette[key] : fallback;
    const rgb = result['--tm-accent'].slice(1).match(/../g).map(v => parseInt(v, 16) / 255).map(v => v <= .04045 ? v / 12.92 : ((v + .055) / 1.055) ** 2.4);
    const luminance = .2126 * rgb[0] + .7152 * rgb[1] + .0722 * rgb[2];
    result['--tm-accent-contrast'] = luminance > .179 ? '#000000' : '#ffffff';
    return result;
}

export function themeSignature(theme) {
    return theme ? JSON.stringify([theme.id, theme.palette, slots.map(slot => theme.assets?.[slot]?.revision || '')]) : 'original';
}

export class Generation {
    number = 0;
    controller = null;
    begin() {
        this.cancel();
        this.controller = new AbortController();
        const number = this.number;
        return { signal: this.controller.signal, current: () => this.number === number };
    }
    cancel() { this.number++; this.controller?.abort(); this.controller = null; }
}

export class ImageSet {
    urls = {};
    // Slots in previewSlots use the server's reduced copy; its revisioned URL may come from the browser cache.
    async load(session, theme, signal, previewSlots = []) {
        try {
            const results = await Promise.allSettled(slots.map(async slot => {
                if (!theme.assets?.[slot]) return;
                const preview = previewSlots.includes(slot);
                const url = new URL('Themes/' + encodeURIComponent(theme.id) + '/Assets/' + slot + (preview ? '/Preview' : ''), session.base);
                if (preview && theme.assets[slot].revision) url.searchParams.set('v', theme.assets[slot].revision);
                const response = await fetch(url, { headers: session.headers, signal, cache: preview ? 'default' : 'no-store', credentials: 'same-origin' });
                if (!response.ok) throw new Error('Theme artwork is unavailable.');
                const blob = await response.blob();
                if (signal.aborted) throw new DOMException('Cancelled', 'AbortError');
                this.urls[slot] = URL.createObjectURL(blob);
            }));
            const failure = results.find(result => result.status === 'rejected');
            if (failure) throw failure.reason;
            return this;
        } catch (error) { this.dispose(); throw error; }
    }
    dispose() { for (const url of Object.values(this.urls)) URL.revokeObjectURL(url); this.urls = {}; }
}

export function isPublicOrPlayback(hash) {
    return /^#!?\/(?:login|selectserver|addserver|forgotpassword|wizard|video|videoplayer|fullscreenvideo)(?:[/?]|$)/i.test(hash);
}

export const groups = [['holidays', 'Holidays'], ['months', 'Months'], ['permanent', 'Permanent'], ['custom', 'Custom']];
export function themeGroup(theme) {
    if (theme.category === 'Permanent') return 'permanent';
    if (theme.category === 'Calendar') return theme.calendar?.kind === 'Month' ? 'months' : 'holidays';
    return 'custom';
}
// Approximate position in the year, only for ordering. Easter offsets use a fixed reference Easter (5 April).
function yearPosition(rule) {
    if (!rule) return 9999;
    if (rule.kind === 'Month') return rule.month * 100;
    if (rule.kind === 'EasterOffset') { const date = new Date(Date.UTC(2026, 3, 5 + rule.startOffset)); return (date.getUTCMonth() + 1) * 100 + date.getUTCDate(); }
    const [start, end] = [rule.start, rule.end].map(value => Number(String(value).replace('-', '')));
    return start > end ? 0 : start; // A range across New Year sorts first.
}
export function groupThemes(themes) {
    const result = new Map(groups.map(([key]) => [key, []]));
    for (const theme of themes) result.get(themeGroup(theme)).push(theme);
    for (const list of result.values()) list.sort((a, b) => yearPosition(a.calendar) - yearPosition(b.calendar) || a.name.localeCompare(b.name));
    return result;
}

// The small, revisioned background copy for a picker tile; null for a palette-only theme.
export function thumbnailPath(theme) {
    const background = theme?.assets?.background;
    if (!background) return null;
    return 'Themes/' + encodeURIComponent(theme.id) + '/Assets/background/Preview?size=thumb' + (background.revision ? '&v=' + encodeURIComponent(background.revision) : '');
}

// Effects for the standard themes, so servers whose packs predate the "effect" field still get them.
const standardEffects = {
    christmas: 'snow', 'month-12': 'snow', 'month-01': 'snow', 'month-02': 'snow',
    halloween: 'leaves', 'month-10': 'leaves', 'month-11': 'leaves',
    valentine: 'hearts', newyear: 'confetti',
};
export const effectKinds = ['snow', 'leaves', 'hearts', 'confetti'];
export const effectsStorageKey = 'themeManager.effects';

/** The effect to show for a theme: its own "effect" field wins, then the standard one; null for none. */
export function effectFor(theme) {
    if (!theme) return null;
    const effect = theme.effect ?? standardEffects[theme.id] ?? null;
    return effectKinds.includes(effect) ? effect : null;
}

/** Effects show only when the server allows them, this user has not switched them off and the system does not ask for less motion. */
export function effectsWanted({ allowed, userOff, reducedMotion }) {
    return allowed !== false && !userOff && !reducedMotion;
}

/** Dashboard and plugin settings pages: the theme shows there, but effects would only get in the way. */
export function isAdminPage(hash) {
    return /^#!?\/(?:dashboard|configurationpage)(?:[/?]|$)/i.test(hash);
}
