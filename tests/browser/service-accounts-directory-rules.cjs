// Real loopback API/UI, persisted SQL access, synthetic directory only. No Worker or corporate calls.
// node service-accounts-directory-rules.cjs <playwright-core> <UI URL> <API URL> <evidence directory>
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium, request } = require(process.argv[2]);
const { loopback, navigate, signIn, capture, apiContext, json } = require('./journey-support.cjs');
const ui = loopback(process.argv[3]), api = loopback(process.argv[4]), out = path.resolve(process.argv[5]);

(async () => {
    fs.mkdirSync(out, { recursive: true });
    const browser = await chromium.launch({ executablePath: process.env.WASAS_CHROME || undefined, headless: true });
    const context = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1440, height: 900 } });
    const page = await context.newPage();
    const admin = await apiContext(request, api), lead = await apiContext(request, api, 'team-lead');
    const anonymous = await request.newContext({ baseURL: api.href });
    const results = [], errors = [], rateLimits = [];
    page.on('pageerror', error => errors.push(String(error)));
    async function step(name, action) {
        try { await action(); results.push({ name, result: 'passed' }); }
        catch (error) { results.push({ name, result: 'failed', error: String(error.message).slice(0, 500) }); throw error; }
    }
    const route = '/api/v1/service-accounts/directory/name-search';
    async function search(client, query) {
        let response = await client.post(route, { data: { query } });
        if (response.status() === 429) {
            const seconds = Number(response.headers()['retry-after'] || 60);
            assert.ok(seconds >= 0 && seconds <= 60);
            rateLimits.push({ status: 429, retryAfter: seconds });
            await new Promise(resolve => setTimeout(resolve, (seconds + 1) * 1000));
            response = await client.post(route, { data: { query } });
        }
        assert.equal(response.status(), 200, await response.text());
        return response.json();
    }
    // Resume only the rule journey after the directory checks have passed on this test-owned inventory record.
    let accountId = process.env.SECUREOPS_SA_RULES_ACCOUNT_ID;
    if (accountId) assert.match(accountId, /^[a-f0-9-]{36}$/i);
    try {
        if (!accountId) {
        await step('actual authorization and query bounds fail closed', async () => {
            assert.equal((await anonymous.post(route, { data: { query: 'ismail' } })).status(), 401);
            for (const query of ['ab', 'ali*', 'ali)(cn=*', 'isisisisis']) {
                assert.equal((await admin.post(route, { data: { query } })).status(), 400);
            }
            const roles = await json(admin, '/api/v1/access/roles');
            const versions = Object.fromEntries(roles.map(role => [role.code, role.version]));
            async function assign(selected) {
                const me = await json(lead, '/api/v1/access/me');
                await json(admin, `/api/v1/access/users/${me.userId}/roles`, { method: 'PUT', data: {
                    roles: selected, expectedVersion: me.version,
                    roleVersions: Object.fromEntries(selected.map(code => [code, versions[code]]))
                } });
            }
            try {
                await assign(['SYN_SA_LEAD_1002']);
                assert.equal((await lead.post(route, { data: { query: 'ismail' } })).status(), 403);
                await assign(['Lead']);
                assert.equal((await lead.post(route, { data: { query: 'ismail' } })).status(), 403);
            } finally { await assign(['Lead', 'SYN_SA_LEAD_1002']); }
        });
        await step('Turkish spelling, same names and scope-safe links through HTTP', async () => {
            for (const query of ['ismail isik', '\u0130SMA\u0130L I\u015eIK', 'sukru ozturk']) {
                assert.equal((await search(admin, query)).matches.length, 1);
            }
            const same = await search(admin, 'ayse yilmaz');
            assert.equal(same.matches.length, 2);
            assert.ok(same.matches.every(match => match.sameNameAsAnother));
            const org = await json(admin, '/api/v1/service-accounts/organizations', {
                method: 'POST', data: { name: 'SYN DIRECTORY ' + Date.now(), kind: 'Department' }
            });
            const account = await json(admin, '/api/v1/service-accounts/accounts', { method: 'POST', data: {
                accountName: 'syn.ismail.isik', domain: 'SYNA', reportOrganizationId: org, reason: 'Synthetic loopback fixture'
            } });
            accountId = account.summary.id;
            assert.equal((await search(admin, 'ismail')).matches[0].serviceAccountId, accountId);
            assert.equal((await search(lead, 'ismail')).matches[0].serviceAccountId, null);
        });
        await step('directory panel links one accessible inventory record, then suppresses ambiguity', async () => {
            await signIn(page, ui);
            await navigate(page, ui, 'service-accounts');
            const query = page.getByLabel('Ad veya ad soyad', { exact: true });
            await query.fill('ismail isik');
            await query.press('Enter');
            const panel = page.locator('section[aria-labelledby="sa-directory-search"]');
            await panel.getByText('syn.ismail.isik', { exact: true }).waitFor();
            assert.equal(await panel.getByRole('link', { name: 'Kayd\u0131 a\u00e7' }).getAttribute('href'), `service-accounts/${accountId}`);
            await capture(page, out, 'directory-linked');
            await json(admin, '/api/v1/service-accounts/accounts', { method: 'POST', data: {
                accountName: 'syn.ismail.isik', domain: 'SYNB', reason: 'Synthetic ambiguous inventory fixture'
            } });
            assert.equal((await search(admin, 'ismail')).matches[0].serviceAccountId, null);
            await panel.getByRole('button', { name: 'Dizinde ara' }).click();
            await panel.getByText('Kapsam\u0131n\u0131zda kay\u0131t yok').waitFor();
            assert.equal(await panel.getByRole('link', { name: 'Kayd\u0131 a\u00e7' }).count(), 0);
        });
        }
        await step('usage is saved by the UI; recommendation is read-only until an explicit request', async () => {
            if (!page.url().startsWith(ui.href)) await signIn(page, ui);
            await navigate(page, ui, `service-accounts/${accountId}`);
            await page.locator('.mud-tab').filter({ hasText: /^Kullan\u0131m/ }).click();
            if ((await json(admin, `/api/v1/service-accounts/accounts/${accountId}`)).usages.length === 0) {
            await page.getByText('Kullan\u0131m ekle', { exact: true }).click();
            await page.getByLabel('Kullan\u0131m t\u00fcr\u00fc', { exact: true }).locator('visible=true').click();
            await page.locator('.mud-popover-open .mud-list-item').filter({ hasText: 'Zamanlanm\u0131\u015f g\u00f6rev' }).click();
            await page.getByLabel('Sunucu', { exact: true }).locator('visible=true').fill('SYNSRV_LOCAL');
            await page.getByRole('button', { name: 'Kullan\u0131m\u0131 kaydet' }).click();
            }
            await page.getByText('Kullan\u0131m yerleri', { exact: true }).waitFor();
            await page.locator('section[aria-labelledby="sa-usages"]').getByText('SYNSRV_LOCAL', { exact: true }).waitFor();
            let account = await json(admin, `/api/v1/service-accounts/accounts/${accountId}`);
            assert.equal(account.usages.length, 1);
            assert.equal(account.rule.path, 'SeekAlternative');
            assert.equal(account.requests.length, 0, 'recommendation must not create a request');
            await page.getByRole('button', { name: /^\u00d6neriden talep a\u00e7:/ }).click();
            await page.locator('.mud-tab').filter({ hasText: '\u0130\u015fler (1 a\u00e7\u0131k)' }).waitFor();
            account = await json(admin, `/api/v1/service-accounts/accounts/${accountId}`);
            assert.equal(account.requests.filter(item => item.actionType === 'Review').length, 1);
            await capture(page, out, 'usage-rule-request');
        });
        await step('real endpoint retains its 429 operation limit', async () => {
            let limited = false;
            for (let index = 0; index <= 10; index++) {
                const response = await admin.post(route, { data: { query: 'ismail' } });
                if (response.status() === 429) { limited = true; break; }
                assert.equal(response.status(), 200);
            }
            assert.equal(limited, true);
        });
        assert.deepEqual(errors, []);
    } finally {
        fs.writeFileSync(path.join(out, 'directory-rules-journey.json'), JSON.stringify({ results, errors, rateLimits, accountId }, null, 2));
        if (results.some(result => result.result === 'failed')) await page.screenshot({ path: path.join(out, 'failure.png'), fullPage: true }).catch(() => {});
        await browser.close();
        await admin.dispose(); await lead.dispose(); await anonymous.dispose();
    }
})().catch(error => { console.error(error); process.exitCode = 1; });
