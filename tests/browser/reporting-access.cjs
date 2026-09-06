// Local SQL capability matrix through the real browser and API, with synthetic actors only.
// node reporting-access.cjs <playwright path> <team-lead UI URL> <API URL> <database> <evidence directory>
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium, request } = require(process.argv[2]);
const { loopback, navigate, signIn, capture, apiContext, json } = require('./journey-support.cjs');
const { query } = require('./management-sql.cjs');
const ui = loopback(process.argv[3]);
const api = loopback(process.argv[4]);
const database = process.argv[5];
const out = path.resolve(process.argv[6]);

(async () => {
    const admin = await apiContext(request, api);
    const browser = await chromium.launch({ executablePath: process.env.WASAS_CHROME || 'C:/Program Files/Google/Chrome/Application/chrome.exe' });
    const initial = await apiContext(request, api, 'team-lead');
    const identity = await json(initial, '/api/v1/access/me');
    await initial.dispose();
    const original = await json(admin, '/api/v1/access/users/' + identity.userId);
    assert.equal(original.corporateIdentity, 'demo:team-lead');
    assert.equal(query(database, `SELECT COUNT(*) FROM security.Users WHERE UserId='${identity.userId}' AND CorporateIdentity='demo:team-lead'`), '1');
    async function roles(values) {
        const user = await json(admin, '/api/v1/access/users/' + identity.userId);
        await json(admin, `/api/v1/access/users/${identity.userId}/roles`, {
            method: 'PUT', data: { roles: values, expectedVersion: user.version, reason: 'Synthetic reporting access verification' }
        });
    }
    const checks = [];
    try {
        for (const role of ['Admin', 'Auditor', 'Lead', 'JiraPublisher', 'Operator', 'ReadOnly']) {
            await roles([role]);
            const client = await apiContext(request, api, 'team-lead');
            const context = await browser.newContext({ ignoreHTTPSErrors: true });
            try {
                const allowed = role === 'Admin' || role === 'Auditor';
                const access = await json(client, '/api/v1/access/me');
                assert.equal(access.capabilities.includes('Reporting.ManagementView'), allowed);
                const results = {};
                for (const route of ['summary', 'operators']) {
                    const response = await client.get('/api/v1/reporting/management/' + route);
                    assert.equal(response.status(), allowed ? 200 : 403, role + ': ' + route);
                    results[route] = await response.json();
                }
                const page = await context.newPage();
                await signIn(page, ui);
                await navigate(page, ui, 'dashboard');
                await page.getByRole('heading', { name: allowed ? 'Yönetim Panosu' : 'Genel Bakış', exact: true }).waitFor();
                assert.equal(await page.getByRole('link', { name: 'Yönetim Panosu', exact: true }).count(), allowed ? 1 : 0);
                if (role === 'ReadOnly') await capture(page, out, 'dashboard-denied');
                await navigate(page, ui, 'reporting/operators');
                if (allowed) {
                    await page.locator('.so-operator-table tbody tr').first().waitFor();
                    assert.equal(await page.locator('.so-operator-table tbody tr').count(), results.operators.items.length);
                    const actor = results.operators.items[0].actor;
                    assert.ok((await page.locator('.so-operator-table').innerText()).includes(actor));
                } else {
                    await page.getByRole('heading', { name: 'Bu alana erişim yetkiniz yok', exact: true }).waitFor();
                    assert.equal(await page.locator('.so-operator-table').count(), 0);
                }
                checks.push({ role, reportingAllowed: allowed, menuAndDirectRoutes: 'passed', api: 'passed' });
                console.log('PASS: reporting matrix ' + role);
            } finally { await context.close(); await client.dispose(); }
        }
        fs.writeFileSync(path.join(out, 'reporting-access.json'), JSON.stringify({ synthetic: true, checks }, null, 2));
    } finally { await roles(original.roles); await admin.dispose(); await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
