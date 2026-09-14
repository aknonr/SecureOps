// Reuses the guarded SQL/Simulation journey support; never contacts corporate providers.
// node sdm-jira-only.cjs <playwright path> <team-lead UI URL> <API URL> <database> <evidence directory>
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
    const started = Date.now();
    query(database, 'SELECT 1');
    const browser = await chromium.launch({ executablePath: process.env.WASAS_CHROME || 'C:/Program Files/Google/Chrome/Application/chrome.exe', headless: true });
    const admin = await apiContext(request, api);
    let actor = await apiContext(request, api, 'team-lead');
    const me = await json(actor, '/api/v1/access/me');
    const original = await json(admin, '/api/v1/access/users/' + me.userId);
    assert.equal(query(database, `SELECT COUNT(*) FROM security.Users WHERE UserId='${me.userId}' AND CorporateIdentity='demo:team-lead'`), '1');
    const checks = [];
    let context;
    let page;
    async function role(roles) {
        const current = await json(admin, '/api/v1/access/users/' + me.userId);
        await json(admin, `/api/v1/access/users/${me.userId}/roles`, { method: 'PUT',
            data: { roles, expectedVersion: current.version, reason: 'Synthetic isolated SDM review journey' } });
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
        await page.getByRole('heading', { name: 'Talep incelemesi', exact: true }).waitFor();
    }
    async function previewAndConfirm(record, name, confirm = true) {
        await detail(record);
        await page.getByRole('button', { name: 'Jira Taslağını Önizle', exact: true }).click();
        await page.getByRole('heading', { name: 'Önizleme hazırlandı', exact: true }).waitFor();
        await page.getByRole('button', { name: 'Jira Kaydı Oluştur', exact: true }).click();
        const dialog = page.getByRole('dialog', { name: 'Jira Kaydı Oluştur', exact: true });
        await dialog.waitFor();
        await page.waitForFunction(() => document.querySelector('[role="dialog"]')?.contains(document.activeElement));
        assert.equal(await dialog.getByRole('button', { name: 'Vazgeç', exact: true }).evaluate(e => e === document.activeElement), true);
        await dialog.getByText('Turuncu Hat kaydı açık bırakılacak.', { exact: true }).waitFor();
        await capture(page, out, name + '-confirmation');
        await dialog.getByRole('button', { name: 'Vazgeç', exact: true }).click();
        await dialog.waitFor({ state: 'hidden' });
        await page.waitForFunction(() => [...document.querySelectorAll('button')].some(e => e.textContent.trim() === 'Jira Kaydı Oluştur' && !e.disabled));
        assert.equal(await page.getByRole('button', { name: 'Jira Kaydı Oluştur', exact: true }).isEnabled(), true);
        if (!confirm) return;
        await page.getByRole('button', { name: 'Jira Kaydı Oluştur', exact: true }).click();
        await dialog.getByRole('button', { name: 'Oluştur', exact: true }).evaluate(e => { e.click(); e.click(); });
    }
    try {
        const records = process.argv[7] === '--verify-presentation'
            ? (await json(admin, '/api/v1/operational-records/stored?pageSize=100')).items
            : await json(admin, '/api/v1/operational-records');
        if (process.argv[7] === '--prepare-walkthrough') {
            const ready = records.find(r => r.orCode === 'SIM-OR-100');
            assert.ok(ready?.simulationMode && ready.classification === 0 && !ready.jiraIssueKey);
            await role(['JiraPublisher']);
            await previewAndConfirm(ready, 'owner-ready', false);
            const saved = await json(actor, '/api/v1/operational-records/' + ready.id);
            assert.equal(saved.jiraExists, false);
            fs.writeFileSync(path.join(out, 'walkthrough.json'), JSON.stringify({ seconds: (Date.now() - started) / 1000,
                url: new URL('operational-records/' + ready.id, ui).href, database,
                state: 'Preview verified; confirmation canceled; no Jira create attempted', sourceCloseEnabled: saved.sourceCloseEnabled }, null, 2));
            return;
        }
        if (process.argv[7] === '--verify-presentation') {
            const published = records.find(r => r.orCode === 'SIM-OR-100');
            assert.ok(published?.jiraIssueKey && !published.sourceCloseRequested);
            assert.equal(published.presentationState, 'SourceOpen');
            await role(['JiraPublisher']);
            await detail(published);
            assert.equal(await page.getByText('Kaynak Kaydı Tamamla', { exact: true }).count(), 0);
            await page.getByText('Jira oluşturuldu. Turuncu Hat kaydı açık bırakıldı.', { exact: true }).first().waitFor();
            await capture(page, out, 'jira-only-result');
            await navigate(page, ui, 'operational-records');
            await page.getByRole('textbox', { name: 'Kayıt ara', exact: true }).fill(published.orCode);
            await page.getByText(published.orCode, { exact: true }).waitFor();
            await capture(page, out, 'jira-only-list');
            const unknown = records.find(r => r.orCode === 'SIM-OR-400');
            assert.ok(unknown?.reconciliationRequired && !unknown.retryEligible);
            await detail(unknown);
            await page.getByRole('heading', { name: 'Jira sonucu doğrulanmalı', exact: true }).waitFor();
            assert.equal(await page.getByRole('button', { name: 'Yeniden Dene', exact: true }).isDisabled(), true);
            assert.equal((await actor.post(`/api/v1/operational-records/${unknown.id}/jira`, {
                headers: { 'Idempotency-Key': 'synthetic-restart-new-command' }
            })).status(), 409);
            const replay = await json(actor, `/api/v1/operational-records/${published.id}/jira`, { method: 'POST' });
            assert.equal(replay.jiraIssueKey, published.jiraIssueKey);
            for (const record of [published, unknown]) {
                assert.equal(query(database, `SELECT COUNT(*) FROM ops.OperationalRecordWorkflowHistory WHERE OperationalRecordId='${record.id}' AND WorkflowState='CreateRequested'`), '1');
            }
            fs.writeFileSync(path.join(out, 'restart-results.json'), JSON.stringify({ seconds: (Date.now() - started) / 1000,
                published: { id: published.id, key: published.jiraIssueKey, sourceCloseRequested: published.sourceCloseRequested },
                unknown: { id: unknown.id, reconciliationRequired: unknown.reconciliationRequired },
                checks: ['Fresh hosts reopen persisted key/source-open and unknown outcome; no second create'] }, null, 2));
            console.log('Persisted Jira-only result and list distinguish source open from pending close/completion');
            return;
        }
        const blocked = records.find(r => !r.jiraEligible && r.evaluatedAt);
        const fixture = code => {
            const record = records.find(r => r.orCode === code);
            assert.ok(record?.simulationMode && !record.jiraIssueKey && !record.reconciliationRequired);
            assert.equal(record.sourceCloseEnabled, false);
            return record;
        };
        const happy = fixture('SIM-OR-100');
        assert.equal(happy.classification, 0, 'The fixed happy fixture is a synthetic ServerRequest');
        assert.ok(blocked, 'Run the existing SQL evaluation test first');
        await role(['ReadOnly']);
        await detail(blocked);
        assert.equal(await page.locator('[data-sdm-review-type]').count(), 0);
        for (const suffix of ['jira-review', 'jira']) {
            assert.equal((await actor.post(`/api/v1/operational-records/${blocked.id}/${suffix}`, {
                data: suffix === 'jira-review' ? { requestType: 0, expectedVersion: blocked.version } : undefined
            })).status(), 403);
        }
        await capture(page, out, 'review-denied');
        checks.push('ReadOnly cannot review or publish; API 403 and absent controls');

        await role(['Operator']);
        await navigate(page, ui, 'operational-records');
        await page.getByRole('button', { name: 'Kaynağı yenile', exact: true }).click();
        await page.getByText('Sınırlı kaynak yenilemesi tamamlandı.', { exact: false }).waitFor();
        const beforeBrowse = query(database, "SELECT CHECKSUM_AGG(BINARY_CHECKSUM(SourceRecordId,UpdatedAt)) FROM ops.OperationalRecords");
        await page.getByRole('combobox', { name: 'Sayfa boyutu', exact: true }).selectOption('10');
        await page.getByRole('button', { name: 'Sonraki', exact: true }).click();
        await page.getByText('Sayfa 2 /', { exact: false }).waitFor();
        await page.getByRole('button', { name: 'Önceki', exact: true }).click();
        await page.getByRole('textbox', { name: 'Kayıt ara', exact: true }).fill('SIM-OR-');
        await page.getByText('5 kayıtlı eşleşme', { exact: true }).waitFor();
        await page.getByRole('combobox', { name: 'Sıralama', exact: true }).selectOption('code');
        await page.getByRole('combobox', { name: 'Sayfa boyutu', exact: true }).selectOption('10');
        await page.getByText('5 kayıtlı eşleşme', { exact: true }).waitFor();
        assert.equal(query(database, "SELECT CHECKSUM_AGG(BINARY_CHECKSUM(SourceRecordId,UpdatedAt)) FROM ops.OperationalRecords"), beforeBrowse);
        await capture(page, out, 'stored-browse');
        checks.push('Explicit UI source refresh; stored search/sort/paging do not mutate source or workflow versions');
        await detail(blocked);
        for (const [label, name] of [['Sunucu Talebi', 'server'], ['Uygulama Kurulumu', 'installation'], ['Sunucu İadesi/Emekliliği', 'retirement']]) {
            const select = page.getByRole('combobox', { name: 'Talep türü (operatör beyanı)', exact: true });
            await select.focus();
            assert.equal(await select.evaluate(e => e === document.activeElement), true);
            await select.selectOption({ label });
            await page.getByText('Bu inceleme taslağı yayımlanamaz.', { exact: true }).waitFor({ state: 'hidden' });
            await page.getByRole('button', { name: 'İnceleme taslağı hazırla', exact: true }).click();
            await page.getByText('Bu inceleme taslağı yayımlanamaz.', { exact: true }).waitFor();
            if (name === 'installation') await page.getByText('Uygulama Kurulumu için Jira etiket eşlemesi doğrulanmadı.', { exact: false }).waitFor();
            assert.equal(await page.getByRole('button', { name: 'Jira Kaydı Oluştur', exact: true }).count(), 0);
            await capture(page, out, 'review-' + name);
        }
        const retained = await json(actor, '/api/v1/operational-records/' + blocked.id);
        assert.equal(retained.jiraEligible, false);
        assert.equal(retained.version, blocked.version);
        assert.equal((await actor.post(`/api/v1/operational-records/${blocked.id}/jira`)).status(), 403);
        assert.ok(blocked.title.startsWith('Synthetic'));
        query(database, `UPDATE ops.OperationalRecords SET UpdatedAt=DATEADD(second,1,UpdatedAt) WHERE OperationalRecordId='${blocked.id}'`);
        await page.getByRole('button', { name: 'İnceleme taslağı hazırla', exact: true }).click();
        await page.getByText('Kaynak kayıt değişmiş', { exact: true }).waitFor();
        assert.equal(await page.getByRole('combobox', { name: 'Talep türü (operatör beyanı)', exact: true }).inputValue(), 'ServerRetirement');
        await capture(page, out, 'review-version-conflict');
        await page.getByRole('button', { name: 'İnceleme taslağı hazırla', exact: true }).click();
        await page.getByText('Bu inceleme taslağı yayımlanamaz.', { exact: true }).waitFor();
        checks.push('Three declarations render; choice changes invalidate draft; blockers remain and Operator cannot publish');
        checks.push('Real SQL version conflict preserves declaration; fresh review recovers without publication');

        await role(['JiraPublisher']);
        await previewAndConfirm(happy, 'jira-only');
        await page.getByText('Jira oluşturuldu. Turuncu Hat kaydı açık bırakıldı.', { exact: true }).first().waitFor();
        const created = await json(actor, '/api/v1/operational-records/' + happy.id);
        assert.equal(created.workflowState, 7);
        assert.equal(created.sourceCloseRequested, false);
        assert.equal(created.retryEligible, false);
        assert.ok(created.jiraIssueKey);
        await capture(page, out, 'jira-only-result');
        const replay = await json(actor, `/api/v1/operational-records/${happy.id}/jira`, { method: 'POST' });
        const retry = await json(actor, `/api/v1/operational-records/${happy.id}/retry`, { method: 'POST' });
        assert.equal(replay.jiraIssueKey, created.jiraIssueKey);
        assert.equal(retry.jiraIssueKey, created.jiraIssueKey);
        assert.equal(query(database, `SELECT COUNT(*) FROM ops.OperationalRecordWorkflowHistory WHERE OperationalRecordId='${happy.id}' AND WorkflowState='JiraCreated'`), '1');
        assert.equal(query(database, `SELECT COUNT(*) FROM ops.OperationalRecordWorkflowHistory WHERE OperationalRecordId='${happy.id}' AND WorkflowState IN ('ClosingOperationalRecord','OperationalRecordCloseFailed','Completed')`), '0');
        checks.push('Publisher confirmation focus/cancel/double click; Jira-only persists one key, replay/retry perform no close stage');

        const unknown = fixture('SIM-OR-400');
        await previewAndConfirm(unknown, 'jira-unknown');
        await page.getByRole('heading', { name: 'Jira sonucu doğrulanmalı', exact: true }).waitFor();
        const uncertain = await json(actor, '/api/v1/operational-records/' + unknown.id);
        assert.equal(uncertain.reconciliationRequired, true);
        assert.equal(uncertain.retryEligible, false);
        assert.equal((await actor.post(`/api/v1/operational-records/${unknown.id}/retry`)).status(), 409);
        await capture(page, out, 'jira-unknown-result');
        checks.push('Ambiguous Simulation result retains reconciliation block with no automatic retry');
        fs.writeFileSync(path.join(out, 'jira-only-results.json'), JSON.stringify({ seconds: (Date.now() - started) / 1000,
            published: { id: created.id, key: created.jiraIssueKey, sourceCloseRequested: created.sourceCloseRequested }, checks }, null, 2));
        console.log(JSON.stringify({ checks }, null, 2));
    } finally {
        const latest = await json(admin, '/api/v1/access/users/' + me.userId);
        await json(admin, `/api/v1/access/users/${me.userId}/roles`, { method: 'PUT', data: {
            roles: original.roles, expectedVersion: latest.version, reason: 'Restore synthetic journey roles' } });
        await browser.close();
        await actor.dispose();
        await admin.dispose();
    }
})().catch(error => { console.error(error); process.exitCode = 1; });
