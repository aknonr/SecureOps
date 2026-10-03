// Jira-only submission states against paired Simulation hosts with in-memory persistence.
// Never contacts corporate providers: every key is a synthetic SIM-* value from the in-process client.
// node sdm-jira-submission.cjs <playwright path> <UI URL> <API URL> <evidence directory>
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium, request } = require(process.argv[2]);
const { loopback, navigate, signIn, capture, apiContext, json } = require('./journey-support.cjs');
const ui = loopback(process.argv[3]);
const api = loopback(process.argv[4]);
const out = path.resolve(process.argv[5]);

(async () => {
    const started = Date.now();
    const checks = [];
    const browser = await chromium.launch({ executablePath: process.env.WASAS_CHROME || 'C:/Program Files/Google/Chrome/Application/chrome.exe', headless: true });
    const admin = await apiContext(request, api);
    const context = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1440, height: 900 } });
    const page = await context.newPage();
    const panel = () => page.locator('[data-jira-submission]');
    const heading = () => panel().locator('h3');
    const button = name => page.getByRole('button', { name, exact: true });
    try {
        const records = await json(admin, '/api/v1/operational-records');
        const fixture = code => {
            const record = records.find(r => r.orCode === code);
            assert.ok(record?.simulationMode && !record.jiraIssueKey && !record.reconciliationRequired, code + ' must be a fresh synthetic fixture');
            return record;
        };
        await signIn(page, ui);

        async function open(record) {
            await navigate(page, ui, 'operational-records/' + record.id);
            await page.getByRole('heading', { name: record.orCode, exact: true }).waitFor();
            await panel().waitFor();
        }
        async function previewByKeyboard() {
            await button('Jira Taslağını Önizle').focus();
            await page.keyboard.press('Enter');
            await page.getByRole('heading', { name: 'Önizleme hazırlandı', exact: true }).waitFor();
            await heading().filter({ hasText: /^Gönderime hazır$/ }).waitFor();
        }
        async function openConfirmation() {
            await button('Jira Kaydı Oluştur').focus();
            await page.keyboard.press('Enter');
            const dialog = page.getByRole('dialog', { name: 'Jira Kaydı Oluştur', exact: true });
            await dialog.waitFor();
            // Initial focus is the confirmation statement, not a dialog button.
            await page.waitForFunction(() => document.activeElement?.matches('[role="dialog"] input[type="checkbox"]'));
            return dialog;
        }
        async function confirmByKeyboard(dialog) {
            const create = dialog.getByRole('button', { name: 'Oluştur', exact: true });
            assert.equal(await create.isDisabled(), true, 'Create must wait for the explicit statement');
            const ack = dialog.getByRole('checkbox');
            await page.keyboard.press('Space');
            assert.equal(await ack.isChecked(), true);
            await dialog.getByText('Onay verildi. Oluştur düğmesi etkin.', { exact: true }).waitFor();
            assert.equal(await create.isEnabled(), true);
            // Forward Tab reaches the now-enabled create button: statement -> Vazgeç -> Oluştur.
            await page.keyboard.press('Tab');
            await page.keyboard.press('Tab');
            assert.equal(await create.evaluate(e => e === document.activeElement), true, 'Tab must reach Oluştur');
            return create;
        }
        async function focusedOnOutcome() {
            await page.waitForFunction(() => document.activeElement?.closest('[data-jira-submission]')
                && document.activeElement.tagName === 'H3');
        }

        // ---- ready -> confirmation gate -> cancel -> confirm -> created (saved key)
        const happy = fixture('SIM-OR-100');
        assert.equal(happy.classification, 0, 'SIM-OR-100 is the synthetic ServerRequest fixture');
        await open(happy);
        await heading().filter({ hasText: 'Gönderime hazır değil' }).waitFor();
        await panel().getByText('Jira kaydı oluşturmadan önce önizleme alın.', { exact: true }).waitFor();
        await previewByKeyboard();
        for (const text of ['Seçili kaynak OR', 'Talep türü', 'Jira hedefi', 'Aktar ve kapat', 'Mükerrer koruma (sunucu aktarım anahtarı)']) {
            await panel().getByText(text, { exact: true }).waitFor();
        }
        await panel().getByText('Sunucu Talebi', { exact: true }).waitFor();
        await panel().locator('[data-source-outcome]').getByText('Turuncu Hat kaydı açık kalacak.', { exact: false }).waitFor();
        await panel().locator('[data-transfer-close]').getByText('Kullanılamaz.', { exact: false }).waitFor();
        await capture(page, out, 'submission-ready');
        checks.push('Ready: source OR, ServerRequest type, Jira destination/fields, source-open statement, transfer-and-close unavailable, transfer key');

        let dialog = await openConfirmation();
        await dialog.getByText('Turuncu Hat kaydı açık bırakılacak.', { exact: true }).waitFor();
        await dialog.getByText(`${happy.orCode} için tek bir Jira kaydı isteneceğini ve Turuncu Hat kaydının açık kalacağını onaylıyorum.`, { exact: true }).waitFor();
        assert.equal(await dialog.getByRole('button', { name: 'Oluştur', exact: true }).isDisabled(), true);
        await capture(page, out, 'submission-confirm-unchecked');
        await page.keyboard.press('Escape');
        await dialog.waitFor({ state: 'hidden' });
        assert.equal((await json(admin, '/api/v1/operational-records/' + happy.id)).jiraExists, false);
        await heading().filter({ hasText: /^Gönderime hazır$/ }).waitFor();
        checks.push('Confirmation: initial focus on statement, create disabled until ticked, Escape cancels with no create');

        dialog = await openConfirmation();
        const create = await confirmByKeyboard(dialog);
        await capture(page, out, 'submission-confirm-checked');
        await create.evaluate(e => { e.click(); e.click(); });
        await heading().filter({ hasText: /^Jira kaydı oluşturuldu · SIM-/ }).waitFor();
        await focusedOnOutcome();
        const saved = await json(admin, '/api/v1/operational-records/' + happy.id);
        assert.ok(saved.jiraExists && saved.jiraIssueKey && !saved.sourceCloseRequested);
        assert.equal(await panel().locator('[data-jira-key]').textContent(), saved.jiraIssueKey);
        await panel().getByText('Jira oluşturuldu. Turuncu Hat kaydı açık bırakıldı.', { exact: true }).waitFor();
        await panel().locator('[data-duplicate-guard]').waitFor();
        await panel().getByText('Simülasyon: bu anahtar sentetiktir ve gerçek bir Jira kaydı değildir.', { exact: true }).waitFor();
        assert.equal(await panel().locator('a[href]').count(), 0, 'No Jira link is composed without a server URL');
        assert.equal(await button('Jira Kaydı Oluştur').isDisabled(), true);
        await capture(page, out, 'submission-created');
        checks.push(`Created: saved key ${saved.jiraIssueKey} from re-read record, focus moved to outcome, double click produced one key, no invented link`);

        // ---- rejected: the source changed after preview; the server refuses before any Jira call
        const stale = fixture('SIM-OR-200');
        await open(stale);
        await previewByKeyboard();
        await confirmByKeyboard(await openConfirmation());
        await page.keyboard.press('Enter');
        await heading().filter({ hasText: 'Gönderim reddedildi' }).waitFor();
        await focusedOnOutcome();
        const refused = await json(admin, '/api/v1/operational-records/' + stale.id);
        assert.ok(!refused.jiraExists && !refused.reconciliationRequired);
        await capture(page, out, 'submission-rejected');
        checks.push('Rejected: server refusal shown with no saved key and no uncertainty');

        // ---- uncertain: unknown Jira outcome requires reconciliation, no resubmit or retry
        const unknown = fixture('SIM-OR-400');
        await open(unknown);
        await previewByKeyboard();
        await confirmByKeyboard(await openConfirmation());
        await page.keyboard.press('Enter');
        await heading().filter({ hasText: 'Sonuç belirsiz; mutabakat gerekli' }).waitFor();
        await page.getByRole('heading', { name: 'Jira sonucu doğrulanmalı', exact: true }).first().waitFor();
        assert.equal(await button('Jira Kaydı Oluştur').isDisabled(), true);
        assert.equal(await button('Yeniden Dene').isDisabled(), true);
        await panel().getByText('Yenileme yeni bir Jira isteği göndermez.', { exact: false }).first().waitFor();
        await capture(page, out, 'submission-uncertain');
        await button('Yenile').click();
        await heading().filter({ hasText: 'Sonuç belirsiz; mutabakat gerekli' }).waitFor();
        const uncertain = await json(admin, '/api/v1/operational-records/' + unknown.id);
        assert.ok(uncertain.reconciliationRequired && !uncertain.retryEligible && !uncertain.jiraIssueKey);
        checks.push('Uncertain: reconciliation guidance persists after refresh; create and retry disabled');

        // ---- unsupported type: review-only drafts can never be submitted
        const other = fixture('SIM-OR-500');
        await open(other);
        const select = page.getByRole('combobox', { name: 'Talep türü', exact: true });
        await select.focus();
        await select.selectOption({ label: 'Uygulama Kurulumu' });
        await button('İnceleme taslağı hazırla').click();
        await heading().filter({ hasText: 'İnceleme taslağı; gönderilemez' }).waitFor();
        await panel().getByText('Bu inceleme taslağı yayımlanamaz.', { exact: true }).waitFor();
        assert.equal(await page.getByRole('button', { name: 'Jira Kaydı Oluştur', exact: true }).count(), 1);
        assert.equal(await button('Jira Kaydı Oluştur').isDisabled(), true);
        await capture(page, out, 'submission-review-only');
        assert.equal((await json(admin, '/api/v1/operational-records/' + other.id)).jiraExists, false);
        checks.push('Unsupported type: review-only draft stays unpublishable with create disabled');

        fs.writeFileSync(path.join(out, 'submission-results.json'), JSON.stringify({ seconds: (Date.now() - started) / 1000, checks }, null, 2));
        console.log(JSON.stringify({ checks }, null, 2));
    } catch (error) {
        fs.mkdirSync(out, { recursive: true });
        if (!page.isClosed()) {
            fs.writeFileSync(path.join(out, 'failure.html'), await page.content());
            await page.screenshot({ path: path.join(out, 'failure.png'), fullPage: true });
        }
        throw error;
    } finally {
        await browser.close();
        await admin.dispose();
    }
})().catch(error => { console.error(error); process.exitCode = 1; });
