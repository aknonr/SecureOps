// Synthetic loopback API/UI; weekly journey is separate. Verify monthly/custom immutable reports and comparison.
// node service-accounts-report-periods.cjs <playwright-core> <UI URL> <API URL> <evidence directory>
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium, request } = require(process.argv[2]);
const { loopback, navigate, signIn, capture, apiContext, json } = require('./journey-support.cjs');
const ui = loopback(process.argv[3]), api = loopback(process.argv[4]), out = path.resolve(process.argv[5]);

(async () => {
    fs.mkdirSync(out, { recursive: true });
    const browser = await chromium.launch({ executablePath: process.env.WASAS_CHROME || undefined, headless: true });
    const context = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1440, height: 900 }, acceptDownloads: true });
    const page = await context.newPage(), admin = await apiContext(request, api);
    const results = [], snapshots = [], errors = [];
    page.on('pageerror', error => errors.push(String(error)));
    async function choose(label, value) {
        await page.getByLabel(label, { exact: true }).locator('visible=true').click();
        await page.locator('.mud-popover-open .mud-list-item').filter({ hasText: value }).click();
    }
    async function field(label, value) {
        const input = page.getByLabel(label, { exact: true }).locator('visible=true');
        await input.fill(value); await input.press('Tab');
    }
    try {
        await signIn(page, ui);
        for (const period of ['Month', 'Custom']) {
            await navigate(page, ui, 'service-accounts/reports');
            await choose('D\u00f6nem', period === 'Month' ? 'Ayl\u0131k' : 'Tarih aral\u0131\u011f\u0131');
            await field(period === 'Month' ? 'Ay (herhangi bir g\u00fcn)' : 'Ba\u015flang\u0131\u00e7', '01.10.2026');
            if (period === 'Custom') await field('Biti\u015f (dahil)', '02.10.2026');
            await page.getByRole('button', { name: 'Canl\u0131 raporu g\u00f6ster' }).click();
            await page.getByRole('heading', { name: 'Canl\u0131 rapor', exact: true }).waitFor();
            const label = `SYN ${period} ${Date.now()}`;
            await field('A\u00e7\u0131klama (\u00f6r. al\u0131c\u0131 veya toplant\u0131)', label);
            await page.getByRole('button', { name: 'N\u00fcshay\u0131 kaydet' }).click();
            const row = page.getByRole('row').filter({ hasText: label });
            await row.waitFor();
            const item = (await json(admin, '/api/v1/service-accounts/reports/snapshots')).find(item => item.label === label);
            assert.ok(item);
            const report = await json(admin, `/api/v1/service-accounts/reports/snapshots/${item.id}`);
            assert.equal(report.period, period);
            assert.equal(report.weekStart, '2026-10-01');
            assert.equal(report.weekEndExclusive, period === 'Month' ? '2026-11-01' : '2026-10-03');
            assert.match(item.payloadSha256, /^[a-f0-9]{64}$/i);
            for (const format of ['XLSX', 'PDF']) {
                const download = page.waitForEvent('download');
                await row.getByRole('button', { name: format, exact: true }).click();
                const file = path.join(out, `${period}.${format.toLowerCase()}`);
                await (await download).saveAs(file);
                assert.equal(fs.readFileSync(file).subarray(0, 4).toString('latin1'), format === 'PDF' ? '%PDF' : 'PK\u0003\u0004');
            }
            snapshots.push({ id: item.id, label, payloadSha256: item.payloadSha256, report });
            await capture(page, out, 'report-' + period);
            results.push({ name: period + ' live/snapshot/download journey', result: 'passed' });
        }
        await json(admin, '/api/v1/service-accounts/accounts', { method: 'POST', data: {
            accountName: 'SYN_REPORT_LATER_' + Date.now(), reason: 'Synthetic post-snapshot change'
        } });
        for (const saved of snapshots) {
            assert.deepEqual(await json(admin, `/api/v1/service-accounts/reports/snapshots/${saved.id}`), saved.report);
        }
        await choose('\u00d6nceki n\u00fcsha', snapshots[0].label);
        await choose('Sonraki n\u00fcsha', snapshots[1].label);
        await page.getByRole('button', { name: 'Kar\u015f\u0131la\u015ft\u0131r', exact: true }).click();
        await page.locator('section[aria-labelledby="sa-compare"] table tbody tr').first().waitFor();
        await capture(page, out, 'snapshot-comparison');
        results.push({ name: 'snapshots remain immutable after a write and compare through UI', result: 'passed' });
        assert.deepEqual(errors, []);
    } catch (error) {
        results.push({ name: 'report journey', result: 'failed', error: String(error.message).slice(0, 500) });
        await page.screenshot({ path: path.join(out, 'failure.png'), fullPage: true }).catch(() => {});
        throw error;
    } finally {
        fs.writeFileSync(path.join(out, 'report-periods-journey.json'), JSON.stringify({ results, snapshots: snapshots.map(({ report, ...item }) => item), errors }, null, 2));
        await browser.close(); await admin.dispose();
    }
})().catch(error => { console.error(error); process.exitCode = 1; });
