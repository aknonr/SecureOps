// Published loopback UI/API only. Usage: node workspace-usability.cjs <playwright-core> <UI> <API> <evidence>
// Uses the fixed synthetic team-lead actor and labelled local fixtures. No API proxy or corporate calls.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium, request } = require(process.argv[2]);
const { loopback, navigate, signIn, capture, apiContext, json } = require('./journey-support.cjs');
const ui = loopback(process.argv[3]), api = loopback(process.argv[4]), out = path.resolve(process.argv[5]);

(async () => {
    const admin = await apiContext(request, api), owner = await apiContext(request, api, 'team-lead');
    const browser = await chromium.launch({ executablePath: process.env.WASAS_CHROME || 'C:/Program Files/Google/Chrome/Application/chrome.exe', headless: true });
    const checks = [], errors = [], assets = [];
    let zoomBoundary = '720 CSS pixel reflow; native browser zoom not verified';
    let page;
    try {
        const categories = await json(admin, '/api/v1/resources/categories');
        let category = categories.find(c => c.name === 'Sentetik çalışma alanı doğrulaması');
        if (!category) {
            category = await json(admin, '/api/v1/resources/categories', { method: 'POST', data: { name: 'Sentetik çalışma alanı doğrulaması' } });
            for (let i = 0; i < 28; i++) await json(admin, '/api/v1/resources/links', { method: 'POST', data: {
                categoryId: category.id, name: `Çalışma alanı örneği ${String(i + 1).padStart(2, '0')}`,
                purpose: 'Sentetik yerel doğrulama: günlük operasyon ve uygulama kontrolü.',
                url: new URL(`harmless/workspace/${i}`, ui).href, environment: i % 2 ? 'TEST' : 'DEMO',
                location: 'Yerel', tags: ['sentetik', 'operasyon'], displayOrder: i
            } });
        }
        const fixture = await json(admin, '/api/v1/resources/links?search=' + encodeURIComponent('Çalışma alanı örneği 01'));
        const long = fixture.items[0];
        assert.ok(long && long.categoryId === category.id, 'Only the labelled synthetic fixture may be edited');
        await json(admin, `/api/v1/resources/links/${long.id}`, { method: 'PUT', data: {
            ...long, name: 'Çalışma alanı örneği 01 ' + 'UzunMetin'.repeat(10), purpose: 'Sentetik uzun amaç. '.repeat(15),
            notes: 'Sentetik not. '.repeat(60), expectedVersion: long.version, url: new URL('harmless/workspace/0', ui).href
        } });
        const initial = await json(owner, '/api/v1/resources/me');
        await json(owner, '/api/v1/resources/me/layout', { method: 'PUT', data: {
            expectedVersion: initial.version, layout: { view: 'cards', density: 'comfortable', pageSize: 25, shortcuts: ['links', 'groups'] }
        } });
        const context = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1440, height: 900 } });
        // Only harmless destination pages are fulfilled, never API traffic or corporate hosts.
        await context.route(new URL('harmless/**', ui).href, route => route.fulfill({ contentType: 'text/html', body: '<title>Synthetic destination</title><h1>Sentetik yerel hedef</h1>' }));
        await context.route('https://localhost:5221/harmless/**', route => route.fulfill({ contentType: 'text/html', body: '<title>Synthetic destination</title><h1>Sentetik yerel hedef</h1>' }));
        page = await context.newPage();
        page.on('pageerror', e => errors.push(e.message));
        page.on('response', r => { if (/\.css(?:\?|$)|\.js(?:\?|$)/.test(r.url())) assets.push({ url: r.url(), status: r.status() }); });
        await signIn(page, ui);
        await navigate(page, ui, 'resources');
        const rows = page.locator('.so-resource-results > .so-resource-list > .so-resource-item');
        const search = page.getByLabel('Bağlantı ara', { exact: true });
        await page.waitForFunction(() => [...(document.querySelector('[aria-label="Bağlantı ara"]')?.attributes || [])].some(a => a.name.startsWith('_bl_')));
        await search.fill('Çalışma alanı örneği');
        await page.waitForFunction(() => document.querySelector('.so-resource-results-head [role=status]')?.textContent.split(' · ')[0].trim() === '28 bağlantı');
        assert.equal(await rows.count(), 25);
        assert.match(await page.locator('link[href*="secureops-theme.css"]').getAttribute('href'), /\?v=/);
        const grid = await page.locator('.so-resource-discovery > .so-resource-filters').evaluate(e => ({ display: getComputedStyle(e).display, columns: getComputedStyle(e).gridTemplateColumns }));
        assert.equal(grid.display, 'grid');
        assert.equal(grid.columns.split(' ').length, 3);
        assert.equal(await rows.first().evaluate(e => getComputedStyle(e).borderTopWidth), '1px');
        await page.screenshot({ path: path.join(out, 'after-links-desktop-viewport.png') });
        await page.getByLabel('Bu sayfadakileri seç', { exact: true }).check();
        await page.getByText('25 bağlantı seçili', { exact: true }).waitFor();
        await page.getByRole('button', { name: 'Sonraki', exact: true }).click();
        await page.waitForFunction(() => document.querySelectorAll('.so-resource-results > ul > li').length === 3);
        assert.equal(await page.locator('.so-selection-bar').count(), 0);
        await page.getByRole('button', { name: 'Önceki', exact: true }).click();
        await page.waitForFunction(() => document.querySelectorAll('.so-resource-results > ul > li').length === 25);
        await rows.nth(0).getByRole('checkbox').check();
        await rows.nth(1).getByRole('checkbox').check();
        await page.getByText('2 bağlantı seçili', { exact: true }).waitFor();
        await page.locator('.so-selection-bar').getByRole('button', { name: 'Seçilenleri grubuma kaydet', exact: true }).click();
        const dialog = page.getByRole('dialog');
        if (await dialog.getByRole('button', { name: 'Yeni kişisel grup oluştur', exact: true }).isEnabled()) await dialog.getByRole('button', { name: 'Yeni kişisel grup oluştur', exact: true }).click();
        const groupName = 'Sentetik çalışma grubu ' + Date.now();
        await dialog.getByLabel('Grup adı', { exact: true }).fill(groupName);
        await dialog.getByRole('button', { name: 'Oluştur ve kaydet', exact: true }).click();
        await dialog.waitFor({ state: 'hidden' });
        const saved = await json(owner, '/api/v1/resources/me');
        assert.equal(saved.sets.find(s => s.name === groupName).links.length, 2);
        await page.locator('.so-selection-bar').getByRole('button', { name: 'Açmak için hazırla', exact: true }).click();
        const resolved = page.getByRole('region', { name: 'Açılmaya hazır bağlantılar', exact: true });
        await resolved.waitFor();
        assert.equal(await resolved.locator('[data-so-open-url]').count(), 2);
        const popups = [];
        context.on('page', popup => { if (popup !== page) popups.push(popup); });
        await resolved.locator('[data-so-open-links]').press('Enter');
        await resolved.locator('[data-so-open-status]').waitFor({ state: 'visible' });
        await page.waitForTimeout(500);
        for (const popup of popups) { await popup.waitForLoadState(); assert.equal(await popup.evaluate(() => window.opener === null), true); await popup.close(); }
        checks.push('Published CSS fingerprint/grid; current-page selection clears on paging; atomic two-link group addition; native keyboard opening with opener isolation and honest fallback');
        await page.getByRole('button', { name: 'Çalışma alanını düzenle', exact: true }).click();
        await dialog.getByLabel('Görünüm', { exact: true }).selectOption('list');
        await dialog.getByLabel('Yoğunluk', { exact: true }).selectOption('compact');
        await dialog.getByLabel('Sayfa boyutu', { exact: true }).selectOption('10');
        await dialog.getByRole('button', { name: 'Kişisel bağlantı gruplarım: Yukarı taşı', exact: true }).click();
        assert.equal(await dialog.getByLabel('Bağlantı Yönetimi', { exact: true }).count(), 0);
        await dialog.getByRole('button', { name: 'Kaydet', exact: true }).click();
        await dialog.waitFor({ state: 'hidden' });
        await page.waitForFunction(() => document.querySelectorAll('.so-resource-results > ul > li').length === 10);
        const personal = await json(owner, '/api/v1/resources/me');
        assert.deepEqual(personal.workspaceLayout, { view: 'list', density: 'compact', pageSize: 10, shortcuts: ['groups', 'links'] });
        await navigate(page, ui, 'resources');
        assert.equal(await page.locator('.so-workspace--list.so-workspace--compact').count(), 1);
        await search.fill('Çalışma alanı örneği');
        await page.waitForFunction(() => document.querySelector('.so-resource-results-head [role=status]')?.textContent.split(' · ')[0].trim() === '28 bağlantı');
        await capture(page, out, 'after-links-list');
        await page.getByRole('button', { name: 'Hesap menüsü', exact: true }).click();
        await page.locator('.so-user-menu-popover.mud-popover-open .mud-list-item').filter({ hasText: /^Koyu/ }).click();
        await page.waitForFunction(() => document.documentElement.classList.contains('so-dark'));
        await capture(page, out, 'after-links-dark');
        const beforeZoom = await page.evaluate(() => devicePixelRatio);
        await page.getByRole('heading', { name: 'Uygulama Bağlantıları', exact: true }).click();
        for (let i = 0; i < 4; i++) await page.keyboard.press('Control+Equal');
        await page.waitForTimeout(300);
        const afterZoom = await page.evaluate(() => devicePixelRatio);
        if (Math.abs(afterZoom / beforeZoom - 2) < 0.05) {
            assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1), false);
            await page.screenshot({ path: path.join(out, 'after-links-native-200-percent.png'), fullPage: true });
            zoomBoundary = 'Native 200% browser zoom verified by devicePixelRatio; managed TEST browser remains unverified';
        }
        await page.keyboard.press('Control+Digit0');
        await page.setViewportSize({ width: 720, height: 450 });
        await page.screenshot({ path: path.join(out, 'after-links-200-percent-reflow.png'), fullPage: true });
        assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1), false);
        await page.setViewportSize({ width: 1440, height: 900 });
        await search.fill('Eşleşmeyen sentetik kayıt');
        await page.getByText('Eşleşen bağlantı yok', { exact: true }).waitFor();
        await capture(page, out, 'after-links-no-results');
        checks.push('Server layout persistence after navigation; list/compact/10 rows and ordered shortcuts; dark/mobile and 200% equivalent 720 CSS pixel reflow; no-results state');
        await navigate(page, ui, 'resources/sets');
        await page.getByRole('button').filter({ hasText: groupName }).click();
        await page.locator('.so-group-toolbar').getByRole('button', { name: 'Açmak için hazırla', exact: true }).click();
        await page.locator('[data-so-open-links]').waitFor();
        await capture(page, out, 'after-groups');
        checks.push('Group primary opening action prepares authoritative links next to the toolbar; only a second native activation opens destinations');
        await navigate(page, ui, 'resources');
        await page.waitForFunction(() => [...(document.querySelector('[aria-label="Bağlantı ara"]')?.attributes || [])].some(a => a.name.startsWith('_bl_')));
        const beforeReset = await json(owner, '/api/v1/resources/me');
        await page.getByRole('button', { name: 'Çalışma alanını düzenle', exact: true }).click();
        await dialog.getByRole('button', { name: 'Düzeni sıfırla', exact: true }).click();
        await dialog.waitFor({ state: 'hidden' });
        const reset = await json(owner, '/api/v1/resources/me');
        assert.deepEqual(reset.workspaceLayout, { view: 'cards', density: 'comfortable', pageSize: 25, shortcuts: ['links', 'groups'] });
        assert.deepEqual(reset.sets, beforeReset.sets);
        assert.deepEqual(reset.favourites, beforeReset.favourites);
        checks.push('Explicit Reset layout saves defaults without changing groups or favourites');
        assert.deepEqual(errors, []);
        assert.deepEqual(assets.filter(a => a.status >= 400), []);
        fs.writeFileSync(path.join(out, 'workspace-results.json'), JSON.stringify({ checks, grid, errors, assets, zoomBoundary }, null, 2));
        console.log(JSON.stringify({ checks, errors, assetFailures: assets.filter(a => a.status >= 400), zoomBoundary }, null, 2));
    } finally {
        if (page && !page.isClosed()) await page.screenshot({ path: path.join(out, 'workspace-last-state.png'), fullPage: true }).catch(() => {});
        await browser.close(); await owner.dispose(); await admin.dispose();
    }
})().catch(error => { console.error(error); process.exitCode = 1; });
