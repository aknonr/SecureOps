// Only the guarded local SQL + existing paired Simulation providers are supported.
// node sdm-journey.cjs <playwright path> <team-lead UI URL> <API URL> <database> <evidence directory>
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
const checks = [];

(async () => {
    query(database, 'SELECT 1');
    const browser = await chromium.launch({ executablePath: process.env.WASAS_CHROME || 'C:/Program Files/Google/Chrome/Application/chrome.exe', headless: true });
    const admin = await apiContext(request, api);
    let actor = await apiContext(request, api, 'team-lead');
    const user = await json(actor, '/api/v1/access/me');
    const original = await json(admin, '/api/v1/access/users/' + user.userId);
    assert.equal(original.corporateIdentity, 'demo:team-lead');
    assert.equal(query(database, `SELECT COUNT(*) FROM security.Users WHERE UserId='${user.userId}' AND CorporateIdentity='demo:team-lead'`), '1');
    let context;
    let page;
    async function role(name) {
        const current = await json(admin, '/api/v1/access/users/' + user.userId);
        await json(admin, `/api/v1/access/users/${user.userId}/roles`, {
            method: 'PUT', data: { roles: name, expectedVersion: current.version, reason: 'Synthetic isolated browser journey' }
        });
        await actor.dispose();
        actor = await apiContext(request, api, 'team-lead');
        if (context) await context.close();
        context = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1440, height: 900 } });
        page = await context.newPage();
        await signIn(page, ui);
    }
    async function detail(record) {
        await navigate(page, ui, 'operational-records/' + record.id);
        await page.getByRole('heading', { name: record.orCode, exact: true }).waitFor();
        await page.getByRole('heading', { name: 'Jira aktarımı', exact: true }).waitFor();
    }
    async function preview(record) {
        await detail(record);
        await page.getByRole('button', { name: 'Jira Taslağını Önizle', exact: true }).click();
        await page.getByRole('heading', { name: 'Önizleme hazırlandı', exact: true }).waitFor();
        const create = page.getByRole('button', { name: 'Jira Kaydı Oluştur', exact: true });
        assert.equal(await create.isEnabled(), true);
        return create;
    }
    async function publish(record, name, expectedState, resultText) {
        const create = await preview(record);
        await create.click();
        const dialog = page.getByRole('dialog', { name: 'Jira Kaydı Oluştur', exact: true });
        await dialog.waitFor();
        await page.waitForFunction(() => document.querySelector('[role="dialog"]')?.contains(document.activeElement));
        assert.equal(await dialog.getByRole('button', { name: 'Vazgeç', exact: true }).evaluate(element => element === document.activeElement), true);
        await capture(page, out, name + '-confirmation');
        await dialog.getByRole('button', { name: 'Oluştur', exact: true }).evaluate(button => { button.click(); button.click(); });
        await page.getByText(resultText, { exact: false }).first().waitFor();
        const latest = await json(actor, '/api/v1/operational-records/' + record.id);
        assert.equal(latest.workflowState, expectedState);
        await capture(page, out, name + '-result');
        return latest;
    }
    try {
        const records = await json(admin, '/api/v1/operational-records');
        const fixture = code => {
            const record = records.find(item => item.orCode === code);
            assert.ok(record?.simulationMode, 'Existing Simulation fixture required');
            return record;
        };
        const happy = fixture('SIM-OR-100');
        if (process.env.WASAS_CAPTURE_RESULTS) {
            context = await browser.newContext({ ignoreHTTPSErrors: true });
            page = await context.newPage();
            await signIn(page, ui);
            for (const [code, name] of [
                ['SIM-OR-100', 'sdm-success-result'], ['SIM-OR-200', 'sdm-source-conflict'],
                ['SIM-OR-300', 'sdm-known-failure-result'], ['SIM-OR-400', 'sdm-unknown-result'],
                ['SIM-OR-500', 'sdm-source-close-recovered']
            ]) {
                await detail(fixture(code));
                await capture(page, out, name);
            }
            console.log('PASS: persisted SDM result views after responsive drawer transition');
            return;
        }
        await role(['ReadOnly']);
        await navigate(page, ui, 'dashboard');
        await page.getByRole('heading', { name: 'Genel Bakış', exact: true }).waitFor();
        assert.equal(await page.getByRole('link', { name: 'Yönetim Panosu', exact: true }).count(), 0);
        assert.equal(await page.locator('.so-metric').count(), 0);
        await capture(page, out, 'dashboard-denied');
        await navigate(page, ui, 'reporting/operators');
        await page.getByRole('heading', { name: 'Bu alana erişim yetkiniz yok', exact: true }).waitFor();
        await detail(happy);
        assert.equal(await page.getByRole('button', { name: 'Jira Kaydı Oluştur', exact: true }).count(), 0);
        assert.equal(await page.getByRole('button', { name: 'Jira Taslağını Önizle', exact: true }).count(), 0);
        await page.getByText('Jira yayımlama yetkiniz yok.', { exact: false }).waitFor();
        for (const suffix of ['jira-preview', 'jira', 'retry']) {
            const response = await actor.post(`/api/v1/operational-records/${happy.id}/${suffix}`);
            assert.equal(response.status(), 403);
        }
        for (const suffix of ['summary', 'operators']) {
            assert.equal((await actor.get('/api/v1/reporting/management/' + suffix)).status(), 403);
        }
        await capture(page, out, 'sdm-view-without-publish');
        checks.push('ReadOnly views records; no preview/create controls; direct reporting and all publication APIs deny 403');

        await role(['JiraPublisher']);
        const access = await json(actor, '/api/v1/access/me');
        assert.ok(access.capabilities.includes('OperationalRecords.CreateJira'));
        assert.ok(!access.capabilities.includes('Reporting.ManagementView'));
        const blocked = records.find(record => !record.jiraEligible && record.evaluatedAt);
        assert.ok(blocked, 'Run the existing isolated SQL evaluation test first');
        await detail(blocked);
        await page.getByText('SDM kategori politikası onaylanmadı.', { exact: false }).waitFor();
        assert.equal(await page.getByRole('button', { name: 'Jira Kaydı Oluştur', exact: true }).isDisabled(), true);
        await capture(page, out, 'sdm-blocked');

        const create = await preview(happy);
        await create.click();
        const dialog = page.getByRole('dialog', { name: 'Jira Kaydı Oluştur', exact: true });
        await dialog.waitFor();
        await dialog.getByRole('button', { name: 'Vazgeç', exact: true }).click();
        await dialog.waitFor({ state: 'hidden' });
        assert.equal(await create.isEnabled(), true, 'Cancel preserves preview');
        assert.equal((await json(actor, '/api/v1/operational-records/' + happy.id)).jiraExists, false);
        checks.push('Publisher uses an established synthetic fixture; blocked records stay blocked; cancellation preserves draft');

        const completed = await publish(happy, 'sdm-success', 9, 'Aktarım tamamlandı');
        assert.match(completed.jiraIssueKey, /^SIM-/);
        assert.equal(await page.getByRole('button', { name: 'Jira Kaydı Oluştur', exact: true }).isDisabled(), true);
        assert.equal(query(database, `SELECT COUNT(*) FROM ops.OperationalRecordWorkflowHistory WHERE OperationalRecordId='${happy.id}' AND WorkflowState='CreateRequested'`), '1');
        await json(actor, `/api/v1/operational-records/${happy.id}/jira`, { method: 'POST' });
        assert.equal((await json(actor, '/api/v1/operational-records/' + happy.id)).jiraIssueKey, completed.jiraIssueKey);
        checks.push('Double confirmation dispatch produces one durable create request; existing Jira key survives API replay');

        const stale = fixture('SIM-OR-200');
        await preview(stale);
        await page.getByRole('button', { name: 'Jira Kaydı Oluştur', exact: true }).click();
        await page.getByRole('dialog').getByRole('button', { name: 'Oluştur', exact: true }).click();
        await page.getByText('Kaynak kayıt değişmiş', { exact: true }).waitFor();
        assert.equal((await json(actor, '/api/v1/operational-records/' + stale.id)).jiraExists, false);
        assert.equal(await page.getByRole('button', { name: 'Jira Kaydı Oluştur', exact: true }).isDisabled(), true);
        await capture(page, out, 'sdm-source-conflict');
        checks.push('Changed source conflicts before publication; consumed preview cannot authorize another create');

        const failed = await publish(fixture('SIM-OR-300'), 'sdm-known-failure', 10, 'Jira kaydı oluşturulamadı');
        assert.equal(failed.reconciliationRequired, false);
        assert.equal(failed.retryEligible, true);
        const unknown = await publish(fixture('SIM-OR-400'), 'sdm-unknown', 10, 'Jira sonucu doğrulanmalı');
        assert.equal(unknown.reconciliationRequired, true);
        assert.equal(unknown.retryEligible, false);
        await page.getByText('Jira sonucu belirsiz', { exact: true }).waitFor();
        assert.equal(await page.getByRole('heading', { name: 'Jira sonucu doğrulanmalı', exact: true }).count(), 1);
        assert.equal(await page.getByRole('button', { name: 'Yeniden Dene', exact: true }).isDisabled(), true);
        assert.ok(unknown.correlationId);
        checks.push('Known simulation failure is retryable; ambiguous simulated timeout requires reconciliation and blocks retry/create');

        const partial = await publish(fixture('SIM-OR-500'), 'sdm-source-close-failed', 11, 'Kaynak kayıt tamamlanamadı');
        assert.match(partial.jiraIssueKey, /^SIM-/);
        await page.getByRole('button', { name: 'Yeniden Dene', exact: true }).click();
        await page.getByRole('heading', { name: 'İş akışı durumu: Tamamlandı', exact: true }).waitFor();
        assert.equal((await json(actor, '/api/v1/operational-records/' + partial.id)).jiraIssueKey, partial.jiraIssueKey);
        checks.push('Source-close retry preserves the established Jira key and completes only the remaining stage');
        fs.writeFileSync(path.join(out, 'sdm-results.json'), JSON.stringify({ synthetic: true, database, checks }, null, 2));
        checks.forEach(check => console.log('PASS: ' + check));
    } finally {
        await role(original.roles);
        await actor.dispose();
        await admin.dispose();
        await browser.close();
    }
})().catch(error => { console.error(error); process.exitCode = 1; });
