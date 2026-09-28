// UI-boundary negative paths for Jira-only submission against isolated SQL/Simulation hosts started with
// announcement-hosts.ps1 -OperationalRecordSimulation -UiApiProxyPort <proxy>, plus sdm-fault-proxy.cjs.
// Never contacts corporate providers; every key is a synthetic SIM-* value. Needs fresh fixtures.
// node sdm-jira-negative.cjs <playwright path> <admin UI URL> <API URL> <proxy URL> <database> <evidence directory>
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium, request } = require(process.argv[2]);
const { loopback, navigate, signIn, capture, apiContext, json } = require('./journey-support.cjs');
const { query } = require('./management-sql.cjs');
const ui = loopback(process.argv[3]);
const api = loopback(process.argv[4]);
const proxy = loopback(process.argv[5]);
const database = process.argv[6];
const out = path.resolve(process.argv[7]);

(async () => {
    const started = Date.now();
    const checks = [];
    query(database, 'SELECT 1');
    const browser = await chromium.launch({ executablePath: process.env.WASAS_CHROME || 'C:/Program Files/Google/Chrome/Application/chrome.exe', headless: true });
    const admin = await apiContext(request, api);
    const control = await request.newContext({ baseURL: proxy.href });
    const context = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1440, height: 900 } });
    const page = await context.newPage();
    const panel = () => page.locator('[data-jira-submission]');
    const phase = () => panel().getAttribute('data-jira-submission');
    const button = name => page.getByRole('button', { name, exact: true });
    const history = (id, state) => query(database, `SELECT COUNT(*) FROM ops.OperationalRecordWorkflowHistory WHERE OperationalRecordId='${id}' AND WorkflowState='${state}'`);
    let lead;
    let leadOriginal;
    async function setLeadRoles(roles) {
        const current = await json(admin, '/api/v1/access/users/' + lead.userId);
        const catalog = await json(admin, '/api/v1/access/roles');
        await json(admin, `/api/v1/access/users/${lead.userId}/roles`, { method: 'PUT', data: { roles, expectedVersion: current.version,
            roleVersions: Object.fromEntries(catalog.filter(r => roles.includes(r.code)).map(r => [r.code, r.version])) } });
    }
    async function mode(value) { assert.equal(await (await control.get('/__proxy/mode/' + value)).text(), value); }
    async function open(record) {
        await navigate(page, ui, 'operational-records/' + record.id);
        await page.getByRole('heading', { name: record.orCode, exact: true }).waitFor();
        await panel().waitFor();
    }
    async function preview() {
        await button('Jira Taslağını Önizle').click();
        await page.waitForFunction(() => document.querySelector('[data-jira-submission]')?.getAttribute('data-jira-submission') === 'Ready');
    }
    async function confirmCreate() {
        await button('Jira Kaydı Oluştur').click();
        const dialog = page.getByRole('dialog');
        await dialog.waitFor();
        await dialog.getByRole('checkbox').check();
        await dialog.getByRole('button', { name: 'Oluştur', exact: true }).click();
        await dialog.waitFor({ state: 'hidden' });
    }
    async function settled(expected) {
        await page.waitForFunction(value => document.querySelector('[data-jira-submission]')?.getAttribute('data-jira-submission') === value, expected);
    }
    try {
        const records = await json(admin, '/api/v1/operational-records');
        const fixture = code => {
            const record = records.find(r => r.orCode === code);
            assert.ok(record?.simulationMode && !record.jiraIssueKey && !record.reconciliationRequired, code + ' must be a fresh synthetic fixture');
            return record;
        };
        lead = await json(await apiContext(request, api, 'team-lead'), '/api/v1/access/me');
        leadOriginal = (await json(admin, '/api/v1/access/users/' + lead.userId)).roles;
        await signIn(page, ui);

        // ---- stale preview: another operator publishes while this page still holds Ready
        const shared = fixture('SIM-OR-100');
        await open(shared);
        await preview();
        await setLeadRoles(['JiraPublisher']);
        const other = await apiContext(request, api, 'team-lead');
        const first = await other.post(`/api/v1/operational-records/${shared.id}/jira`);
        const otherKey = first.status() === 200 ? (await first.json()).jiraIssueKey : null;
        await other.dispose();
        assert.ok(otherKey, 'second operator create must succeed first: ' + first.status());
        assert.equal(await phase(), 'Ready', 'page still holds the stale preview');
        await button('Jira Kaydı Oluştur').dblclick();
        await page.getByRole('dialog').waitFor();
        assert.equal(await page.getByRole('dialog').count(), 1, 'double click opens one confirmation');
        const dialog = page.getByRole('dialog');
        await dialog.getByRole('checkbox').check();
        await dialog.getByRole('button', { name: 'Oluştur', exact: true }).evaluate(e => { e.click(); e.click(); });
        await settled('Created');
        assert.equal(await panel().locator('[data-jira-key]').textContent(), otherKey);
        assert.equal(query(database, `SELECT COUNT(*) FROM ops.JiraTransfers WHERE OperationalRecordId='${shared.id}' AND JiraIssueKey IS NOT NULL`), '1');
        assert.equal(history(shared.id, 'JiraCreated'), '1');
        await capture(page, out, 'negative-stale-preview-other-operator');
        checks.push(`Stale preview after another operator published: page shows the one saved key ${otherKey}; double click opened one dialog; one transfer, one JiraCreated`);

        // ---- lost response after the server finished: re-read resolves to the saved key
        const lost = fixture('SIM-OR-500');
        await open(lost);
        await preview();
        await mode('drop-after');
        await confirmCreate();
        await settled('Created');
        const lostSaved = await json(admin, '/api/v1/operational-records/' + lost.id);
        assert.ok(lostSaved.jiraIssueKey);
        assert.equal(await panel().locator('[data-jira-key]').textContent(), lostSaved.jiraIssueKey);
        await page.getByRole('heading', { name: 'Jira sonucu doğrulanmalı', exact: true }).first().waitFor();
        assert.equal(history(lost.id, 'JiraCreated'), '1');
        await capture(page, out, 'negative-lost-response-resolved');
        checks.push('Lost response after server success: problem notice kept, panel shows only the re-read saved key, one JiraCreated');

        // ---- request never reached the API: uncertain until explicit refresh, then no persisted key
        const dropped = fixture('SIM-OR-300');
        await open(dropped);
        await preview();
        const versionBefore = (await json(admin, '/api/v1/operational-records/' + dropped.id)).version;
        await mode('drop-before');
        await confirmCreate();
        await settled('Uncertain');
        assert.equal(await button('Jira Kaydı Oluştur').isDisabled(), true);
        assert.equal(await button('Yeniden Dene').isDisabled(), true);
        const untouched = await json(admin, '/api/v1/operational-records/' + dropped.id);
        assert.ok(!untouched.jiraExists && !untouched.reconciliationRequired && untouched.version === versionBefore);
        assert.equal(history(dropped.id, 'CreateRequested'), '0');
        await capture(page, out, 'negative-dropped-uncertain');
        await button('Yenile').click();
        await settled('Unavailable');
        await panel().getByText('Jira kaydı oluşturmadan önce önizleme alın.', { exact: false }).waitFor();
        assert.equal(await panel().locator('[data-jira-key]').count(), 0);
        await capture(page, out, 'negative-dropped-refreshed');
        checks.push('Request dropped before the API: Uncertain with create/retry disabled after auto re-read; explicit refresh shows no persisted key and requires a new preview');

        // ---- stale source: refused, then retry is gated by its own confirmation
        const stale = fixture('SIM-OR-200');
        await open(stale);
        await preview();
        await confirmCreate();
        await settled('Rejected');
        const refused = await json(admin, '/api/v1/operational-records/' + stale.id);
        assert.ok(!refused.jiraExists && refused.retryEligible, 'safe failure is retry-eligible');
        await button('Yeniden Dene').click();
        const retry = page.getByRole('dialog');
        await retry.getByText('Aktarımı Yeniden Dene', { exact: true }).waitFor();
        await page.waitForFunction(() => document.activeElement?.matches('[role="dialog"] input[type="checkbox"]'));
        assert.equal(await retry.getByRole('button', { name: 'Yeniden Dene', exact: true }).isDisabled(), true);
        await retry.getByText('Turuncu Hat kaydı açık kalacak.', { exact: true }).waitFor();
        await capture(page, out, 'negative-retry-confirmation');
        await page.keyboard.press('Escape');
        await retry.waitFor({ state: 'hidden' });
        assert.equal((await json(admin, '/api/v1/operational-records/' + stale.id)).retryCount, refused.retryCount, 'cancel sends no retry');
        await button('Yeniden Dene').click();
        await retry.getByRole('checkbox').check();
        await retry.getByRole('button', { name: 'Yeniden Dene', exact: true }).click();
        await retry.waitFor({ state: 'hidden' });
        await settled('Rejected');
        const retried = await json(admin, '/api/v1/operational-records/' + stale.id);
        assert.ok(!retried.jiraExists && retried.retryCount === refused.retryCount + 1);
        checks.push('Stale source: create refused with no key; retry needs its own confirmation, Escape sends nothing, confirmed retry is refused again with no key');

        // ---- unsupported types: review-only drafts, create stays disabled
        const typed = fixture('SIM-OR-400');
        await open(typed);
        for (const label of ['Sunucu İadesi/Emekliliği', 'Uygulama Kurulumu']) {
            await page.getByRole('combobox', { name: 'Talep türü (operatör beyanı)', exact: true }).selectOption({ label });
            await button('İnceleme taslağı hazırla').click();
            await settled('ReviewOnly');
            assert.equal(await button('Jira Kaydı Oluştur').isDisabled(), true);
        }
        assert.equal(history(typed.id, 'CreateRequested'), '0');
        await capture(page, out, 'negative-unsupported-type');
        checks.push('Unsupported types (retirement, installation): review-only, create disabled, no create requested');

        fs.writeFileSync(path.join(out, 'negative-results.json'), JSON.stringify({ seconds: (Date.now() - started) / 1000, checks }, null, 2));
        console.log(JSON.stringify({ checks }, null, 2));
    } catch (error) {
        fs.mkdirSync(out, { recursive: true });
        if (!page.isClosed()) {
            fs.writeFileSync(path.join(out, 'failure.html'), await page.content());
            await page.screenshot({ path: path.join(out, 'failure.png'), fullPage: true });
        }
        throw error;
    } finally {
        if (lead && leadOriginal) await setLeadRoles(leadOriginal).catch(e => console.error('role restore failed', e));
        await mode('pass').catch(() => {});
        await browser.close();
        await control.dispose();
        await admin.dispose();
    }
})().catch(error => { console.error(error); process.exitCode = 1; });
