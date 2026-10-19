// Editor for a custom calendar ("my own Automatic Seasonal"). Shared by the admin page and the theme picker.
// Built with DOM APIs only; theme and calendar names are assigned through textContent/value, never parsed as HTML.
const months = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'];
const kinds = [['Month', 'Whole month'], ['Range', 'Dates'], ['EasterOffset', 'Around Easter']];
const daysIn = [31, 29, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];

export const scheduleEditorCss = `
.tm-sched{display:grid;gap:12px}.tm-sched label{display:grid;gap:5px;font-size:13px}
.tm-sched select,.tm-sched input{padding:8px 10px;border-radius:8px;border:1px solid #65769577;background:#152138;color:#fff;font:inherit;min-width:0}
.tm-sched button{font:inherit;cursor:pointer;border-radius:8px;border:1px solid #9caacd66;background:transparent;color:inherit;padding:7px 12px}
.tm-sched .tm-row{display:grid;grid-template-columns:minmax(130px,1.4fr) minmax(120px,1fr) minmax(170px,2fr) 70px auto;gap:8px;align-items:end;padding:10px;border:1px solid #9cafd033;border-radius:10px}
.tm-sched .tm-rule{display:flex;gap:6px;align-items:end;flex-wrap:wrap}.tm-sched .tm-rule>*{flex:1 1 60px}
.tm-sched .tm-actions{display:flex;gap:8px;flex-wrap:wrap}.tm-sched .tm-timeline{margin:0;padding-left:18px;font-size:13px;line-height:1.7}
.tm-sched .tm-head{display:grid;grid-template-columns:2fr 1fr;gap:10px}.tm-sched .tm-note{font-size:12px;opacity:.8;margin:0}
@media(max-width:700px){.tm-sched .tm-row,.tm-sched .tm-head{grid-template-columns:1fr}}`;

const node = (tag, props = {}, ...children) => { const n = Object.assign(document.createElement(tag), props); n.append(...children); return n; };
const option = (value, text) => node('option', { value, textContent: text });
const labelled = (text, control) => node('label', {}, text, control);
const pad = n => String(n).padStart(2, '0');

/** Parses "MM-DD" into [month, day]; invalid input gives [1, 1]. */
export function splitMonthDay(value) {
    const match = /^(\d\d)-(\d\d)$/.exec(String(value || ''));
    return match ? [Number(match[1]), Number(match[2])] : [1, 1];
}
export function joinMonthDay(month, day) { return pad(month) + '-' + pad(Math.min(Math.max(1, day), daysIn[month - 1])); }

/** A permanent id from a display name: "Børnekalender" → "boernekalender". Letters without an ASCII form are dropped. */
export function calendarId(name) {
    const spelled = String(name).toLowerCase().replace(/æ/g, 'ae').replace(/ø/g, 'oe').replace(/å/g, 'aa').replace(/ß/g, 'ss');
    return spelled.normalize('NFKD').replace(/[̀-ͯ]/g, '').replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '').slice(0, 80).replace(/-$/, '');
}

/** Entries equivalent to the built-in Automatic Seasonal: every pack that carries a calendar rule. */
export function automaticEntries(themes) {
    return themes.filter(theme => theme.calendar).map(theme => ({ themeId: theme.id, rule: { ...theme.calendar } }));
}

/** Human-readable rule, for summaries and screen readers. */
export function describeRule(rule) {
    if (rule.kind === 'Month') return months[rule.month - 1] || '?';
    if (rule.kind === 'EasterOffset') return 'Easter ' + (rule.startOffset >= 0 ? '+' : '') + rule.startOffset + ' to ' + (rule.endOffset >= 0 ? '+' : '') + rule.endOffset + ' days';
    const [sm, sd] = splitMonthDay(rule.start), [em, ed] = splitMonthDay(rule.end);
    return sd + ' ' + months[sm - 1].slice(0, 3) + ' – ' + ed + ' ' + months[em - 1].slice(0, 3);
}

/**
 * @param container element to render into
 * @param options.themes catalog themes ({id, name, category, calendar})
 * @param options.schedule initial schedule or null
 * @param options.test async (schedule, fromIsoDate) => {timeline:[{from, themeId}]}
 * @param options.showId show an editable id field (shared calendars)
 */
export function createScheduleEditor(container, { themes, schedule, test, showId = false }) {
    const sorted = [...themes].sort((a, b) => a.name.localeCompare(b.name));
    const nameOf = id => id == null ? 'Original Jellyfin' : (themes.find(t => t.id === id)?.name ?? id + ' (not installed)');
    const root = node('div', { className: 'tm-sched' });
    const name = node('input', { maxLength: 60, placeholder: 'e.g. Kids calendar', required: true });
    const id = node('input', { maxLength: 80, placeholder: 'kids-calendar', pattern: '[a-z0-9]+(-[a-z0-9]+)*' });
    const fallback = node('select', {}, option('', 'Original Jellyfin'), ...sorted.map(t => option(t.id, t.name)));
    const rows = node('div', { className: 'tm-sched-rows' });
    const timeline = node('ol', { className: 'tm-timeline' });
    const from = node('input', { type: 'date' });
    let idTouched = false;

    function themeSelect(value) {
        const select = node('select', {}, ...sorted.map(t => option(t.id, t.name)));
        if (value && !themes.some(t => t.id === value)) select.prepend(option(value, value + ' (not installed)'));
        select.value = value || sorted[0]?.id || '';
        return select;
    }
    function monthDay(value, text) {
        const [m, d] = splitMonthDay(value);
        const month = node('select', {}, ...months.map((label, i) => option(String(i + 1), label.slice(0, 3))));
        const day = node('input', { type: 'number', min: 1, max: 31, value: d });
        month.value = String(m);
        return { element: node('div', { className: 'tm-rule' }, labelled(text + ' day', day), labelled('month', month)), read: () => joinMonthDay(Number(month.value), Number(day.value) || 1) };
    }
    function ruleEditor(holder, rule) {
        holder.replaceChildren();
        if (rule.kind === 'Month') {
            const month = node('select', {}, ...months.map((label, i) => option(String(i + 1), label)));
            month.value = String(rule.month || 1);
            holder.append(labelled('Month', month));
            return () => ({ kind: 'Month', month: Number(month.value) });
        }
        if (rule.kind === 'EasterOffset') {
            const start = node('input', { type: 'number', min: -90, max: 90, value: rule.startOffset ?? -3 });
            const end = node('input', { type: 'number', min: -90, max: 90, value: rule.endOffset ?? 1 });
            holder.append(node('div', { className: 'tm-rule' }, labelled('Days from Easter Sunday', start), labelled('to', end)));
            return () => ({ kind: 'EasterOffset', startOffset: Number(start.value) || 0, endOffset: Number(end.value) || 0 });
        }
        const start = monthDay(rule.start ?? '12-24', 'From'), end = monthDay(rule.end ?? rule.start ?? '12-26', 'To');
        holder.append(node('div', { className: 'tm-rule' }, start.element, end.element));
        return () => ({ kind: 'Range', start: start.read(), end: end.read() });
    }
    function addRow(entry = { themeId: sorted[0]?.id, rule: { kind: 'Range', start: '12-24', end: '12-26' } }) {
        const theme = themeSelect(entry.themeId);
        const kind = node('select', {}, ...kinds.map(([value, text]) => option(value, text)));
        kind.value = entry.rule?.kind || 'Range';
        const holder = node('div');
        let readRule = ruleEditor(holder, entry.rule || {});
        kind.addEventListener('change', () => { readRule = ruleEditor(holder, { kind: kind.value }); });
        const priority = node('input', { type: 'number', min: -1000, max: 1000, value: entry.rule?.priority ?? 0, title: 'When two dates overlap, the higher priority wins.' });
        const remove = node('button', { type: 'button', textContent: 'Remove' });
        const row = node('div', { className: 'tm-row' }, labelled('Theme', theme), labelled('When', kind), holder, labelled('Priority', priority), remove);
        row.read = () => ({ themeId: theme.value, rule: { ...readRule(), priority: Number(priority.value) || 0 } });
        remove.addEventListener('click', () => row.remove());
        rows.append(row);
    }
    function load(value) {
        name.value = value?.name || ''; id.value = value?.id && value.id !== 'personal' ? value.id : ''; idTouched = !!id.value;
        fallback.value = value?.fallbackThemeId || '';
        rows.replaceChildren(); (value?.entries || []).forEach(addRow);
        timeline.replaceChildren();
    }
    function value() {
        return { id: id.value.trim(), name: name.value.trim(), fallbackThemeId: fallback.value || null, entries: [...rows.children].map(row => row.read()) };
    }
    async function showTimeline() {
        timeline.replaceChildren(node('li', { textContent: 'Calculating…' }));
        try {
            const result = await test(value(), from.value || null);
            timeline.replaceChildren(...result.timeline.map(change => {
                const date = new Date(change.from + 'T12:00:00');
                return node('li', { textContent: date.toLocaleDateString(undefined, { day: 'numeric', month: 'short', year: 'numeric' }) + ' → ' + nameOf(change.themeId) });
            }));
        } catch (error) { timeline.replaceChildren(node('li', { textContent: error.message })); }
    }
    // A readable id follows the name until the administrator edits it.
    name.addEventListener('input', () => { if (showId && !idTouched) id.value = calendarId(name.value); });
    id.addEventListener('input', () => { idTouched = true; });

    const add = node('button', { type: 'button', textContent: '+ Add date' });
    const copy = node('button', { type: 'button', textContent: 'Start from Automatic Seasonal' });
    const preview = node('button', { type: 'button', textContent: 'Show the year' });
    add.addEventListener('click', () => addRow());
    copy.addEventListener('click', () => { rows.replaceChildren(); automaticEntries(themes).forEach(addRow); });
    preview.addEventListener('click', () => { void showTimeline(); });

    root.append(
        node('div', { className: 'tm-head' }, labelled('Calendar name', name), showId ? labelled('Id (permanent)', id) : node('span')),
        node('p', { className: 'tm-note', textContent: 'The date with the highest priority wins when dates overlap. Dates repeat every year; "Around Easter" follows Easter Sunday each year.' }),
        rows,
        node('div', { className: 'tm-actions' }, add, copy),
        labelled('Other days', fallback),
        node('div', { className: 'tm-actions' }, labelled('Show from', from), preview),
        timeline);
    container.replaceChildren(root);
    load(schedule);
    return { value, load, lockId(locked) { id.disabled = locked; } };
}
