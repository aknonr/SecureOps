// Published loopback hosts only: node resource-opening.cjs <playwright-core> <UI> <API> <evidence> <before|after>
// Remove Playwright's popup bypass in BOTH modes. Allow only this site in a disposable Chrome profile.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium, request } = require(process.argv[2]);
const { loopback, navigate, signIn, capture, apiContext, json } = require('./journey-support.cjs');
const ui = loopback(process.argv[3]), api = loopback(process.argv[4]), out = path.resolve(process.argv[5]);
const phase = process.argv[6];
assert.ok(['before', 'after'].includes(phase));
fs.mkdirSync(out, { recursive: true });

(async () => {
    const admin = await apiContext(request, api), owner = await apiContext(request, api, 'team-lead');
    const results = [], errors = [];
    try {
        const category = await json(admin, '/api/v1/resources/categories', { method: 'POST', data: { name: `Synthetic opening ${phase} ${Date.now()}` } });
        const links = [];
        for (const [index, name] of ['IST PROD', 'AYT HOT DR', 'IST DEV'].entries()) links.push(await json(admin, '/api/v1/resources/links', { method: 'POST', data: {
            categoryId: category.id, name: `Synthetic ${name}`, purpose: 'Local opening verification only',
            url: new URL(`harmless/opening/${index}`, ui).href, displayOrder: index
        } }));
        const resolved = await json(owner, '/api/v1/resources/links/resolve', { method: 'POST', data: { linkIds: links.map(l => l.id) } });
        assert.deepEqual(resolved.map(l => [l.id, l.url]), links.map(l => [l.id, l.url]));
        const started = Date.now();
        for (const policy of ['default', 'allow']) {
            const profile = path.join(out, `${phase}-${policy}-${Date.now()}`);
            fs.mkdirSync(path.join(profile, 'Default'), { recursive: true });
            fs.writeFileSync(path.join(profile, 'Default', 'Preferences'), JSON.stringify({ profile: { content_settings: { exceptions: { popups:
                policy === 'allow' ? { [new URL(ui).origin + ',*']: { setting: 1 } } : {}
            } } } }));
            const context = await chromium.launchPersistentContext(profile, {
                executablePath: process.env.WASAS_CHROME || 'C:/Program Files/Google/Chrome/Application/chrome.exe',
                headless: true, ignoreDefaultArgs: ['--disable-popup-blocking'], ignoreHTTPSErrors: true,
                viewport: { width: 1440, height: 900 }
            });
            try {
                await context.route('**/*', route => {
                    const url = new URL(route.request().url());
                    if (!['localhost', '127.0.0.1'].includes(url.hostname)) return route.abort();
                    if (url.pathname.startsWith('/harmless/')) return route.fulfill({ contentType: 'text/html', body: '<title>Local target</title><h1>Synthetic local target</h1>' });
                    return route.continue();
                });
                const page = await context.newPage();
                page.on('pageerror', error => errors.push(error.message));
                await signIn(page, ui);
                await navigate(page, ui, 'resources');
                await page.waitForFunction(() => [...(document.querySelector('[aria-label="Bağlantı ara"]')?.attributes || [])].some(a => a.name.startsWith('_bl_')));
                await page.locator('.so-resource-filters select').first().selectOption(category.id);
                const rows = page.locator('.so-resource-results > ul > li');
                await page.waitForFunction(() => document.querySelectorAll('.so-resource-results > ul > li').length === 3);
                await page.getByLabel('Bu sayfadakileri seç', { exact: true }).check();
                await page.getByText('3 bağlantı seçili', { exact: true }).waitFor();
                await page.locator('.so-selection-bar').getByRole('button', { name: 'Açmak için hazırla', exact: true }).click();
                const panel = page.locator('.so-set-resolved');
                await panel.waitFor();
                assert.deepEqual(await panel.locator('[data-so-open-url]').evaluateAll(items => items.map(a => a.href)), links.map(l => l.url));
                await capture(page, out, `${phase}-${policy}-prepared`);
                await page.setViewportSize({ width: 1440, height: 900 });
                const popups = [];
                context.on('page', popup => { if (popup !== page) popups.push(popup); });
                await panel.locator('[data-so-open-links]').press('Enter');
                await panel.locator('[data-so-open-status]').waitFor({ state: 'visible' });
                // A bounded rendering barrier, not mocked window.open counts or a target-load claim by the UI.
                await page.evaluate(() => new Promise(resolve => { let frames = 0; function tick() { if (++frames === 30) resolve(); else requestAnimationFrame(tick); } tick(); }));
                for (const popup of popups) { await popup.waitForLoadState(); assert.equal(await popup.evaluate(() => window.opener === null), true); }
                const urls = popups.map(p => p.url());
                assert.equal(urls.length, policy === 'allow' ? 3 : 1);
                assert.deepEqual(urls, links.slice(0, urls.length).map(l => l.url));
                results.push({ phase, policy, urls, browser: context.browser()?.version(), popupBypassRemoved: true });
                for (const popup of popups.splice(0)) await popup.close();
                const single = context.waitForEvent('page');
                await panel.locator('[data-so-open-url]').nth(1).click();
                const fallback = await single;
                await fallback.waitForLoadState();
                assert.equal(fallback.url(), links[1].url);
                assert.equal(await fallback.evaluate(() => window.opener === null), true);
                await fallback.close();
                if (phase === 'after' && policy === 'allow') {
                    await page.getByRole('button', { name: 'Seçimi değiştir', exact: true }).click();
                    await rows.first().getByRole('checkbox').uncheck();
                    await panel.waitFor({ state: 'hidden' });
                    await page.getByLabel('Bu sayfadakileri seç', { exact: true }).check();
                    await personalGroup(page, owner, admin, links, out);
                }
            } finally {
                const last = context.pages().find(p => p.url().includes('/resources'));
                if (last) await last.screenshot({ path: path.join(out, `${phase}-${policy}-last.png`), fullPage: true }).catch(() => {});
                await context.close();
            }
        }
        assert.deepEqual(errors, []);
        const evidence = { results, errors, elapsedMs: Date.now() - started,
            checks: phase === 'after' ? ['Native three-target opening and single fallback; no opener',
                'Selection invalidation; non-admin create and existing-group save; 409 reread preserves draft',
                'Cross-user direct API denial; reorder/reopen; partial resolution retains hidden membership',
                'Tour keyboard/focus with unchanged preferences and page count; desktop/mobile light/dark'] : ['Baseline exact selected-ID/URL mapping and actual popup-policy reproduction'] };
        fs.writeFileSync(path.join(out, `${phase}-opening-results.json`), JSON.stringify(evidence, null, 2));
        console.log(JSON.stringify(evidence, null, 2));
    } finally { await owner.dispose(); await admin.dispose(); }
})().catch(error => { console.error(error); process.exitCode = 1; });

async function personalGroup(page, owner, admin, links, out) {
    await page.locator('.so-selection-bar').getByRole('button', { name: 'Seçilenleri grubuma kaydet', exact: true }).click();
    const dialog = page.getByRole('dialog');
    await dialog.getByRole('button', { name: 'Yeni kişisel grup oluştur', exact: true }).click();
    await dialog.getByRole('button', { name: 'Oluştur ve kaydet', exact: true }).click();
    await dialog.getByText('Grup adı zorunludur.', { exact: true }).waitFor();
    const name = 'Synthetic personal ' + Date.now();
    await dialog.getByLabel('Grup adı', { exact: true }).fill(name);
    const stale = await json(owner, '/api/v1/resources/me');
    await json(owner, `/api/v1/resources/me/favourites/${links[0].id}`, { method: 'PUT', data: { favourite: true, expectedVersion: stale.version } });
    await dialog.getByRole('button', { name: 'Oluştur ve kaydet', exact: true }).click();
    await dialog.getByRole('button', { name: 'Güncel grupları getir', exact: true }).waitFor();
    await capture(page, out, 'after-group-conflict');
    await dialog.getByRole('button', { name: 'Güncel grupları getir', exact: true }).click();
    await dialog.getByText('Güncel gruplar getirildi. Seçiminizi kontrol edip yeniden kaydedin.', { exact: true }).waitFor();
    assert.equal(await dialog.getByLabel('Grup adı', { exact: true }).inputValue(), name);
    await capture(page, out, 'after-group-refreshed');
    await dialog.getByRole('button', { name: 'Oluştur ve kaydet', exact: true }).click();
    await dialog.waitFor({ state: 'hidden' });
    const saved = await json(owner, '/api/v1/resources/me');
    const group = saved.sets.find(s => s.name === name);
    assert.deepEqual(group.links.map(l => l.id), links.map(l => l.id));
    assert.equal((await admin.get(`/api/v1/resources/me/sets/${group.id}/resolve`)).status(), 404);
    assert.equal((await admin.put(`/api/v1/resources/me/sets/${group.id}`, { data: { name: 'Denied', linkIds: [], expectedVersion: saved.version } })).status(), 404);
    assert.equal((await owner.post('/api/v1/resources/categories', { data: { name: 'Denied' } })).status(), 403);
    const empty = await json(owner, '/api/v1/resources/me/sets', { method: 'POST', data: { name: name + ' existing', linkIds: [], expectedVersion: saved.version } });
    await page.getByRole('button', { name: 'Yenile', exact: true }).click();
    await page.getByLabel('Bu sayfadakileri seç', { exact: true }).check();
    await page.locator('.so-selection-bar').getByRole('button', { name: 'Seçilenleri grubuma kaydet', exact: true }).click();
    await dialog.locator('.so-group-choices button').filter({ hasText: name + ' existing' }).click();
    await dialog.getByRole('button', { name: 'Grubuma kaydet', exact: true }).click();
    await dialog.waitFor({ state: 'hidden' });
    const added = await json(owner, '/api/v1/resources/me');
    assert.deepEqual(added.sets.find(s => s.name === name + ' existing').links.map(l => l.id), links.map(l => l.id));
    assert.ok(added.version > empty.version);
    await page.setViewportSize({ width: 390, height: 844 });
    await navigate(page, ui, 'resources/sets');
    await page.waitForFunction(() => document.querySelector('.so-drawer')?.getBoundingClientRect().right <= 1);
    await page.locator('.so-group-choices button').filter({ has: page.getByText(name, { exact: true }) }).click();
    await page.getByRole('button', { name: `${links[1].name}: Yukarı taşı`, exact: true }).click();
    await page.waitForFunction(name => document.querySelector('.so-set-list li strong')?.textContent === name, links[1].name);
    const reordered = await json(owner, '/api/v1/resources/me');
    assert.deepEqual(reordered.sets.find(s => s.id === group.id).links.map(l => l.id), [links[1].id, links[0].id, links[2].id]);
    await page.locator('.so-group-toolbar').getByRole('button', { name: 'Açmak için hazırla', exact: true }).click();
    await page.locator('[data-so-open-links]').waitFor();
    await capture(page, out, 'after-personal-group');
    const opened = [];
    const onPage = popup => opened.push(popup);
    page.context().on('page', onPage);
    await page.locator('[data-so-open-links]').press('Enter');
    await page.waitForFunction(() => !document.querySelector('[data-so-open-status]').hidden);
    await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))));
    assert.equal(opened.length, 3);
    for (const popup of opened) await popup.waitForLoadState();
    assert.deepEqual(opened.map(p => p.url()), [links[1].url, links[0].url, links[2].url]);
    for (const popup of opened) await popup.close();
    page.context().off('page', onPage);
    // A real catalogue change leaves the stored group intact, but explicit resolution omits it.
    const changed = await json(admin, `/api/v1/resources/links/${links[2].id}`, { method: 'PUT', data: { ...links[2], archived: true, expectedVersion: links[2].version } });
    await page.getByRole('button', { name: 'Bağlantıları yeniden denetle', exact: true }).click();
    await page.waitForFunction(() => document.querySelectorAll('[data-so-open-url]').length === 2);
    await json(admin, `/api/v1/resources/links/${links[2].id}`, { method: 'PUT', data: { ...changed, archived: false, expectedVersion: changed.version } });
    const restored = await json(owner, `/api/v1/resources/me/sets/${group.id}/resolve`);
    assert.equal(restored.links.length, 3);
    await navigate(page, ui, 'resources');
    await page.waitForFunction(() => [...(document.querySelector('[aria-label="Bağlantı ara"]')?.attributes || [])].some(a => a.name.startsWith('_bl_')));
    const beforeTour = await json(owner, '/api/v1/resources/me');
    const pages = page.context().pages().length;
    await page.getByRole('button', { name: 'Nasıl kullanılır?', exact: true }).click();
    const guide = page.getByRole('dialog', { name: 'Kullanım Rehberi', exact: true });
    await guide.getByRole('heading', { name: 'Bağlantıları seçin', exact: true }).waitFor();
    await page.keyboard.press('ArrowRight');
    await guide.getByRole('heading', { name: 'Bağlantıları açın', exact: true }).waitFor();
    await page.keyboard.press('ArrowLeft');
    await guide.getByRole('heading', { name: 'Bağlantıları seçin', exact: true }).waitFor();
    await page.keyboard.press('Escape');
    await guide.waitFor({ state: 'hidden' });
    await page.waitForFunction(() => document.activeElement?.id === 'resource-guide-replay-links');
    assert.equal(page.context().pages().length, pages);
    assert.deepEqual(await json(owner, '/api/v1/resources/me'), beforeTour);
    await page.getByRole('button', { name: 'Hesap menüsü', exact: true }).click();
    await page.locator('.so-user-menu-popover.mud-popover-open .mud-list-item').filter({ hasText: /^Koyu/ }).click();
    await page.waitForFunction(() => document.documentElement.classList.contains('so-dark'));
    await capture(page, out, 'after-links-dark');
}
