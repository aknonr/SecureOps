// #67 Access screens (Aktif Oturumlar, Kullanıcılar, Erişim Talepleri, Kullanıcı detayı) at 1280, 640 (= 200 %
// of a 1280 px window) and 390 px, light and dark, by keyboard. Proves: no horizontal scroll, the revoke action stays
// reachable, keyboard focus is not lost after a page change, tabs follow the arrow-key pattern, and loading / empty /
// error stay distinct.
// The paged user and request endpoints exist only on the SQL repository (503 on InMemory), so a test-only loopback
// proxy answers the list endpoints with SYNTHETIC data (demo:* identities, *.contoso.example addresses) and forwards
// everything else. It adds no product route or setting and contacts nothing outside loopback.
// Start the API (Demo, Access__DemoCompatibilityEnabled=true, Access__RepositoryProvider=InMemory) on <API URL>, then the
// UI with IdentityLookupApi__BaseAddress=http://localhost:<proxy port>/ .
// SECUREOPS_BROWSER_CHANNEL: browser channel for Playwright (default msedge); set it empty to use Playwright's own Chromium.
// node access-screens-responsive.cjs <playwright path> <UI URL> <API URL> <proxy port> <evidence directory>
const assert = require('node:assert/strict');
const crypto = require('node:crypto');
const fs = require('node:fs');
const http = require('node:http');
const path = require('node:path');
const { loopback, signIn } = require('./journey-support.cjs');
const { chromium } = require(process.argv[2]);
const ui = loopback(process.argv[3]), api = loopback(process.argv[4]);
const proxyPort = Number(process.argv[5]);
const out = process.argv[6];
const channel = process.env.SECUREOPS_BROWSER_CHANNEL ?? 'msedge';

// ---- synthetic list proxy -------------------------------------------------------------------------------
const state = { mode: 'data', delayMs: 0 };
const guid = key => { const h = crypto.createHash('md5').update('so-' + key).digest('hex'); return `${h.slice(0, 8)}-${h.slice(8, 12)}-${h.slice(12, 16)}-${h.slice(16, 20)}-${h.slice(20, 32)}`; };
const ago = minutes => new Date(Date.now() - minutes * 60000).toISOString();
const slice = (total, page, size) => Math.max(0, Math.min(size, total - (page - 1) * size));
const profile = i => ({ displayName: i === 4 ? 'Örnek Çok Uzun Görünen Ad Soyad Demo Kullanıcısı' : `Demo Kullanıcı ${String(i + 1).padStart(2, '0')}`, account: `demo.user${i + 1}`, email: i === 4 ? 'demo.cok.uzun.eposta.adresi.sentetik.kullanici@contoso.example' : `demo.user${i + 1}@contoso.example`, department: 'Demo', title: 'Operatör', uid: null });
function sessions(page, size) {
    const start = (page - 1) * size, items = [];
    for (let i = start; i < start + slice(2 * size + 5, page, size); i++) items.push({ sessionId: guid('s' + i), userId: guid('u' + i), startedAtUtc: ago(300 - i), lastSeenAtUtc: ago(5 + i), absoluteExpiresAtUtc: new Date(Date.now() + (700 - i) * 60000).toISOString(), authenticationMethod: 'SecureOpsDemo', accessVersion: 2, principal: `demo:user${i}`, normalizedPrincipal: `demo:user${i}`, displayName: i === 3 ? 'Örnek Çok Uzun Görünen Ad Soyad Demo Kullanıcısı Üçüncü Satır' : `Demo Kullanıcı ${i + 1}`, authenticationProvider: 'demo-api-bridge', isCurrent: i === 0, uid: i % 3 === 0 ? String(100000 + i) : null });
    return { page, pageSize: size, items };
}
function users(page, size) {
    const start = (page - 1) * size, items = [], statuses = ['Approved', 'Pending', 'Rejected', 'Disabled'];
    for (let i = start; i < start + slice(60, page, size); i++) items.push({ userId: guid('au' + i), corporateIdentity: `demo:user${i + 1}`, profile: profile(i), accessStatus: statuses[i % 4], roles: i % 4 === 0 ? ['Admin'] : [], capabilities: [], latestRequest: null, requestHistory: [], version: 1, authenticationSource: 'SecureOpsDemo', firstAuthenticatedAt: ago(9000 + i), lastAuthenticatedAt: ago(30 + i * 7), disabledAt: null });
    return { items, total: 60, page, pageSize: size };
}
function requests(page, size, status) {
    const start = (page - 1) * size, items = [];
    for (let i = start; i < start + slice(55, page, size); i++) items.push({ id: guid('r' + i), userId: guid('ru' + i), corporateIdentity: `demo:requester${i + 1}`, status: status || 'Pending', requestedAt: ago(60 * 24 * (i / 3)), decidedAt: !status || status === 'Pending' ? null : ago(60 * i), decisionReason: null, decidedByCorporateIdentity: null, version: 1, profile: profile(i) });
    return { items, total: 55, page, pageSize: size };
}
const proxy = http.createServer((req, res) => {
    const url = new URL(req.url, 'http://x'), route = url.pathname.replace(/\/$/, ''), q = url.searchParams;
    const list = ['/api/v1/sessions/active', '/api/v1/access/users/page', '/api/v1/access/requests/page'].includes(route);
    const send = (code, body) => { const go = () => { res.writeHead(code, { 'content-type': code >= 400 ? 'application/problem+json' : 'application/json' }); res.end(JSON.stringify(body)); }; state.delayMs ? setTimeout(go, state.delayMs) : go(); };
    if (list && req.method === 'GET') {
        const page = Number(q.get('page') || 1), size = Number(q.get('pageSize') || 25);
        if (state.mode === 'error') return send(503, { type: 'about:blank', title: 'Service Unavailable', status: 503 });
        if (state.mode === 'empty') return send(200, route.includes('sessions') ? { page, pageSize: size, items: [] } : { items: [], total: 0, page, pageSize: size });
        return send(200, route.includes('sessions') ? sessions(page, size) : route.includes('users') ? users(page, size) : requests(page, size, q.get('status')));
    }
    const upstream = http.request({ host: api.hostname, port: api.port, path: req.url, method: req.method, headers: { ...req.headers, host: api.host } }, r => { res.writeHead(r.statusCode, r.headers); r.pipe(res); });
    upstream.on('error', () => { res.writeHead(502); res.end(); });
    req.pipe(upstream);
});

// ---- journey ---------------------------------------------------------------------------------------------
const SCREENS = [
    { key: 'sessions', route: 'admin/sessions', next: 'button:has-text("Sonraki")', landing: 'h2' },
    { key: 'users', route: 'access/users', next: 'button[aria-label="Sonraki sayfa"]', landing: '.so-access-count' },
    { key: 'requests', route: 'access/requests', next: 'button[aria-label="Sonraki sayfa"]', landing: '#request-list-title' },
];
const probe = () => {
    const visible = element => { const box = element.getBoundingClientRect(); return box.width > 0 && box.height > 0; };
    const revoke = [...document.querySelectorAll('button')].filter(button => /sonlandır/i.test(button.textContent) && visible(button));
    return {
        overflow: document.documentElement.scrollWidth > innerWidth + 1,
        scrollers: [...document.querySelectorAll('.so-page *')].filter(element => visible(element) && ['auto', 'scroll'].includes(getComputedStyle(element).overflowX) && element.scrollWidth > element.clientWidth + 1).map(element => String(element.className).split(' ')[0]),
        revoke: revoke.length, revokeOffscreen: revoke.filter(button => button.getBoundingClientRect().right > innerWidth).length,
    };
};

(async () => {
    const observations = [];
    const note = text => { observations.push(text); console.log(text); };
    fs.mkdirSync(out, { recursive: true });
    await new Promise(resolve => proxy.listen(proxyPort, '127.0.0.1', resolve));
    const browser = await chromium.launch({ ...(channel ? { channel } : {}), headless: true });
    const context = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1280, height: 900 }, reducedMotion: 'reduce' });
    const page = await context.newPage();
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    try {
        await signIn(page, ui);
        const open = async (screen, size = [1280, 900], mode = 'data', delayMs = 0) => {
            state.mode = mode; state.delayMs = delayMs;
            await page.setViewportSize({ width: size[0], height: size[1] });
            await page.goto(new URL(screen.route, ui).href);
            if (!delayMs) { await page.locator('.so-loading').last().waitFor({ state: 'hidden', timeout: 15000 }).catch(() => { }); await page.waitForTimeout(1200); }
        };
        const theme = async name => {
            await page.setViewportSize({ width: 1280, height: 900 });
            await page.getByRole('button', { name: 'Hesap menüsü', exact: true }).click();
            await page.locator('.so-user-menu-popover .mud-list-item').filter({ hasText: name }).click();
            await page.keyboard.press('Escape');
        };

        // 1. No horizontal scroll, revoke reachable: 1280 / 640 (200 %) / 390, light then dark.
        for (const mode of ['Aydınlık', 'Koyu']) {
            await open(SCREENS[0]); await theme(mode);
            for (const size of mode === 'Aydınlık' ? [[1280, 900], [640, 450], [390, 844]] : [[390, 844]]) {
                for (const screen of SCREENS) {
                    await open(screen, size);
                    const m = await page.evaluate(probe);
                    assert.equal(m.overflow, false, `${screen.key} @${size[0]} ${mode}: page overflows`);
                    assert.deepEqual(m.scrollers, [], `${screen.key} @${size[0]} ${mode}: inner horizontal scroller ${m.scrollers}`);
                    if (screen.key === 'sessions') { assert.ok(m.revoke > 0, 'revoke buttons rendered'); assert.equal(m.revokeOffscreen, 0, `sessions @${size[0]} ${mode}: revoke action outside the viewport`); }
                    if (size[0] === 390 && mode === 'Aydınlık') await page.screenshot({ path: path.join(out, `${screen.key}-390.png`), animations: 'disabled' });
                    note(`${screen.key} @${size[0]} ${mode}: no overflow, no inner scroller${screen.key === 'sessions' ? `, ${m.revoke} revoke actions in view` : ''}`);
                }
            }
        }
        await open(SCREENS[0]); await theme('Aydınlık');

        // 2. A role chip is not hidden on narrow screens (a bare span:last-child once hid it).
        await open(SCREENS[1], [390, 844]);
        assert.match(await page.locator('.so-access-table-row').first().innerText(), /Sistem Yöneticisi|Admin/, 'role chip hidden at 390');
        assert.match(await page.locator('.so-access-table-row').first().innerText(), /\d{2}\.\d{2}\.\d{4}/, 'last sign-in hidden at 390');
        note('users @390: role and last sign-in stay visible');

        // 3. Keyboard: Enter on "Sonraki" moves focus to the list landing element, never to <body>.
        for (const screen of SCREENS) {
            await open(screen);
            if (screen.key === 'sessions') { await page.locator('.so-page-size').first().click(); await page.locator('.mud-popover-open .mud-list-item').filter({ hasText: /^\s*25\s*$/ }).first().click(); await page.waitForTimeout(1200); }
            await page.locator(screen.next).first().focus();
            await page.keyboard.press('Enter');
            await page.waitForTimeout(1800);
            const landed = await page.evaluate(landing => { const active = document.activeElement; return { body: active === document.body, onLanding: !!active && active.matches(landing), status: document.querySelector('[role=status]')?.textContent ?? document.querySelector('.so-access-count')?.textContent }; }, screen.landing);
            assert.equal(landed.body, false, `${screen.key}: focus dropped to body after the page change`);
            assert.equal(landed.onLanding, true, `${screen.key}: focus is not on ${screen.landing}`);
            assert.match(landed.status ?? '', /(Sayfa|eşleşen)/, `${screen.key}: page change is not announced`);
            note(`${screen.key}: after Enter on Sonraki focus is on ${screen.landing}; announced "${(landed.status ?? '').slice(0, 40)}"`);
        }

        // 4. Request tabs: one tab stop, arrow keys move and select, focus follows.
        await open(SCREENS[2]);
        const tabs = page.locator('button[role=tab]');
        assert.equal(await tabs.evaluateAll(buttons => buttons.filter(button => button.tabIndex === 0).length), 1, 'exactly one tab stop');
        await tabs.first().focus();
        await page.keyboard.press('ArrowRight');
        await page.waitForTimeout(1500);
        assert.match(await page.evaluate(() => document.activeElement.getAttribute('role') + ':' + document.activeElement.textContent.trim()), /^tab:Onaylanan/, 'ArrowRight did not move focus to the next tab');
        assert.equal(await page.evaluate(() => document.activeElement.getAttribute('aria-selected')), 'true', 'focused tab is not selected');
        note('requests: ArrowRight moves focus and selection to the next tab');

        // 4b. A visible focus ring, measured rather than assumed: every MudBlazor button, tab, row and link in the page that the
        // keyboard reaches draws a solid outline of at least 2 px (MudBlazor 6 alone shows only a faint tint).
        for (const screen of SCREENS) {
            await open(screen);
            await page.evaluate(() => { document.activeElement?.blur(); window.scrollTo(0, 0); });
            const reached = [];
            for (let stop = 0; stop < 30 && reached.length < 8; stop++) {
                await page.keyboard.press('Tab');
                await page.waitForTimeout(400); // the global button rule transitions `all`, so the outline grows from 0 px; read it settled
                const focused = await page.evaluate(() => {
                    const element = document.activeElement;
                    if (!element || element === document.body || !element.closest('.so-page') || !element.matches('.mud-button-root, .so-tab, .so-request-row, a, summary')) return null;
                    const style = getComputedStyle(element);
                    return { name: (element.getAttribute('aria-label') || element.textContent || element.tagName).trim().replace(/\s+/g, ' ').slice(0, 28), style: style.outlineStyle, width: parseFloat(style.outlineWidth) };
                });
                if (focused) reached.push(focused);
            }
            assert.ok(reached.length >= 3, `${screen.key}: the keyboard reached only ${reached.length} controls`);
            const weak = reached.filter(focused => focused.style === 'none' || focused.width < 2);
            assert.deepEqual(weak, [], `${screen.key}: focus ring under 2 px or missing on ${JSON.stringify(weak)}`);
            note(`${screen.key}: ${reached.length} keyboard-reached controls each draw an outline of at least 2 px (${reached.map(focused => `${focused.name}=${focused.width}px`).slice(0, 3).join(', ')} ...)`);
        }

        // 5. Distinct loading / empty / error at 390.
        for (const screen of SCREENS) {
            await open(screen, [390, 844], 'empty');
            assert.ok(await page.locator('.so-empty').count() > 0, `${screen.key}: empty state missing`);
            assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1), false, `${screen.key}: empty overflows`);
            await open(screen, [390, 844], 'error');
            assert.match(await page.locator('.so-page').innerText(), /Servis şu anda yanıt veremiyor/, `${screen.key}: error panel missing`);
            assert.equal(await page.locator('.so-empty').filter({ hasText: /yok/ }).count(), 0, `${screen.key}: error shown as empty`);
            await open(screen, [390, 844], 'data', 2500);
            await page.waitForTimeout(500);
            assert.ok(await page.locator('.so-loading, [role=progressbar]').count() > 0, `${screen.key}: loading state missing`);
            note(`${screen.key}: empty, error and loading are distinct at 390`);
        }

        assert.deepEqual(errors, [], 'page errors');
        fs.writeFileSync(path.join(out, 'access-screens-responsive.txt'), observations.join('\n') + '\n');
        console.log('access-screens-responsive: PASS');
    } finally {
        await browser.close();
        proxy.close();
    }
})().catch(error => { console.error('FAIL', error.message); process.exit(1); });
