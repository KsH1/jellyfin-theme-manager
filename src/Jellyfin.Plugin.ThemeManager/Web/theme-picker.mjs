import { Generation, ImageSet, effectsStorageKey, groupThemes, groups, paletteVariables, request, sameSession, slots, themeGroup, thumbnailPath } from './client-core.mjs';
import { createScheduleEditor, scheduleEditorCss } from './schedule-editor.mjs';

export function createPicker(getSession, onSaved, onEffects = () => {}) {
    const host = document.createElement('div');
    host.id = 'theme-manager-picker';
    const root = host.attachShadow({ mode: 'open' });
    // This template is static. Pack names and API responses are only assigned through textContent.
    root.innerHTML = `<style>
    :host{position:fixed;right:22px;bottom:22px;z-index:10010;font:15px system-ui,sans-serif;color:#f3f5fa;color-scheme:dark}
    *{box-sizing:border-box}button,select,input{font:inherit}button{cursor:pointer}button:disabled{opacity:.5;cursor:wait}
    button:focus-visible,select:focus-visible,input:focus-visible{outline:3px solid #87deff;outline-offset:3px}
    .launch{background:#162037ed;color:#f3f5fa;border:1px solid #8295b866;border-radius:30px;padding:11px 18px;box-shadow:0 8px 24px #0007}
    dialog{margin:auto;width:min(1060px,94vw);max-height:90dvh;overflow:auto;padding:0;border:1px solid #6b7b9955;border-radius:20px;background:#0d1424;color:#f3f5fa;box-shadow:0 25px 100px #0009}
    dialog::backdrop{background:#030817c9;backdrop-filter:blur(5px)}header{position:sticky;top:0;z-index:2;background:#101a2cf5;padding:22px 26px;display:flex;justify-content:space-between;gap:15px;align-items:center;border-bottom:1px solid #c5d9ff18}
    h1{font-size:23px;margin:0 0 5px}h2{font-size:20px;margin:0}p{line-height:1.55;margin:.5em 0;color:#b9c5dc}header p{font-size:13px;margin:0}
    .close{border:1px solid #9caacd66;background:transparent;color:white;border-radius:10px;padding:9px 14px}
    .body{padding:22px 26px}.controls{display:grid;grid-template-columns:1fr 1fr;gap:18px;margin-bottom:20px}label{display:grid;gap:8px;font-size:13px;color:#c9d5ed}
    select,input{width:100%;padding:10px 12px;background:#152138;color:#fff;border:1px solid #65769577;border-radius:9px}
    .preview{position:relative;isolation:isolate;border-radius:14px;overflow:hidden;background:#091323;background-size:cover;background-position:center;padding:24px;min-height:230px;margin-bottom:20px}
    .preview:before{content:'';position:absolute;inset:0;background:linear-gradient(90deg,#030813d9,#03081344);z-index:-1}.preview h2,.preview p{text-shadow:0 2px 7px #000}.preview p{color:#f3f5fa}
    .banners{display:flex;gap:10px;margin-top:20px}.banners img{width:calc((100% - 20px)/3);aspect-ratio:16/9;object-fit:cover;border-radius:8px}
    .swatches{display:flex;gap:7px;margin-top:13px}.swatches span{width:22px;height:22px;border-radius:50%;border:1px solid #ffffff77}
    .grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(160px,1fr));gap:10px;margin-top:13px}
    .tile{text-align:left;min-height:87px;border:1px solid #9cafd038;border-radius:11px;color:white;padding:13px;background:#17243b}.tile span{display:block;font-weight:600;text-shadow:0 1px 6px #000}.tile small{display:block;margin-top:6px;color:#e3eafb;font-size:11px}.tile[aria-pressed=true]{outline:2px solid #a7e4ff;outline-offset:1px}
    footer{position:sticky;bottom:0;background:#101a2cf7;border-top:1px solid #c5d9ff18;padding:15px 26px;display:flex;align-items:center;justify-content:space-between;gap:20px}.status{margin:0;font-size:13px;min-height:20px}.save{border:0;border-radius:10px;background:#8edcff;color:#071523;font-weight:700;padding:12px 20px;white-space:nowrap}
    .tabs{display:flex;flex-wrap:wrap;gap:8px;margin-top:12px}.tab{border:1px solid #9cafd055;background:transparent;color:#d6e0f5;border-radius:20px;padding:7px 14px;font-size:13px}.tab small{opacity:.7;margin-left:6px}.tab[aria-selected=true]{background:#8edcff;border-color:#8edcff;color:#071523;font-weight:700}.tabs.searching .tab{opacity:.45}
    .mycal{margin-top:20px;border:1px solid #9cafd038;border-radius:12px;padding:12px 16px}.mycal summary{cursor:pointer;font-weight:700}.mycal .row{display:flex;gap:10px;margin-top:12px;flex-wrap:wrap}
    .mycal .row button{border:1px solid #9caacd66;background:transparent;color:inherit;border-radius:10px;padding:9px 14px}.mycal .row .primary{background:#8edcff;color:#071523;border-color:#8edcff;font-weight:700}
    ${scheduleEditorCss}
    .hint{font-size:12px}.section-title{display:flex;justify-content:space-between;align-items:center;margin-top:20px}.section-title h2{font-size:16px}
    @media(max-width:600px){:host{bottom:14px;right:14px}.controls{grid-template-columns:1fr}header,.body,footer{padding:16px}.preview{padding:16px;min-height:190px}.grid{grid-template-columns:repeat(2,minmax(0,1fr))}.banners{gap:5px}.banners img{width:calc((100% - 10px)/3)}footer{align-items:flex-start}.save{padding:10px 13px}}
    </style>
    <button class="launch" type="button" aria-label="Choose your Jellyfin theme">◈ Themes</button>
    <dialog aria-labelledby="tm-title">
      <header><div><h1 id="tm-title">Make Jellyfin yours</h1><p>Your account. Your theme. Everyone else keeps theirs.</p></div><button class="close" type="button">Close</button></header>
      <div class="body">
        <div class="controls"><label>Theme mode<select class="mode"><option value="FollowServer">Follow server default</option><option value="Automatic">Automatic Seasonal</option><option value="Fixed">Choose a theme</option><option value="Original">Original Jellyfin</option></select></label><label>Find a theme<input class="search" type="search" placeholder="Search themes…" /></label></div>
        <section class="preview" aria-label="Theme preview"><h2 class="preview-title">Loading your theme…</h2><p class="preview-detail"></p><div class="swatches"></div><div class="banners"></div></section>
        <label class="fx" style="display:flex;flex-direction:row;align-items:center;gap:.5em;font-size:13px;margin:0 0 .6em"><input class="effects" type="checkbox" style="width:auto" /> Seasonal effects (snow, leaves, hearts) on this device</label>
        <p class="hint">Previewing does not save your choice. Automatic themes use the server’s calendar. Original removes this plugin’s styling; existing server-wide custom styling is unchanged.</p>
        <div class="section-title"><h2>Available themes</h2><span class="count"></span></div><div class="tabs" role="tablist" aria-label="Theme categories"></div><div class="grid" role="tabpanel" aria-label="Available themes"></div>
        <details class="mycal" hidden><summary>My own calendar</summary><p class="hint">Make your own "Automatic Seasonal": pick which theme shows on which dates. Only you see it.</p><div class="editor"></div>
          <div class="row"><button class="primary save-cal" type="button">Save my calendar and use it</button><button class="delete-cal" type="button">Delete my calendar</button></div></details>
      </div>
      <footer><p class="status" role="status" aria-live="polite"></p><button class="save" type="button">Save my theme</button></footer>
    </dialog>`;
    document.body.append(host);
    const el = selector => root.querySelector(selector), dialog = el('dialog');
    const generation = new Generation(), opening = new Generation();
    let session = null, themes = [], selected = null, previewImages = null, activeGroup = 'permanent', calendarEditor = null;
    const status = message => { el('.status').textContent = message; };
    const valid = () => dialog.open && sameSession(session, getSession());
    // Calendars are listed in the mode menu as "Schedule:<id>".
    const choice = () => {
        const value = el('.mode').value;
        if (value.startsWith('Schedule:')) return { mode: 'Schedule', themeId: null, scheduleId: value.slice(9) };
        return { mode: value, themeId: value === 'Fixed' ? selected : null };
    };
    function calendarOptions(all) {
        el('.mode').querySelector('optgroup')?.remove();
        const entries = all.shared.map(s => ['Schedule:' + s.id, s.name]);
        if (all.personal) entries.push(['Schedule:personal', 'My own calendar']);
        if (!entries.length) return;
        const group = document.createElement('optgroup'); group.label = 'Calendars';
        for (const [value, text] of entries) { const o = document.createElement('option'); o.value = value; o.textContent = text; group.append(o); }
        el('.mode').append(group);
    }
    async function loadCalendars(job) {
        const all = await request(session, 'Schedules', { signal: job?.signal });
        calendarOptions(all);
        el('.mycal').hidden = !all.allowPersonalSchedules;
        el('.delete-cal').disabled = !all.personal;
        if (all.allowPersonalSchedules) calendarEditor = createScheduleEditor(el('.mycal .editor'), {
            themes, schedule: all.personal ?? { name: 'My calendar', entries: [] },
            test: (schedule, from) => request(session, 'Schedules/Test', { method: 'POST', body: { schedule, from } }),
        });
        return all;
    }
    function clearPreview() { previewImages?.dispose(); previewImages = null; el('.banners').replaceChildren(); el('.swatches').replaceChildren(); el('.preview').style.backgroundImage = 'none'; }
    // Tile thumbnails are fetched once per theme while the picker is open; the URLs are released when it closes.
    const thumbs = new Map(), thumbJob = new Generation();
    let thumbUrls = [];
    function clearThumbs() { thumbJob.cancel(); thumbUrls.forEach(URL.revokeObjectURL); thumbUrls = []; thumbs.clear(); }
    function thumbnail(theme, button, overlay) {
        const path = thumbnailPath(theme);
        if (!path || !session) return;
        if (!thumbs.has(theme.id)) {
            const captured = session, signal = thumbJob.controller?.signal ?? thumbJob.begin().signal;
            thumbs.set(theme.id, fetch(new URL(path, captured.base), { headers: captured.headers, signal, cache: 'default', credentials: 'same-origin' })
                .then(response => response.ok ? response.blob() : null)
                .then(blob => { if (!blob || signal.aborted) return null; const url = URL.createObjectURL(blob); thumbUrls.push(url); return url; })
                .catch(() => null));
        }
        thumbs.get(theme.id).then(url => { if (url && button.isConnected) button.style.background = overlay + ', url("' + url + '") center / cover'; });
    }
    function close() { generation.cancel(); opening.cancel(); clearPreview(); clearThumbs(); if (dialog.open) dialog.close(); session = null; }
    async function preview() {
        const job = generation.begin(); clearPreview(); el('.save').disabled = true;
        try {
            const result = await request(session, 'Preview', { method: 'POST', body: choice(), signal: job.signal });
            if (!job.current() || !valid()) return;
            const theme = result.theme;
            el('.preview-title').textContent = theme?.name || 'Original Jellyfin';
            el('.preview-detail').textContent = theme ? theme.category + ' · ' + (Object.keys(theme.assets).length ? 'Artwork included' : 'Palette preset · bespoke artwork pending') : 'No personal theme overlay';
            el('.preview').style.backgroundColor = theme?.palette.background || '#101010';
            if (theme) {
                for (const [key, value] of Object.entries(paletteVariables(theme.palette))) {
                    if (key === '--tm-accent-contrast') continue;
                    const swatch = document.createElement('span'); swatch.style.background = value; swatch.title = key.replace('--tm-', ''); el('.swatches').append(swatch);
                }
                const images = await new ImageSet().load(session, theme, job.signal, slots);
                if (!job.current() || !valid()) { images.dispose(); return; }
                previewImages = images;
                if (images.urls.background) el('.preview').style.backgroundImage = 'url("' + images.urls.background + '")';
                for (const slot of ['collections', 'movies', 'tvShows']) {
                    if (!images.urls[slot]) continue;
                    const img = document.createElement('img'); img.src = images.urls[slot]; img.alt = { collections: 'Collections', movies: 'Movies', tvShows: 'TV Shows' }[slot]; el('.banners').append(img);
                }
            }
            status(result.resolution.warning || 'Preview only — save when you are happy.');
        } catch (error) { if (error.name !== 'AbortError' && job.current() && valid()) status(error.message); }
        finally { if (job.current() && valid()) el('.save').disabled = false; }
    }
    function tabs(grouped, searching) {
        const visible = groups.filter(([key]) => grouped.get(key).length);
        if (!visible.some(([key]) => key === activeGroup)) activeGroup = visible[0]?.[0] ?? 'permanent';
        el('.tabs').classList.toggle('searching', searching);
        el('.tabs').replaceChildren(...visible.map(([key, label], index) => {
            const tab = document.createElement('button'); tab.type = 'button'; tab.className = 'tab'; tab.setAttribute('role', 'tab');
            const active = !searching && key === activeGroup;
            tab.setAttribute('aria-selected', String(active)); tab.tabIndex = active || (searching && index === 0) ? 0 : -1;
            const count = document.createElement('small'); count.textContent = grouped.get(key).length;
            tab.append(label, count);
            tab.addEventListener('click', () => { activeGroup = key; el('.search').value = ''; gallery(); });
            tab.addEventListener('keydown', event => {
                const step = { ArrowRight: 1, ArrowLeft: -1 }[event.key]; if (!step) return;
                event.preventDefault(); activeGroup = visible[(index + step + visible.length) % visible.length][0]; el('.search').value = ''; gallery();
                el('.tab[aria-selected=true]').focus();
            });
            return tab;
        }));
    }
    function gallery() {
        el('.grid').replaceChildren();
        const query = el('.search').value.trim().toLocaleLowerCase();
        const grouped = groupThemes(themes);
        tabs(grouped, !!query);
        // A search looks through every category; otherwise only the open tab is shown.
        const filtered = query ? groups.flatMap(([key]) => grouped.get(key)).filter(theme => theme.name.toLocaleLowerCase().includes(query)) : grouped.get(activeGroup);
        el('.count').textContent = filtered.length + ' / ' + themes.length;
        for (const theme of filtered) {
            const button = document.createElement('button'); button.type = 'button'; button.className = 'tile';
            button.setAttribute('aria-pressed', String(choice().mode === 'Fixed' && selected === theme.id));
            const colors = paletteVariables(theme.palette);
            button.style.background = 'linear-gradient(135deg,' + colors['--tm-secondary'] + '55,' + colors['--tm-background'] + ')';
            // Darken the lower part so the name stays readable on top of the artwork.
            thumbnail(theme, button, 'linear-gradient(180deg,#0000 20%,#000c)');
            const name = document.createElement('span'); name.textContent = theme.name;
            const kind = document.createElement('small'); kind.textContent = theme.category + (Object.keys(theme.assets).length ? ' · Artwork' : ' · Palette');
            button.append(name, kind); button.addEventListener('click', () => { selected = theme.id; activeGroup = themeGroup(theme); el('.mode').value = 'Fixed'; gallery(); void preview(); });
            el('.grid').append(button);
        }
    }
    async function open() {
        if (dialog.open) return;
        session = getSession(); if (!session) return;
        dialog.showModal(); status('Loading your saved choice…'); el('.save').disabled = true;
        const job = opening.begin();
        try {
            const [catalog, mine] = await Promise.all([request(session, 'Catalog', { signal: job.signal }), request(session, 'Me', { signal: job.signal })]);
            if (!job.current() || !valid()) return;
            // Hidden themes stay out of the picker, except the one this user already has.
            themes = catalog.themes.filter(theme => !theme.hidden || theme.id === mine.themeId); selected = mine.themeId || themes[0]?.id || null;
            await loadCalendars(job);
            if (!job.current() || !valid()) return;
            // Open on the saved theme's category; without a fixed theme, start with the year-round themes.
            const saved = themes.find(theme => theme.id === mine.themeId);
            activeGroup = saved ? themeGroup(saved) : 'permanent';
            el('.mode').value = mine.mode === 'Schedule' ? 'Schedule:' + mine.scheduleId : mine.mode;
            if (!el('.mode').value) el('.mode').value = 'FollowServer';
            el('.search').value = ''; gallery(); await preview();
        } catch (error) { if (error.name !== 'AbortError' && job.current() && valid()) status(error.message); }
    }
    el('.launch').addEventListener('click', () => { void open(); });
    el('.close').addEventListener('click', close);
    dialog.addEventListener('cancel', event => { event.preventDefault(); close(); });
    el('.mode').addEventListener('change', () => { gallery(); void preview(); });
    el('.search').addEventListener('input', gallery);
    // Stored in this browser only; Theme Manager also skips effects when the system asks for less motion.
    try { el('.effects').checked = localStorage.getItem(effectsStorageKey) !== 'off'; } catch { el('.effects').checked = true; }
    el('.effects').addEventListener('change', () => {
        try { localStorage.setItem(effectsStorageKey, el('.effects').checked ? 'on' : 'off'); } catch { /* private mode: effects follow the default */ }
        onEffects();
    });
    el('.save-cal').addEventListener('click', async () => {
        if (!valid() || !calendarEditor) return;
        el('.save-cal').disabled = true; status('Saving your calendar…');
        try {
            await request(session, 'Me/Schedule', { method: 'PUT', body: calendarEditor.value() });
            if (!valid()) return;
            await loadCalendars();
            el('.mode').value = 'Schedule:personal'; gallery();
            await request(session, 'Me', { method: 'PUT', body: choice() });
            await onSaved(); await preview();
            if (valid()) status('Your calendar is saved and in use.');
        } catch (error) { if (valid()) status(error.message); }
        finally { el('.save-cal').disabled = false; }
    });
    el('.delete-cal').addEventListener('click', async () => {
        if (!valid() || !confirm('Delete your calendar? If you use it, you go back to the server default.')) return;
        try {
            await request(session, 'Me/Schedule', { method: 'DELETE' });
            const mine = await request(session, 'Me');
            await loadCalendars();
            el('.mode').value = mine.mode === 'Schedule' ? 'Schedule:' + mine.scheduleId : mine.mode;
            await onSaved(); gallery(); await preview();
            if (valid()) status('Your calendar is deleted.');
        } catch (error) { if (valid()) status(error.message); }
    });
    el('.save').addEventListener('click', async () => {
        if (!valid()) { close(); return; }
        const captured = session;
        el('.save').disabled = true; status('Saving your theme…');
        try {
            await request(captured, 'Me', { method: 'PUT', body: choice(), signal: opening.controller?.signal });
            if (!valid()) return;
            await onSaved();
            if (valid()) status('Saved to your account. Your Jellyfin view has been updated.');
        } catch (error) { if (error.name !== 'AbortError' && valid()) status(error.message); }
        finally { if (valid()) el('.save').disabled = false; }
    });
    return {
        available(value) { host.style.display = value ? '' : 'none'; if (!value) close(); },
        reset: close,
        destroy() { close(); host.remove(); },
    };
}
