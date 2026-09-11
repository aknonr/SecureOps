// Guarded synthetic SQL fixture from ResourceSqlTests.InUse_ReporterRefresh only.
const assert = require('node:assert/strict'), fs = require('node:fs'), path = require('node:path');
const { chromium, request } = require(process.argv[2]);
const { loopback, navigate, signIn, capture, apiContext, json } = require('./journey-support.cjs');
const ui = loopback(process.argv[3]), api = loopback(process.argv[4]), out = path.resolve(process.argv[5]);
(async () => {
    fs.mkdirSync(out, { recursive: true });
    const browser = await chromium.launch({ executablePath: 'C:/Program Files/Google/Chrome/Application/chrome.exe', headless: true });
    const client = await apiContext(request, api), errors = [];
    try {
        const context = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1440, height: 900 }, reducedMotion: 'reduce' });
        await context.route('**/*', r => ['localhost', '127.0.0.1'].includes(new URL(r.request().url()).hostname) ? r.continue() : r.abort());
        const page = await context.newPage();
        page.on('pageerror', e => errors.push(e.message));
        page.on('dialog', async d => { errors.push(d.message()); await d.dismiss(); });
        await signIn(page, ui);
        const stored = await json(client, '/api/v1/in-use?search=OR-91000');
        assert.equal(stored.total, 1);
        const record = stored.items[0];
        assert.equal(record.source.title, 'Synthetic RFC reporter acceptance');
        assert.equal(record.source.servers.length, 4);
        await navigate(page, ui, 'in-use');
        await page.getByLabel('In Use kayıt ara', { exact: true }).fill('OR-91000');
        await page.getByLabel('In Use görünüm', { exact: true }).focus();
        await page.waitForFunction(() => document.querySelectorAll('.so-inuse-records > li').length === 1);
        assert.match(await page.locator('.so-inuse-records').innerText(), /İlgili talebi bildiren/);
        assert.equal(await page.locator('.so-inuse-records .so-inuse-reporter').count(), 3);
        await capture(page, out, 'reporter-list');
        await page.locator(`.so-inuse-records a[href="in-use/${record.id}"]`).last().click();
        const table = page.getByLabel('Servis öğesi tablosu', { exact: true });
        await table.waitFor();
        const rows = table.locator('tbody tr');
        assert.match(await rows.nth(0).innerText(), /OR-200.*Doğrulandı/s);
        assert.match(await rows.nth(0).innerText(), /Sentetik Şahıs 200 <b>/);
        assert.match(await rows.nth(0).innerText(), /Kişi referansı: 800/);
        assert.match(await rows.nth(2).innerText(), /OR-201.*Erişim reddedildi.*Önceki kanıt: Sentetik Şahıs 201 &lt;b&gt;/s);
        assert.match(await rows.nth(3).innerText(), /RFC boş/);
        assert.equal(await page.locator('.so-inuse-reporter b, .so-inuse-reporter script').count(), 0);
        await capture(page, out, 'reporter-detail');
        await page.setViewportSize({ width: 390, height: 844 });
        await table.evaluate(el => { el.scrollLeft = el.scrollWidth; el.scrollIntoView({ block: 'start' }); });
        await page.evaluate(() => window.scrollBy(0, -80));
        await page.screenshot({ path: path.join(out, 'reporter-mobile-table.png') });
        await rows.nth(2).locator('summary').click();
        assert.match(await rows.nth(2).innerText(), /Son doğrulama:.*\d{2}\.\d{2}\.\d{4}/s);
        await table.evaluate(el => { el.scrollTop = el.scrollHeight; });
        await page.screenshot({ path: path.join(out, 'reporter-mobile-retained.png') });
        const denied = await apiContext(request, api, 'team-lead');
        try {
            assert.equal((await denied.get(`/api/v1/in-use/${record.id}`)).status(), 403);
            assert.equal((await denied.post('/api/v1/in-use/refresh', { data: { commandId: '11111111-1111-4111-8111-111111111111' } })).status(), 403);
        } finally { await denied.dispose(); }
        const reread = await json(client, `/api/v1/in-use/${record.id}`);
        assert.equal(reread.version, record.version);
        assert.equal(reread.sourceHash, record.sourceHash);
        assert.deepEqual(reread.source, record.source);
        assert.deepEqual(errors, []);
        fs.writeFileSync(path.join(out, 'result.json'), JSON.stringify({ passed: true, viewports: [1440, 390], sourceCallsDuringDisplay: 'None; Simulation host reads persisted fake-transport SQL fixture', checks: ['per-server RFC', 'shared grouping', 'Turkish single-pass text', 'retained denied evidence', 'null RFC', 'authorization', 'unchanged DB versions'] }, null, 2));
    } finally { await client.dispose(); await browser.close(); }
})().catch(e => { console.error(e); process.exitCode = 1; });
