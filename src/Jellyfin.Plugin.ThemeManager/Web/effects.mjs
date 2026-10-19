// Seasonal effects drawn on one transparent canvas above the page. It never takes clicks, pauses while the tab is
// hidden and draws few particles, so Jellyfin stays responsive.
const settings = {
    snow: { count: 60, colors: ['#ffffff'], fall: [0.4, 1.1], size: [1.5, 3.5] },
    leaves: { count: 22, colors: ['#e07a2f', '#c4521d', '#f0a33a', '#8f3b1b'], fall: [0.6, 1.3], size: [6, 10] },
    hearts: { count: 18, colors: ['#ff5d8f', '#ff8fb1', '#e63973'], fall: [-0.9, -0.4], size: [6, 10] },
    confetti: { count: 50, colors: ['#ffd166', '#06d6a0', '#118ab2', '#ef476f', '#ffffff'], fall: [0.8, 1.6], size: [3, 6] },
};
const random = (min, max) => min + Math.random() * (max - min);

export function createEffects() {
    let canvas = null, context = null, kind = null, particles = [], frame = 0, last = 0;
    function spawn(initial) {
        const s = settings[kind];
        const rising = s.fall[0] < 0;
        return {
            x: random(0, innerWidth), y: initial ? random(0, innerHeight) : (rising ? innerHeight + 10 : -10),
            speed: random(...s.fall), drift: random(-0.3, 0.3), size: random(...s.size),
            color: s.colors[Math.floor(Math.random() * s.colors.length)], angle: random(0, Math.PI * 2), spin: random(-0.03, 0.03),
        };
    }
    function resize() { if (!canvas) return; canvas.width = innerWidth * devicePixelRatio; canvas.height = innerHeight * devicePixelRatio; context.setTransform(devicePixelRatio, 0, 0, devicePixelRatio, 0, 0); }
    function draw(p) {
        context.fillStyle = p.color;
        context.save(); context.translate(p.x, p.y); context.rotate(p.angle);
        if (kind === 'snow') { context.globalAlpha = 0.8; context.beginPath(); context.arc(0, 0, p.size, 0, Math.PI * 2); context.fill(); }
        else if (kind === 'leaves') { context.globalAlpha = 0.85; context.beginPath(); context.ellipse(0, 0, p.size, p.size / 2.2, 0, 0, Math.PI * 2); context.fill(); }
        else if (kind === 'hearts') {
            context.globalAlpha = 0.75; const r = p.size / 2;
            context.beginPath(); context.moveTo(0, r); context.bezierCurveTo(-p.size, -r / 2, -r, -p.size, 0, -r / 2); context.bezierCurveTo(r, -p.size, p.size, -r / 2, 0, r); context.fill();
        } else { context.globalAlpha = 0.9; context.fillRect(-p.size / 2, -p.size / 4, p.size, p.size / 2); }
        context.restore();
    }
    function tick(time) {
        frame = 0;
        if (!canvas || !kind) return;
        if (document.hidden) { last = 0; return; }
        const step = last ? Math.min((time - last) / 16.7, 3) : 1; last = time;
        context.clearRect(0, 0, innerWidth, innerHeight);
        for (let i = 0; i < particles.length; i++) {
            const p = particles[i];
            p.y += p.speed * step; p.x += (p.drift + Math.sin((time / 1000) + i) * 0.3) * step; p.angle += p.spin * step;
            if (p.y > innerHeight + 20 || p.y < -20 || p.x < -20 || p.x > innerWidth + 20) particles[i] = spawn(false);
            draw(p);
        }
        frame = requestAnimationFrame(tick);
    }
    function wake() { if (canvas && kind && !document.hidden && !frame) frame = requestAnimationFrame(tick); }
    return {
        /** Shows the given effect (snow, leaves, hearts or confetti), or removes everything for null. */
        set(next) {
            if (next === kind) return;
            kind = next && settings[next] ? next : null;
            if (!kind) { this.destroy(); return; }
            if (!canvas) {
                canvas = document.createElement('canvas');
                canvas.dataset.themeManager = 'effects';
                canvas.setAttribute('aria-hidden', 'true');
                canvas.style.cssText = 'position:fixed;inset:0;width:100vw;height:100vh;pointer-events:none;z-index:9000';
                document.body.append(canvas);
                context = canvas.getContext('2d');
                addEventListener('resize', resize); document.addEventListener('visibilitychange', wake);
                resize();
            }
            particles = Array.from({ length: settings[kind].count }, () => spawn(true));
            wake();
        },
        destroy() {
            kind = null; particles = [];
            if (frame) cancelAnimationFrame(frame); frame = 0; last = 0;
            removeEventListener('resize', resize); document.removeEventListener('visibilitychange', wake);
            canvas?.remove(); canvas = null; context = null;
        },
    };
}
