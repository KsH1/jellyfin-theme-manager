import test from 'node:test';
import assert from 'node:assert/strict';
import { Generation, ImageSet, isDocumentPageHide, isPublicOrPlayback, librarySlots, paletteVariables, readSession, request, sameSession, themeSignature } from '../../src/Jellyfin.Plugin.ThemeManager/Web/client-core.mjs';

const user = '0123456789abcdef0123456789abcdef';
const api = (overrides = {}) => ({ accessToken: () => 'test-token', getCurrentUserId: () => user, deviceId: () => 'test-device', getUrl: path => 'https://server.test/jellyfin/' + path, ...overrides });
const base = 'https://server.test/jellyfin/ThemeManager/';

test('Jellyfin SPA pagehide must not unload the client', () => {
    const window = {}, view = {};
    assert.equal(isDocumentPageHide({ target: view }, window), false);
    assert.equal(isDocumentPageHide({ target: window }, window), false);
    assert.equal(isDocumentPageHide({ target: window, persisted: false }, window), true);
    assert.equal(isDocumentPageHide({ target: window, persisted: true }, window), true);
});

test('session accepts only the same server and BaseUrl', () => {
    const session = readSession(api(), base);
    assert.equal(session.userId, user);
    assert.equal(session.viewsUrl, 'https://server.test/jellyfin/Users/' + user + '/Views');
    assert.match(session.headers.Authorization, /Token="test-token"/);
    assert.equal(readSession(api(), 'https://other.test/ThemeManager/'), null);
    assert.equal(readSession(api(), 'https://server.test/ThemeManager/'), null);
});
test('logged out or malformed sessions are unavailable', () => {
    for (const value of [null, api({ accessToken: () => '' }), api({ getCurrentUserId: () => 'bad' }), api({ accessToken: () => 'bad\r\nHeader' }), api({ accessToken: () => 'bad"header' })]) assert.equal(readSession(value, base), null);
});
test('switching account or token changes identity', () => {
    const first = readSession(api(), base);
    assert.ok(sameSession(first, readSession(api(), base)));
    assert.ok(!sameSession(first, readSession(api({ getCurrentUserId: () => 'f'.repeat(32) }), base)));
    assert.ok(!sameSession(first, readSession(api({ accessToken: () => 'next-token' }), base)));
    assert.ok(!sameSession(first, null));
});
test('request refuses cross-server token forwarding before fetch', async () => {
    await assert.rejects(request(readSession(api(), base), 'https://evil.test/'), /cross-server/);
    await assert.rejects(request(readSession(api(), base), '../Users/other/Views'), /cross-server/);
});
test('every library gets a card; only the first of each themed type uses its banner', () => {
    const result = librarySlots({ Items: [
        { Id: '1'.repeat(32), CollectionType: 'movies', Name: 'Film' },
        { Id: '2'.repeat(32), CollectionType: 'tvshows', Name: 'Serier' },
        { Id: '3'.repeat(32), CollectionType: 'boxsets', Name: 'Collections' },
        { Id: '4'.repeat(32), CollectionType: 'music', Name: 'Musik' },
        { Id: '5'.repeat(32), CollectionType: 'movies', Name: '  Børnefilm  ' },
        { Id: '6'.repeat(32), Name: 'Blandet <b>"x"</b>' },
        { Id: 'bad-selector', CollectionType: 'movies', Name: 'Ignored' }
    ] });
    assert.equal(result.size, 6);
    assert.deepEqual([...result.values()].map(v => v.slot), ['movies', 'tvShows', 'collections', null, null, null]);
    assert.equal(result.get('5'.repeat(32)).name, 'Børnefilm');
    assert.equal(result.get('6'.repeat(32)).name, 'Blandet <b>"x"</b>'); // kept as plain text; rendered only via CSS attr()
    assert.match(result.get('4'.repeat(32)).position, /^\d{1,3}%$/);
    assert.equal(librarySlots({ Items: [{ Id: '7'.repeat(32), Name: 'x'.repeat(200) }] }).get('7'.repeat(32)).name.length, 60);
});
test('palette rejects CSS injection and computes contrast', () => {
    const values = paletteVariables({ accent: 'url(https://evil.test)', text: '#123456', background: 'red;display:none' });
    assert.equal(values['--tm-accent'], '#00a4dc'); assert.equal(values['--tm-text'], '#123456'); assert.equal(values['--tm-background'], '#080b12');
    assert.equal(paletteVariables({ accent: '#ffffff' })['--tm-accent-contrast'], '#000000');
    assert.equal(paletteVariables({ accent: '#000000' })['--tm-accent-contrast'], '#ffffff');
});
test('a changed image revision invalidates the theme signature', () => {
    const theme = { id: 'ocean', palette: {}, assets: { background: { revision: 'one' } } };
    const before = themeSignature(theme); theme.assets.background.revision = 'two';
    assert.notEqual(before, themeSignature(theme)); assert.equal(themeSignature(null), 'original');
});
test('late requests cannot apply after account change or cancellation', () => {
    const guard = new Generation(), first = guard.begin(), second = guard.begin();
    assert.equal(first.current(), false); assert.equal(first.signal.aborted, true); assert.equal(second.current(), true);
    guard.cancel(); assert.equal(second.current(), false); assert.equal(second.signal.aborted, true);
});
for (const path of ['#/login', '#/login?serverId=abc', '#!/selectserver', '#/video?itemId=123', '#/fullscreenvideo']) {
    test('suspends overlays on ' + path, () => assert.equal(isPublicOrPlayback(path), true));
}
for (const path of ['#/home', '#/movies', '#/details?id=1', '#/mypreferencesmenu']) {
    test('allows themes on ' + path, () => assert.equal(isPublicOrPlayback(path), false));
}
test('image cleanup waits for late parallel loads on failure', async () => {
    const savedFetch = globalThis.fetch, savedCreate = URL.createObjectURL, savedRevoke = URL.revokeObjectURL;
    const revoked = [], created = [];
    URL.createObjectURL = () => { const value = 'blob:' + created.length; created.push(value); return value; };
    URL.revokeObjectURL = value => revoked.push(value);
    globalThis.fetch = async url => {
        if (String(url).endsWith('/background')) return { ok: false };
        await new Promise(resolve => setTimeout(resolve, 10));
        return { ok: true, blob: async () => new Blob(['test']) };
    };
    try {
        const images = new ImageSet();
        await assert.rejects(images.load(readSession(api(), base), { id: 'sample', assets: { background: {}, movies: {} } }, new AbortController().signal));
        assert.equal(created.length, 1); assert.deepEqual(revoked, created); assert.deepEqual(images.urls, {});
    } finally { globalThis.fetch = savedFetch; URL.createObjectURL = savedCreate; URL.revokeObjectURL = savedRevoke; }
});
test('cancelled image load never creates an object URL', async () => {
    const savedFetch = globalThis.fetch, controller = new AbortController(); controller.abort();
    globalThis.fetch = async () => ({ ok: true, blob: async () => new Blob(['test']) });
    try {
        const images = new ImageSet();
        await assert.rejects(images.load(readSession(api(), base), { id: 'sample', assets: { movies: {} } }, controller.signal), { name: 'AbortError' });
        assert.deepEqual(images.urls, {});
    } finally { globalThis.fetch = savedFetch; }
});
test('preview slots use the revisioned reduced copy; other slots stay full size and uncached', async () => {
    const savedFetch = globalThis.fetch, calls = [];
    globalThis.fetch = async (url, options) => { calls.push([String(url), options.cache]); return { ok: true, blob: async () => new Blob(['test']) }; };
    try {
        const images = new ImageSet();
        await images.load(readSession(api(), base), { id: 'halloween', assets: { background: { revision: 'aa11' }, movies: { revision: 'bb22' } } }, new AbortController().signal, ['movies']);
        images.dispose();
        assert.deepEqual(calls.sort(), [
            [base + 'Themes/halloween/Assets/background', 'no-store'],
            [base + 'Themes/halloween/Assets/movies/Preview?v=bb22', 'default'],
        ]);
    } finally { globalThis.fetch = savedFetch; }
});
test('themes are grouped into tabs and calendar themes follow the year', async () => {
    const { groupThemes, themeGroup } = await import('../../src/Jellyfin.Plugin.ThemeManager/Web/client-core.mjs');
    const t = (name, category, calendar) => ({ id: name.toLowerCase(), name, category, calendar });
    const themes = [
        t('Christmas', 'Calendar', { kind: 'Range', start: '11-24', end: '12-26' }), t('Easter', 'Calendar', { kind: 'EasterOffset', startOffset: -3, endOffset: 1 }),
        t('New Year', 'Calendar', { kind: 'Range', start: '12-30', end: '01-02' }), t('Fastelavn', 'Calendar', { kind: 'EasterOffset', startOffset: -51, endOffset: -49 }),
        t('Valentine', 'Calendar', { kind: 'Range', start: '02-14', end: '02-14' }), t('December', 'Calendar', { kind: 'Month', month: 12 }),
        t('April', 'Calendar', { kind: 'Month', month: 4 }), t('Retro Neon', 'Permanent'), t('Anime City', 'Permanent'), t('Sample', 'Custom'), t('Odd', 'Calendar'),
    ];
    const grouped = groupThemes(themes), names = key => grouped.get(key).map(theme => theme.name);
    assert.deepEqual(names('holidays'), ['New Year', 'Fastelavn', 'Valentine', 'Easter', 'Christmas', 'Odd']);
    assert.deepEqual(names('months'), ['April', 'December']);
    assert.deepEqual(names('permanent'), ['Anime City', 'Retro Neon']);
    assert.deepEqual(names('custom'), ['Sample']);
    assert.equal(themeGroup({ category: 'Custom', calendar: { kind: 'Month', month: 1 } }), 'custom');
});
test('the themed banner goes to the library named for its type, not the alphabetically first', () => {
    const result = librarySlots({ Items: [
        { Id: 'a'.repeat(32), CollectionType: 'movies', Name: 'Børnefilm' },
        { Id: 'b'.repeat(32), CollectionType: 'movies', Name: 'Film' },
        { Id: 'c'.repeat(32), CollectionType: 'tvshows', Name: 'Anime' },
        { Id: 'd'.repeat(32), CollectionType: 'tvshows', Name: 'TV-serier' },
        { Id: 'e'.repeat(32), CollectionType: 'boxsets', Name: 'Mine samlinger' },
        { Id: 'f'.repeat(32), CollectionType: 'movies', Name: 'Movies' },
    ] });
    const slot = c => result.get(c.repeat(32)).slot;
    assert.equal(slot('a'), null); assert.equal(slot('b'), 'movies'); assert.equal(slot('f'), null); // first matching name wins
    assert.equal(slot('c'), null); assert.equal(slot('d'), 'tvShows');
    assert.equal(slot('e'), 'collections'); // no matching name: the only one keeps the banner
});
test('calendar editor helpers keep MM-DD valid and copy Automatic Seasonal', async () => {
    const { splitMonthDay, joinMonthDay, automaticEntries, describeRule, calendarId } = await import('../../src/Jellyfin.Plugin.ThemeManager/Web/schedule-editor.mjs');
    assert.deepEqual(splitMonthDay('12-24'), [12, 24]);
    assert.equal(calendarId('Børnekalender'), 'boernekalender');
    assert.equal(calendarId('  Påske & Æbler — Café! '), 'paaske-aebler-cafe');
    assert.equal(calendarId('!!!'), '');
    assert.deepEqual(splitMonthDay('bad'), [1, 1]);
    assert.equal(joinMonthDay(2, 31), '02-29');
    assert.equal(joinMonthDay(7, 0), '07-01');
    const entries = automaticEntries([{ id: 'halloween', calendar: { kind: 'Range', start: '10-01', end: '10-31', priority: 80 } }, { id: 'ocean' }]);
    assert.deepEqual(entries, [{ themeId: 'halloween', rule: { kind: 'Range', start: '10-01', end: '10-31', priority: 80 } }]);
    assert.equal(describeRule({ kind: 'Month', month: 7 }), 'July');
    assert.equal(describeRule({ kind: 'Range', start: '12-30', end: '01-02' }), '30 Dec – 2 Jan');
    assert.equal(describeRule({ kind: 'EasterOffset', startOffset: -3, endOffset: 1 }), 'Easter -3 to +1 days');
});
test('server validation messages reach the user; bodies without one get the generic message', async () => {
    const savedFetch = globalThis.fetch;
    try {
        globalThis.fetch = async () => ({ ok: false, status: 400, json: async () => ({ error: 'Every date must use an installed theme.' }) });
        await assert.rejects(request(readSession(api(), base), 'Me/Schedule', { method: 'PUT', body: {} }), { message: 'Every date must use an installed theme.', status: 400 });
        globalThis.fetch = async () => ({ ok: false, status: 500, json: async () => { throw new SyntaxError('html'); } });
        await assert.rejects(request(readSession(api(), base), 'Me'), /Theme request failed \(500\)/);
    } finally { globalThis.fetch = savedFetch; }
});
