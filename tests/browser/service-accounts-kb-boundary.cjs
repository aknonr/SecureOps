// Service Accounts knowledge-base/report v2 boundary journey on the REAL local composition (unmodified API Program and UI host).
// node service-accounts-kb-boundary.cjs <playwright path> <UI URL> <API URL> <evidence directory> [expected API status for module routes]
// Preconditions: API in the Demo environment with the demo bridge, platform stores in memory, ServiceAccounts:Provider=SqlServer
// against a disposable SA-001+SA-002 database (or Disabled when the expected status is 503). Loopback hosts and synthetic data only.
// What this proves: without a persisted role bundle no platform role reaches the new module routes or screens. What it cannot prove
// on Linux: the allowed journeys, because module actions exist only in SQL access-store role bundles and the API host requires
// Integrated Security for that store (WINDOWS-ACCEPTANCE.md section 4).
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium, request } = require(process.argv[2]);
const { loopback, navigate, signIn, capture, json } = require('./journey-support.cjs');
const ui = loopback(process.argv[3]);
const api = loopback(process.argv[4]);
const out = path.resolve(process.argv[5]);
const expected = Number(process.argv[6] || 403);
const results = [];

async function step(name, action) {
    try { await action(); results.push({ name, result: 'passed' }); }
    catch (error) { results.push({ name, result: 'failed', error: String(error.message || error).slice(0, 400) }); throw error; }
}

const id = '00000000-0000-4000-8000-0000000000a1';
const routes = [
    ['GET', 'me'], ['GET', 'team-roles'], ['GET', `accounts/${id}`], ['GET', 'reports/weekly?weekStart=2026-09-28'], ['GET', 'reports/snapshots'],
    ['GET', `reports/snapshots/${id}/xlsx`], ['GET', `reports/snapshots/${id}/pdf`],
    ['POST', `accounts/${id}/usages`, { kind: 'Database', databaseEngine: 'Oracle' }],
    ['PATCH', `usages/${id}`, { expectedVersion: 'AAAAAAAAB9E=', databaseEngine: 'SqlServer' }],
    ['POST', `usages/${id}/remove`, { expectedVersion: 'AAAAAAAAB9E=', reason: 'Sentetik' }],
    ['POST', `usages/${id}/exception`, { expectedVersion: 'AAAAAAAAB9E=', reason: 'Sentetik' }],
    ['POST', 'team-roles', { teamId: id, role: 'GmsaExecutor', reason: 'Sentetik' }],
    ['POST', `team-roles/${id}/revoke`, { reason: 'Sentetik' }],
    ['POST', 'reports/snapshots', { weekStart: '2026-09-28', kind: 'Manager' }]
];

(async () => {
    fs.mkdirSync(out, { recursive: true });
    const anonymous = await request.newContext({ baseURL: api.href });
    const admin = await request.newContext({ baseURL: api.href, extraHTTPHeaders: { 'X-SecureOps-Demo-Actor': 'platform-admin' } });
    const lead = await request.newContext({ baseURL: api.href, extraHTTPHeaders: { 'X-SecureOps-Demo-Actor': 'team-lead' } });
    try {
        await step('platform roles carry no module action', async () => {
            const me = await json(admin, '/api/v1/access/me');
            assert.ok(!me.capabilities.some(c => c.startsWith('ServiceAccounts.')), 'Admin must not imply module actions');
        });
        for (const [method, route, body] of routes) {
            await step(`${method} ${route}`, async () => {
                const options = { method, data: body };
                assert.equal((await anonymous.fetch(`/api/v1/service-accounts/${route}`, options)).status(), 401, 'anonymous');
                assert.equal((await admin.fetch(`/api/v1/service-accounts/${route}`, options)).status(), expected, 'platform-admin');
                assert.equal((await lead.fetch(`/api/v1/service-accounts/${route}`, options)).status(), expected, 'team-lead');
            });
        }

        if (expected === 403) {
            const browser = await chromium.launch();
            const page = await (await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1440, height: 900 } })).newPage();
            const errors = [];
            page.on('pageerror', e => errors.push(String(e)));
            await signIn(page, ui);
            for (const [route, name, text] of [
                ['service-accounts', 'list', 'Servis hesaplarını görüntüleme yetkisi gerekli'],
                [`service-accounts/${id}`, 'detail', 'Servis hesaplarını görüntüleme yetkisi gerekli'],
                ['service-accounts/reports', 'reports', 'Rapor yetkisi gerekli'],
                ['service-accounts/admin', 'admin', 'yetki']]) {
                await step(`UI ${route} refuses without a module bundle`, async () => {
                    await navigate(page, ui, route);
                    await page.getByText(text, { exact: false }).first().waitFor({ timeout: 15000 });
                    for (const hidden of ['Kullanım ve kural', 'gMSA yönlendirme ayarı', 'Nüsha karşılaştırma', 'Direktörlük görünümü']) {
                        assert.equal(await page.getByText(hidden).count(), 0, `${route}: ${hidden} must not render`);
                    }
                    await capture(page, out, `kb-boundary-${name}`);
                });
            }
            await step('navigation hides the module', async () => {
                await navigate(page, ui, '');
                assert.equal(await page.locator('a[href="service-accounts"], a[href="/service-accounts"]').count(), 0);
            });
            await step('no page errors', async () => assert.deepEqual(errors, []));
            await browser.close();
        }
    } finally {
        fs.writeFileSync(path.join(out, `kb-boundary-${expected}.json`), JSON.stringify(results, null, 2));
        console.log(JSON.stringify(results.map(r => `${r.result} ${r.name}${r.error ? ': ' + r.error : ''}`), null, 1));
    }
})().catch(error => { console.error(String(error.message || error)); process.exit(1); });
