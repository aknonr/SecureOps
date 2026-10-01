// Foreground Demo UI/API, SQL persistence, paired Simulation. Never a corporate endpoint.
// node management-journey.cjs <playwright path> <UI URL> <API URL> <database> <evidence directory>
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium, request } = require(process.argv[2]);
const { loopback, navigate, signIn, capture, apiContext, json } = require('./journey-support.cjs');
const sql = require('./management-sql.cjs');
const ui = loopback(process.argv[3]);
const api = loopback(process.argv[4]);
const database = process.argv[5];
const out = path.resolve(process.argv[6]);
const checks = [];

async function metric(page, label, count) {
    const value = page.locator('.so-metric').filter({ has: page.getByText(label, { exact: true }) }).first().locator('.so-metric-value');
    await value.filter({ hasText: String(count) }).waitFor();
    assert.equal((await value.innerText()).replaceAll('.', ''), String(count), label);
}

async function custom(page, from, to) {
    await page.getByRole('tab', { name: 'Özel aralık', exact: true }).click();
    for (const [name, value] of [['Başlangıç (UTC)', from], ['Bitiş (UTC, dahil)', to]]) {
        const input = page.getByLabel(name, { exact: true });
        await input.fill(value);
        await input.press('Tab');
    }
    await page.getByRole('button', { name: 'Uygula', exact: true }).click();
}

(async () => {
    const window = process.env.WASAS_REUSE_FIXTURES
        ? JSON.parse(fs.readFileSync(path.join(out, 'window.json'))) : sql.seed(database);
    fs.mkdirSync(out, { recursive: true });
    fs.writeFileSync(path.join(out, 'window.json'), JSON.stringify(window));
    const browser = await chromium.launch({ executablePath: process.env.WASAS_CHROME || 'C:/Program Files/Google/Chrome/Application/chrome.exe', headless: true });
    const client = await apiContext(request, api);
    try {
        const access = await json(client, '/api/v1/access/me');
        assert.ok(access.capabilities.includes('Reporting.ManagementView'));
        const route = `/api/v1/reporting/management/summary?window=custom&from=${encodeURIComponent(window.from)}&to=${encodeURIComponent(window.to)}`;
        const report = await json(client, route);
        assert.equal(report.identityLookup.totalLookups, 2);
        assert.equal(report.identityLookup.successfulLookups, 1);
        assert.equal(report.identityLookup.notFound, 1);
        assert.equal(report.operationalWorkflow.eligible, 1);
        assert.equal(report.operationalWorkflow.completed, 1);
        assert.equal(sql.query(database, "SELECT COUNT(*) FROM ops.OperationalRecords WHERE SourceRecordId='synthetic-management-history' AND JiraEligible=1"), '0');
        fs.writeFileSync(path.join(out, 'authoritative-report.json'), JSON.stringify(report, null, 2));
        checks.push('SQL lower bound included; upper bound excluded at 100ns precision; transitions differ from current eligibility');

        const context = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1440, height: 900 } });
        const page = await context.newPage();
        await signIn(page, ui);
        await navigate(page, ui, 'dashboard');
        await page.getByRole('heading', { name: 'Yönetim Panosu', exact: true }).waitFor();
        assert.equal(await page.getByRole('link', { name: 'Yönetim Panosu', exact: true }).count(), 1);
        const day = window.from.slice(0, 10);
        await custom(page, day, day);
        await metric(page, 'Kimlik sorgusu', report.identityLookup.totalLookups);
        await metric(page, 'Tamamlanan iş akışı', report.operationalWorkflow.completed);
        await capture(page, out, 'dashboard-populated');
        checks.push('Interactive custom UTC day matches authoritative SQL API counts at desktop/mobile sizes');

        await custom(page, '2026-01-01', '2026-01-01');
        await page.getByText('Bu aralıkta kayıtlı kanıt yok', { exact: true }).waitFor();
        assert.equal(await page.locator('.so-metric').count(), 0);
        await capture(page, out, 'dashboard-empty');
        await page.getByRole('tab', { name: 'Son 30 gün', exact: true }).click();
        await page.locator('.so-metric').first().waitFor();
        checks.push('Empty window has no invented metrics; preset recovers without stale custom bounds');

        const release = await sql.lockHistory(database);
        try {
            await page.getByRole('button', { name: 'Yenile', exact: true }).click();
            await page.getByText('Rapor hazırlanıyor…', { exact: true }).waitFor();
            assert.equal(await page.locator('.so-metric').count(), 0);
            assert.equal(await page.getByRole('tab', { name: 'Bugün', exact: true }).isDisabled(), true);
            await capture(page, out, 'dashboard-loading');
            await page.locator('.so-problem-title').waitFor({ timeout: 45000 });
            assert.equal(await page.locator('.so-metric').count(), 0);
            assert.ok(!(await page.locator('.so-problem').innerText()).includes('SqlException'));
            await capture(page, out, 'dashboard-failed');
        } finally { await release(); }
        await page.getByRole('button', { name: 'Yeniden yükle', exact: true }).click();
        await page.locator('.so-metric').first().waitFor();
        checks.push('Real SQL blocking produces loading/failed states; lock release and explicit retry recover');
        fs.writeFileSync(path.join(out, 'management-results.json'), JSON.stringify({ database, synthetic: true, checks }, null, 2));
        checks.forEach(check => console.log('PASS: ' + check));
    } finally { await client.dispose(); await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
