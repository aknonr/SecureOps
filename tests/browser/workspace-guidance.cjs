// Published loopback hosts and existing synthetic SQL fixtures only; no proxy or external write.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium, request } = require(process.argv[2]);
const { loopback, navigate, signIn, capture, apiContext, json } = require('./journey-support.cjs');
const ui = loopback(process.argv[3]), api = loopback(process.argv[4]), out = path.resolve(process.argv[5]);
async function interactive(page, label) {
    await page.waitForFunction(label => [...(document.querySelector(`[aria-label="${label}"]`)?.attributes || [])].some(a => a.name.startsWith('_bl_')), label);
}
async function tour(page, surface, count) {
    const replay = page.locator(`#resource-guide-replay-${surface}`);
    await replay.focus(); await replay.press('Enter');
    const guide = page.getByRole('dialog', { name: 'Kullanım Rehberi', exact: true });
    await guide.waitFor();
    for (let i = 0; i < count; i++) {
        await page.waitForTimeout(350);
        const bounds = await guide.boundingBox();
        const viewport = page.viewportSize();
        assert.ok(bounds.x >= 0 && bounds.y >= 0 && bounds.x + bounds.width <= viewport.width + 1 && bounds.y + bounds.height <= viewport.height + 1,
            'Guide fits viewport: ' + JSON.stringify({ surface, i, bounds, ancestors: await guide.evaluate(e => { const a = []; for (let p = e; p; p = p.parentElement) { const s = getComputedStyle(p); a.push([p.className, s.transform, s.filter, s.backdropFilter, s.willChange, s.contain, p.style.cssText]); } return a; }) }));
        if (i === 1) { await guide.getByRole('button', { name: 'Geri', exact: true }).click(); await guide.getByRole('button', { name: 'İleri', exact: true }).click(); }
        if (i === count - 1) await guide.getByRole('button', { name: 'Bitir', exact: true }).click();
        else await guide.getByRole('button', { name: 'İleri', exact: true }).click();
    }
    await guide.waitFor({ state: 'hidden' });
    await page.waitForFunction(id => document.activeElement?.id === id, `resource-guide-replay-${surface}`);
}
(async () => {
    const browser = await chromium.launch({ executablePath: process.env.WASAS_CHROME || 'C:/Program Files/Google/Chrome/Application/chrome.exe', headless: true });
    const owner = await apiContext(request, api, 'team-lead'), admin = await apiContext(request, api);
    const checks = [], errors = [], failedAssets = [];
    let page, originalRoles, userId;
    try {
        let context = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1440, height: 900 }, reducedMotion: 'reduce' });
        page = await context.newPage();
        page.on('pageerror', e => errors.push(e.message));
        page.on('response', r => { if (/\.(css|js)(?:\?|$)/.test(r.url()) && r.status() >= 400) failedAssets.push(r.url()); });
        await signIn(page, ui);
        await navigate(page, ui, 'resources'); await interactive(page, 'Bağlantı ara');
        const preferences = await json(owner, '/api/v1/resources/me');
        if (!preferences.guideDismissed) {
            await page.locator('#resource-guide-start-links').press('Enter');
            await page.getByRole('dialog', { name: 'Kullanım Rehberi' }).press('Escape');
            await page.waitForFunction(() => document.activeElement?.id === 'resource-guide-start-links');
        }
        await tour(page, 'links', 4);
        assert.equal((await json(owner, '/api/v1/resources/me')).version, preferences.version, 'Tour must not dismiss/save preferences');
        await page.locator('#resource-guide-replay-links').click();
        await capture(page, out, 'after-links-tour');
        await page.getByRole('dialog', { name: 'Kullanım Rehberi' }).press('Escape');
        await page.getByRole('button', { name: 'Çalışma alanını düzenle', exact: true }).click();
        await tour(page, 'personalize', 3);
        await page.getByRole('button', { name: 'Vazgeç', exact: true }).click();
        assert.equal((await json(owner, '/api/v1/resources/me')).version, preferences.version);
        await navigate(page, ui, 'resources/sets');
        await page.waitForTimeout(500);
        const groupCount = (await json(owner, '/api/v1/resources/me')).sets.length;
        await tour(page, 'groups', 4);
        assert.equal((await json(owner, '/api/v1/resources/me')).sets.length, groupCount);
        checks.push('Links, groups and personalization tours: next/back/finish/Escape/replay, visible or missing targets, focus restoration, no personal writes');
        await navigate(page, ui, 'operational-records'); await interactive(page, 'Kayıt ara');
        await page.getByRole('button', { name: 'Kaynağı yenile', exact: true }).click();
        await page.getByText('Sınırlı kaynak yenilemesi tamamlandı.', { exact: false }).waitFor();
        const before = await json(owner, '/api/v1/operational-records/stored?search=SIM-OR&sort=code');
        assert.equal(before.total, 5);
        await page.getByLabel('Kayıt ara', { exact: true }).fill('SIM-OR');
        await page.getByText('5 kayıtlı eşleşme', { exact: true }).waitFor();
        await page.getByLabel('Sıralama', { exact: true }).selectOption('code');
        await page.getByLabel('Sayfa boyutu', { exact: true }).selectOption('10');
        await capture(page, out, 'after-records');
        const after = await json(owner, '/api/v1/operational-records/stored?search=SIM-OR&sort=code');
        assert.deepEqual(after.items.map(r => [r.id, r.version]), before.items.map(r => [r.id, r.version]), 'Browsing must not reimport');
        await page.getByLabel('Kayıt ara', { exact: true }).fill('NoMatchingSyntheticRecord');
        await page.getByText('Kayıtlı eşleşme yok', { exact: true }).waitFor();
        await capture(page, out, 'after-records-empty');
        await page.getByLabel('Kayıt ara', { exact: true }).fill('');
        await page.waitForFunction(() => document.querySelectorAll('.so-record-browse-list li').length === 10);
        await page.getByRole('button', { name: 'Sonraki', exact: true }).click();
        await page.getByText('Sayfa 2 /', { exact: false }).waitFor();
        checks.push('Explicit simulated source refresh once; stored search/sort/page-size and paging use persisted rows; actual stored totals and no-match state');
        await navigate(page, ui, `operational-records/${before.items[0].id}`);
        await page.locator('#sdm-request-type').waitFor();
        await page.waitForTimeout(400);
        assert.equal(await page.locator('#sdm-request-type').inputValue(), '');
        assert.equal(await page.getByRole('button', { name: 'İnceleme taslağı hazırla', exact: true }).isDisabled(), true);
        await page.getByText('Sunucu emekliliği gibi diğer talepler', { exact: false }).waitFor();
        await tour(page, 'review', 4);
        assert.equal((await json(owner, `/api/v1/operational-records/${before.items[0].id}`)).version, before.items[0].version);
        await capture(page, out, 'after-review-unresolved');
        await page.locator('#sdm-request-type').selectOption('SoftwareInstallation');
        await page.getByRole('button', { name: 'İnceleme taslağı hazırla', exact: true }).click();
        await page.getByText('Bu inceleme taslağı yayımlanamaz.', { exact: false }).waitFor();
        await capture(page, out, 'after-review-draft');
        assert.equal((await json(owner, `/api/v1/operational-records/${before.items[0].id}`)).jiraIssueKey, null);
        await page.locator('#resource-guide-replay-review').click();
        await capture(page, out, 'after-review-tour');
        await page.getByRole('dialog', { name: 'Kullanım Rehberi' }).press('Escape');
        await page.setViewportSize({ width: 390, height: 844 });
        await tour(page, 'review', 4);
        await page.setViewportSize({ width: 720, height: 450 });
        await tour(page, 'review', 4);
        await page.setViewportSize({ width: 1440, height: 900 });
        checks.push('Unresolved type stays empty including retirement scope notice; declaration-only review and guide do not authorize/create Jira; review tour focuses actual evidence and preview');
        const access = await json(owner, '/api/v1/access/me'); userId = access.userId;
        const original = await json(admin, `/api/v1/access/users/${userId}`); originalRoles = original.roles;
        async function roles(values) {
            await context.close();
            const current = await json(admin, `/api/v1/access/users/${userId}`);
            await json(admin, `/api/v1/access/users/${userId}/roles`, { method: 'PUT', data: { roles: values, expectedVersion: current.version, reason: 'Synthetic workspace verification' } });
            context = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1440, height: 900 } });
            page = await context.newPage();
            page.on('pageerror', e => errors.push(e.message));
            await signIn(page, ui);
        }
        await roles(['ResourceCurator']);
        await navigate(page, ui, 'admin/resources'); await page.getByText('Bağlantı Yönetimi', { exact: true }).first().waitFor();
        await capture(page, out, 'after-curator');
        await navigate(page, ui, 'operational-records'); await page.getByText('Bu alana erişim yetkiniz yok', { exact: true }).waitFor();
        await capture(page, out, 'after-records-denied');
        await roles(['Auditor']);
        await navigate(page, ui, 'dashboard');
        await page.getByRole('heading', { name: 'Yönetim Panosu', exact: true }).waitFor();
        await page.getByRole('tab', { name: 'Son 30 gün', exact: true }).click();
        await page.waitForTimeout(700); await capture(page, out, 'after-dashboard-regression');
        checks.push('Curator catalogue, operational denied role, auditor dashboard and authentication navigation remain reachable/denied according to current capability');
        assert.deepEqual(errors, []); assert.deepEqual(failedAssets, []);
        fs.writeFileSync(path.join(out, 'guidance-results.json'), JSON.stringify({ checks, errors, failedAssets }, null, 2));
        console.log(JSON.stringify({ checks, errors, failedAssets }, null, 2));
    } finally {
        if (originalRoles) { const current = await json(admin, `/api/v1/access/users/${userId}`); await json(admin, `/api/v1/access/users/${userId}/roles`, { method: 'PUT', data: { roles: originalRoles, expectedVersion: current.version, reason: 'Restore synthetic verification role' } }); }
        if (page && !page.isClosed()) await page.screenshot({ path: path.join(out, 'guidance-last-state.png'), fullPage: true }).catch(() => {});
        await browser.close(); await owner.dispose(); await admin.dispose();
    }
})().catch(e => { console.error(e); process.exitCode = 1; });
