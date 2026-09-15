// Loopback baseline/changed payload check. No corporate access or sending.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { loopback, navigate, signIn, apiContext, json } = require('./journey-support.cjs');
const [driver, uiValue, apiValue, out, mode = 'baseline'] = process.argv.slice(2);
const ui = loopback(uiValue), api = loopback(apiValue);
const { chromium, request } = require(driver);
(async () => {
    const started = Date.now(), measures = [];
    fs.mkdirSync(out, { recursive: true });
    const browser = await chromium.launch({ channel: 'msedge', headless: true });
    const context = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1440, height: 900 }, reducedMotion: 'reduce' });
    const page = await context.newPage();
    const admin = await apiContext(request, api, 'platform-admin');
    const lead = await apiContext(request, api, 'team-lead');
    try {
        const target = await json(lead, 'api/v1/access/me');
        await signIn(page, ui);
        for (const route of ['access/requests', 'access/users', `access/users/${target.userId}`]) {
            const before = Date.now();
            await navigate(page, ui, route);
            await page.locator('.so-loading').last().waitFor({ state: 'hidden' });
            for (const width of [1440, 390]) {
                await page.setViewportSize({ width, height: width === 390 ? 844 : 900 });
                if (width === 390) await page.waitForFunction(() => {
                    const drawer = document.querySelector('.so-drawer');
                    return (!drawer || (drawer.classList.contains('mud-drawer--closed') && drawer.getBoundingClientRect().right <= 1))
                        && document.querySelector('.so-page').getBoundingClientRect().width > 300;
                });
                const layout = await page.evaluate(() => {
                    const p = document.querySelector('.so-page'), c = getComputedStyle(p), r = p.getBoundingClientRect();
                    return { viewport: innerWidth, dpr: devicePixelRatio, visualScale: visualViewport.scale,
                        cssZoom: c.zoom, contentWidth: r.width, fontSize: c.fontSize, maxWidth: c.maxWidth,
                        overflow: document.documentElement.scrollWidth > innerWidth + 1 };
                });
                assert.equal(layout.overflow, false);
                measures.push({ route, width, ...layout, elapsedMs: Date.now() - before });
                await page.screenshot({ path: path.join(out, `${route.split('/').slice(0, 2).join('-')}-${route.includes(target.userId) ? 'detail-' : ''}${width}.png`), animations: 'disabled' });
            }
            await page.setViewportSize({ width: 1440, height: 900 });
        }
        await page.getByRole('button', { name: 'Rolleri değiştir', exact: true }).click();
        const dialog = page.getByRole('dialog');
        await dialog.waitFor();
        await dialog.getByRole('button', { name: 'Rolleri uygula', exact: true }).waitFor();
        await page.screenshot({ path: path.join(out, 'roles-dialog.png'), animations: 'disabled' });
        fs.writeFileSync(path.join(out, 'dialog.html'), await dialog.innerHTML());
        assert.equal(await dialog.locator('textarea').count(), mode === 'baseline' ? 1 : 0);
        await dialog.getByRole('button', { name: 'Vazgeç', exact: true }).click();
        if (mode !== 'baseline') {
            await page.getByRole('button', { name: 'Rolleri değiştir', exact: true }).click();
            await dialog.getByRole('button', { name: 'Rolleri uygula', exact: true }).click();
            await dialog.waitFor({ state: 'hidden' });
            await page.getByRole('heading', { name: 'Roller güncellendi', exact: true }).waitFor();
            const fresh = await json(admin, `api/v1/access/users/${target.userId}`);
            assert.equal(fresh.version, target.version + 1);
            await page.getByRole('button', { name: 'Erişimi kapat', exact: true }).click();
            await dialog.waitFor();
            assert.equal(await dialog.locator('textarea').count(), 1);
            await dialog.getByLabel('Gerekçe', { exact: true }).focus();
            await dialog.getByRole('button', { name: 'Vazgeç', exact: true }).click();
            await navigate(page, ui, 'announcements');
            await page.getByRole('button', { name: 'Yeni duyuru', exact: true }).click();
            await page.getByText('Profil e-postası / yeni hazırlığın göndereni', { exact: true }).waitFor();
            assert.ok(await page.getByText('actor@example.invalid', { exact: true }).count() > 0);
            await page.screenshot({ path: path.join(out, 'actor-sender.png'), animations: 'disabled' });
        }
        fs.writeFileSync(path.join(out, 'result.json'), JSON.stringify({ mode, seconds: (Date.now() - started) / 1000, measures }, null, 2));
        console.log(`PASS ${mode} ${((Date.now() - started) / 1000).toFixed(3)}s`);
    } finally { await admin.dispose(); await lead.dispose(); await browser.close(); }
})().catch(e => { console.error(e); process.exitCode = 1; });
