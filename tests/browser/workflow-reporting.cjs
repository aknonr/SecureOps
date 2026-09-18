// Loopback SQL/API/Blazor acceptance; no corporate service or remote mutation.
const assert = require('node:assert/strict'), fs = require('node:fs'), path = require('node:path'), crypto = require('node:crypto');
const { chromium, request } = require(process.argv[2]);
const { loopback, navigate, signIn, apiContext, json } = require('./journey-support.cjs');
const { query, seed } = require('./management-sql.cjs');
const ui = loopback(process.argv[3]), api = loopback(process.argv[4]), database = process.argv[5], out = path.resolve(process.argv[6]);
(async () => {
    fs.mkdirSync(out, { recursive: false });
    const client = await apiContext(request, api), denied = await apiContext(request, api, 'team-lead');
    const actor = await json(client, '/api/v1/access/me');
    query(database, `IF (SELECT CorporateIdentity FROM security.Users WHERE UserId='${actor.userId}')<>'demo:platform-admin' THROW 51239,'Synthetic actor required',1;
        UPDATE security.Users SET Mail='operator@example.invalid',DisplayName='Synthetic report operator' WHERE UserId='${actor.userId}';`);
    const resumed = process.argv[7] === 'resumed';
    if (!resumed) { seed(database); await json(client, '/api/v1/in-use/refresh', { method: 'POST', data: { commandId: crypto.randomUUID() } }); }
    const id = resumed ? query(database, `SELECT CONVERT(varchar(36),DraftId) FROM announcements.Preparations WHERE OwnerId='${actor.userId}' AND Subject='Synthetic management preparation';`) : crypto.randomUUID();
    assert.match(id, /^[a-f0-9-]{36}$/i);
    const preparation = crypto.randomUUID();
    const content = { ocoReference: 'OCO-SYNTHETIC', scope: 'Synthetic reporting scope', subject: 'Synthetic management preparation',
        announcementDate: '2026-09-18', workStart: '2026-09-18T10:00:17+03:00', workEnd: '2026-09-18T11:00:29+03:00',
        description: 'Synthetic', impact: 'Synthetic', checks: 'Synthetic', notes: '', to: ['reader@example.invalid'], cc: [],
        bannerRevision: 'bundle-v1', templateRevision: 'oco-table-v3', dateTextRevision: 'tr-v1', affectedServices: ['Synthetic service'] };
    if (!resumed) {
        await json(client, `/api/v1/announcements/${id}?version=0`, { method: 'PUT', data: content });
        await json(client, `/api/v1/announcements/preparations/${preparation}?draftId=${id}&version=1`, { method: 'PUT' });
    }
    const browser = await chromium.launch({ executablePath: 'C:/Program Files/Google/Chrome/Application/chrome.exe', headless: true });
    const context = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1366, height: 768 } });
    await context.route('**/*', r => ['localhost', '127.0.0.1'].includes(new URL(r.request().url()).hostname) ? r.continue() : r.abort());
    const page = await context.newPage(), errors = [], measurements = [];
    page.on('pageerror', e => errors.push(e.message));
    try {
        await signIn(page, ui); await navigate(page, ui, 'dashboard');
        const panel = page.locator('.workflow-report');
        await panel.getByRole('button', { name: 'Raporu yenile', exact: true }).click();
        await panel.getByText('Bu filtrelerde kayıtlı kanıt bulunamadı.', { exact: true }).waitFor();
        await panel.getByRole('checkbox').check();
        await panel.getByRole('button', { name: 'Raporu yenile', exact: true }).click();
        await panel.getByText('Hazırlanmış duyuru', { exact: true }).waitFor();
        const cut = query(database, `SELECT TOP(1) CONVERT(varchar(36),Id) FROM reporting.WorkflowSnapshots WHERE OwnerId='${actor.userId}' ORDER BY AsOf DESC;`);
        const report = await json(client, `/api/v1/reporting/management/workflows/${cut}?pageSize=100`);
        assert.equal(report.total, report.metrics.reduce((n, m) => n + m.count, 0));
        assert.ok(report.metrics.some(m => m.key === 'InUse.Backlog'));
        assert.ok(report.metrics.some(m => m.key === 'Sdm.Blocked'));
        assert.equal(report.metrics.find(m => m.key === 'Oco.Prepared').count, 1);
        assert.equal((await denied.get(`/api/v1/reporting/management/workflows/${cut}`)).status(), 403);
        const downloadPromise = page.waitForEvent('download');
        await panel.getByRole('button', { name: "Yönetim Excel'ini indir", exact: true }).click();
        const download = await downloadPromise;
        await download.saveAs(path.join(out, 'synthetic-management.xlsx'));
        assert.equal(fs.readFileSync(path.join(out, 'synthetic-management.xlsx')).readUInt16LE(0), 0x4b50);
        for (const theme of ['light', 'dark']) {
            await page.getByRole('button', { name: 'Hesap menüsü', exact: true }).click();
            await page.locator('.so-user-menu-popover .mud-list-item').filter({ hasText: theme === 'dark' ? /^Koyu/ : /^Aydınlık/ }).click();
            for (const width of [1366, 1440, 390]) {
                await page.setViewportSize({ width, height: width === 1366 ? 768 : 900 });
                await panel.scrollIntoViewIfNeeded(); await page.waitForTimeout(350);
                const observed = await panel.evaluate(el => ({ font: getComputedStyle(el).fontSize,
                    overflow: document.documentElement.scrollWidth > innerWidth + 1,
                    color: getComputedStyle(el).color, background: getComputedStyle(el).backgroundColor }));
                assert.equal(observed.overflow, false);
                measurements.push({ theme, width, ...observed });
                await page.screenshot({ path: path.join(out, `dashboard-${theme}-${width}.png`), fullPage: true });
            }
        }
        await page.setViewportSize({ width: 1366, height: 768 });
        await panel.getByLabel('Modül', { exact: true }).selectOption('Oco');
        await page.waitForFunction(() => document.querySelectorAll('.workflow-report .workflow-table')[1]?.querySelectorAll('tbody tr').length === 1);
        await panel.getByRole('link', { name: 'OCO-SYNTHETIC', exact: true }).waitFor();
        assert.equal(await panel.locator('table').last().locator('tbody tr').count(), 1);
        await page.keyboard.press('Tab');
        assert.notEqual(await page.evaluate(() => document.activeElement.tagName), 'BODY');
        await panel.getByRole('link', { name: 'OCO-SYNTHETIC', exact: true }).click();
        await page.locator('#announcement-Subject').waitFor();
        assert.equal(await page.locator('#announcement-Subject').inputValue(), content.subject);
        assert.deepEqual(errors, []);
        fs.writeFileSync(path.join(out, 'result.json'), JSON.stringify({ passed: true, cut, asOf: report.asOf, metrics: report.metrics,
            measurements, checks: ['synthetic excluded by default', 'SQL totals and drilldown', 'owner scope SQL tests separate', 'denied direct read',
                'same-cut browser Excel', 'both themes VDI/desktop/mobile', 'keyboard', 'exact OCO draft link'],
            limitations: ['corporate activation not tested', 'native zoom tested separately', 'SMTP and source readback not implied'] }, null, 2));
    } catch (error) { await page.screenshot({ path: path.join(out, 'failure.png'), fullPage: true }); throw error; }
    finally { await context.close(); await browser.close(); await client.dispose(); await denied.dispose(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
