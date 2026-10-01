// Loopback hosts with synthetic InMemory data only:
//   node resource-shift-journey.cjs <playwright-core> <UI URL> <API URL> <evidence dir>
// Operator journey: find a link, favourite it, save it and two more to a new personal group, follow the
// confirmation to that group, see which saved links will and will not open, then open with one click.
// Destinations are intercepted harmless pages under the UI origin; no corporate site is contacted.
// Playwright's default popup handling is used, so the popup count is NOT evidence of browser policy.
// WASAS_CHROME optionally selects an installed browser on Windows; unset retains bundled Chromium.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium, request } = require(process.argv[2]);
const { loopback, navigate, signIn, capture, apiContext, json } = require('./journey-support.cjs');
const ui = loopback(process.argv[3]), api = loopback(process.argv[4]), out = path.resolve(process.argv[5]);
fs.mkdirSync(out, { recursive: true });

async function noOverflow(page, label) {
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1), false, label + ': horizontal overflow');
}

(async () => {
    const admin = await apiContext(request, api);
    const label = `Vardiya ${Date.now().toString(36)}`;
    const results = {};
    let browser, page;
    const links = [];
    let groupId;
    try {
        const category = await json(admin, '/api/v1/resources/categories', { method: 'POST', data: { name: label } });
        for (let i = 0; i < 30; i++) links.push(await json(admin, '/api/v1/resources/links', { method: 'POST', data: {
            categoryId: category.id, name: `${label} araç ${String(i + 1).padStart(2, '0')}`, purpose: 'Synthetic shift journey link',
            url: new URL(`harmless/shift/${i}`, ui).href, environment: i % 2 ? 'TEST' : 'PROD', displayOrder: i
        } }));

        browser = await chromium.launch({ executablePath: process.env.WASAS_CHROME || undefined });
        const context = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1440, height: 900 } });
        await context.route(new URL('harmless/**', ui).href, route => route.fulfill({ status: 200, contentType: 'text/html', body: '<title>harmless</title>' }));
        page = await context.newPage();
        const pageErrors = [];
        page.on('pageerror', error => pageErrors.push(error.message));
        await signIn(page, ui);
        await navigate(page, ui, 'resources');

        // Find: search narrows the list; the head states the range and the filter in words.
        await page.waitForFunction(() => [...(document.querySelector('[aria-label="Bağlantı ara"]')?.attributes || [])]
            .some(attribute => attribute.name.startsWith('_bl_')));
        await page.getByRole('textbox', { name: 'Bağlantı ara' }).fill(label);
        await page.locator('.so-resource-filter-summary').filter({ hasText: `Arama: “${label}”` }).waitFor();
        const head = page.locator('.so-resource-results-head [role=status]');
        await head.filter({ hasText: '30 bağlantı · 1–' }).waitFor();
        results.range = (await head.innerText()).trim();
        assert.match(results.range, /^30 bağlantı · 1–\d+ \/ 30 gösteriliyor · Sayfa 1 \/ \d+/);
        results.filters = (await page.locator('.so-resource-filter-summary').innerText()).trim();
        assert.ok(results.filters.includes(`Arama: “${label}”`), results.filters);
        await capture(page, out, 'resources-search');

        // Favourite gives explicit feedback.
        const first = links[0];
        await page.getByRole('button', { name: `${first.name}: Favorilere ekle` }).click();
        await page.locator('.so-workspace-notice').filter({ hasText: 'favorilerinize eklendi' }).waitFor();

        // Save one link to a new group, then two more to the same group by selection.
        const group = `${label} gece`;
        await page.getByRole('button', { name: `${first.name}: Grubuma kaydet` }).click();
        const dialog = page.getByRole('dialog');
        if (await dialog.getByRole('textbox', { name: 'Grup adı' }).count() === 0) {
            await dialog.getByRole('button', { name: 'Yeni kişisel grup oluştur' }).click();
        }
        await dialog.getByRole('textbox', { name: 'Grup adı' }).fill(group);
        await dialog.getByRole('button', { name: 'Oluştur ve kaydet' }).click();
        await dialog.waitFor({ state: 'detached' });
        const notice = page.locator('.so-workspace-notice');
        await notice.filter({ hasText: `“${first.name}” kişisel grubunuza kaydedildi: ${group}.` }).waitFor();
        await page.getByRole('checkbox', { name: `${links[1].name}: Seç` }).check();
        await page.getByRole('checkbox', { name: `${links[2].name}: Seç` }).check();
        await page.getByRole('button', { name: 'Seçilenleri grubuma kaydet' }).click();
        await dialog.getByRole('button', { name: new RegExp(group) }).click();
        await dialog.getByRole('button', { name: 'Grubuma kaydet' }).click();
        await dialog.waitFor({ state: 'detached' });
        await notice.filter({ hasText: `2 bağlantı kişisel grubunuza kaydedildi: ${group}.` }).waitFor();
        await capture(page, out, 'resources-saved');

        // Follow the confirmation to the group; it is selected on arrival.
        await notice.getByRole('link', { name: 'Grubu aç' }).click();
        await page.waitForURL(url => url.pathname.endsWith('/resources/sets') && url.searchParams.has('set'));
        groupId = new URL(page.url()).searchParams.get('set');
        await page.getByRole('heading', { name: group, level: 2 }).waitFor();
        await page.getByText('3 bağlantı kayıtlı.').waitFor();
        assert.equal(await page.locator('.so-set-item').count(), 3);

        // A saved link archived after saving is named as not opening; it carries no address or anchor.
        const archived = links[1];
        await json(admin, `/api/v1/resources/links/${archived.id}`, { method: 'PUT', data: { ...archived, archived: true, expectedVersion: archived.version } });
        const prepare = page.locator('.so-group-toolbar').getByRole('button', { name: 'Açmak için hazırla', exact: true });
        await prepare.focus();
        await page.keyboard.press('Enter');
        const panel = page.locator('.so-set-resolved');
        await panel.waitFor();
        assert.equal(await panel.locator('[data-so-open-url]').count(), 2);
        const excluded = panel.locator('.so-open-excluded');
        assert.equal(await excluded.locator('li').count(), 1);
        assert.equal((await excluded.locator('li').innerText()).trim(), archived.name);
        assert.equal(await excluded.locator('a').count(), 0);
        results.openOrder = await panel.locator('.so-open-order .so-open-name').allInnerTexts();
        assert.deepEqual(results.openOrder, [links[0].name, links[2].name]);
        await capture(page, out, 'sets-ready');

        // One deliberate action; the UI reports an attempt, never a confirmed load.
        const popups = [];
        context.on('page', p => popups.push(p));
        await panel.getByRole('button', { name: '2 bağlantıyı aç' }).click();
        await panel.locator('[data-so-open-status]').waitFor({ state: 'visible' });
        results.openStatus = (await panel.locator('[data-so-open-status]').innerText()).trim();
        assert.ok(results.openStatus.includes('açma isteği gönderildi'));
        await page.waitForTimeout(500);
        results.popupsUnderAutomation = popups.length;
        await Promise.all(popups.map(p => p.close()));

        // Edits report what happened.
        // Returning to editing re-reads the group, so the list no longer shows the archived link.
        await page.getByRole('button', { name: 'Düzenlemeye dön', exact: true }).click();
        await page.getByText('2 bağlantı kayıtlı.').waitFor();
        assert.equal(await page.locator('.so-set-item').count(), 2);
        await page.getByRole('button', { name: `${links[0].name}: Aşağı taşı` }).click();
        await page.locator('.so-workspace-notice').filter({ hasText: `“${links[0].name}” 2. sıraya taşındı.` }).waitFor();
        await page.getByRole('button', { name: `${links[0].name}: Gruptan çıkar` }).click();
        await page.locator('.so-workspace-notice').filter({ hasText: `“${links[0].name}” gruptan çıkarıldı.` }).waitFor();
        await capture(page, out, 'sets-feedback');

        // 200% zoom is approximated by a 720 CSS-pixel viewport at device scale 2 (not native browser zoom).
        const zoom = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 720, height: 450 }, deviceScaleFactor: 2 });
        const zoomed = await zoom.newPage();
        await signIn(zoomed, ui);
        for (const route of ['resources', `resources/sets?set=${groupId}`]) {
            await navigate(zoomed, ui, route);
            await zoomed.locator('.so-page-header, h1').first().waitFor();
            await zoomed.waitForTimeout(300);
            await noOverflow(zoomed, route + ' @720');
            await zoomed.screenshot({ path: path.join(out, `${route.startsWith('resources/sets') ? 'sets' : 'resources'}-zoom200.png`), fullPage: true, animations: 'disabled' });
        }
        await zoom.close();
        assert.deepEqual(pageErrors, []);
        results.passed = true;
    } finally {
        if (!results.passed && page) await page.screenshot({ path: path.join(out, 'failure.png'), fullPage: true }).catch(() => {});
        // Best-effort cleanup of this run's own fixtures; it must never hide the journey's failure.
        try {
            if (groupId) {
                const me = await json(admin, '/api/v1/resources/me');
                await admin.fetch(`/api/v1/resources/me/sets/${groupId}?expectedVersion=${me.version}`, { method: 'DELETE' });
            }
            for (const link of links) {
                const response = await admin.fetch(`/api/v1/resources/links/${link.id}`);
                if (!response.ok()) continue;
                const current = await response.json();
                if (!current.archived) await admin.fetch(`/api/v1/resources/links/${link.id}`, { method: 'PUT', data: { ...current, archived: true, expectedVersion: current.version } });
            }
        } catch (cleanup) { console.error('Cleanup incomplete:', cleanup.message); }
        fs.writeFileSync(path.join(out, 'shift-journey-results.json'), JSON.stringify(results, null, 2));
        if (browser) await browser.close();
        await admin.dispose();
    }
})().catch(error => { console.error(error); process.exit(1); });
