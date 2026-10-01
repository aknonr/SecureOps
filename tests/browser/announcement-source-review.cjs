// Published loopback API/UI/Worker with Fixture adapters. Same first six args as announcements.cjs.
const assert = require('node:assert/strict'), fs = require('node:fs'), path = require('node:path'), crypto = require('node:crypto');
const { chromium, request } = require(process.argv[2]);
const { loopback, navigate, signIn, apiContext, json } = require('./journey-support.cjs');
const ui = loopback(process.argv[3]), api = loopback(process.argv[4]), deniedUi = loopback(process.argv[5]), out = path.resolve(process.argv[6]);
(async () => {
    fs.mkdirSync(out, { recursive: false });
    const browser = await chromium.launch({ executablePath: 'C:/Program Files/Google/Chrome/Application/chrome.exe', headless: true });
    const context = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1440, height: 900 } });
    await context.route('**/*', r => ['localhost', '127.0.0.1'].includes(new URL(r.request().url()).hostname) ? r.continue() : r.abort());
    const page = await context.newPage(), client = await apiContext(request, api), denied = await apiContext(request, api, 'team-lead');
    const id = crypto.randomUUID(), route = '/api/v1/announcements/' + id;
    const content = { ocoReference: 'OCO-SYNTHETIC', scope: 'Manual scope', subject: 'Source review ' + id.slice(0, 8),
        announcementDate: '2026-09-14', workStart: '2026-09-15T00:59:37+03:00', workEnd: '2026-09-15T02:00:59+03:00',
        description: 'Türkçe & <script>inert</script>', impact: 'Local impact', checks: 'Local checks', notes: '',
        to: ['manual@example.invalid'], cc: ['copy@example.invalid'], bannerRevision: 'bundle-v1', templateRevision: 'oco-table-v2',
        dateTextRevision: 'tr-v1', affectedServices: ['Manual service'] };
    const panel = () => page.getByRole('region', { name: 'Kaynak incelemesi', exact: true });
    const click = name => page.getByRole('button', { name, exact: true }).click();
    async function open() {
        await navigate(page, ui, 'announcements');
        if (await page.getByRole('button', { name: 'Şimdi değil', exact: true }).count()) await click('Şimdi değil');
        await click('Düzenle: ' + content.subject);
        await panel().waitFor();
        await page.locator('#source-profile option[value=NonProd]').waitFor({ state: 'attached' });
    }
    async function retrieve(profile, state = 'Succeeded') {
        await page.locator('#source-profile').selectOption(profile);
        await click('Kaydet ve kaynağı sorgula');
        await page.locator('[data-source-state=' + state + ']').waitFor();
        await panel().getByText('Dağıtım talebi alıcıları; nihai duyuru için onaylanmış gönderim listesi değildir.', { exact: true }).waitFor();
        return json(client, route + '/source/jobs');
    }
    async function apply(recipients) {
        const summary = panel().locator('summary').filter({ hasText: /^Çalışma yapılacak sistem/ });
        if (!await summary.evaluate(e => e.parentElement.open)) await summary.click();
        await panel().getByRole('checkbox', { name: 'Çalışma yapılacak sistem/uygulama uygula', exact: true }).check();
        await panel().getByRole('checkbox', { name: 'Servis değişikliklerini uygula', exact: true }).check();
        if (recipients) await panel().getByRole('checkbox', { name: 'Alıcı değişikliklerini uygula', exact: true }).check();
        await click('Seçili değişiklikleri uygula');
        await page.getByText('İncelenen değişiklikler yeni sürüme kaydedildi.', { exact: true }).waitFor();
    }
    try {
        await json(client, route + '?version=0', { method: 'PUT', data: content });
        await signIn(page, ui); await open();
        assert.equal(await page.locator('#source-profile option[value=ProdSingle]').evaluate(e => e.disabled), true);
        await page.locator('#announcement-Scope').fill('Unsaved local value');
        await page.locator('#source-profile').selectOption('NonProd');
        assert.equal(await page.getByRole('button', { name: 'Kaydet ve kaynağı sorgula', exact: true }).isEnabled(), true);
        const first = await retrieve('NonProd');
        assert.equal((await json(client, route)).scope, 'Unsaved local value', 'completion must not apply');
        const duplicateInput = { profile: 'NonProd', ocoReference: 'OCO-SYNTHETIC', submissionKey: 'combined-duplicate-key' };
        const duplicate = await json(client, route + '/source/jobs', { method: 'POST', data: duplicateInput, expectedStatus: 202 });
        assert.equal((await json(client, route + '/source/jobs', { method: 'POST', data: duplicateInput, expectedStatus: 202 })).jobId, duplicate.jobId);
        assert.equal((await client.post(route + '/source/jobs', { data: { ...duplicateInput, profile: 'Prod01' } })).status(), 409);
        await apply(true);
        let saved = await json(client, route);
        assert.equal(saved.affectedServices.length, 155); assert.equal(saved.workStart, content.workStart);
        await page.locator('iframe[title="Duyuru önizlemesi"]:visible').contentFrame().getByText('NonProd scope', { exact: true }).waitFor();
        fs.writeFileSync(path.join(out, 'reviewed.html'), await (await client.get(route + '?version=3&format=html')).text());
        assert.ok(saved.to.includes('manual@example.invalid')); assert.ok(saved.to.includes('remove@example.invalid'));
        const preparedEvent = page.waitForURL(/announcements\/preparations\/[a-f0-9-]+$/);
        await click('İncelemeye hazırla'); await preparedEvent;
        const preparedPath = '/api/v1/announcements/preparations/' + new URL(page.url()).pathname.split('/').at(-1);
        const prepared = await json(client, preparedPath);
        assert.equal(prepared.sourceReview.appliedJobId, first.jobId);
        const download = page.waitForEvent('download'); await click('Hazırlanan maili indir');
        const file = path.join(out, 'prepared.eml'); await (await download).saveAs(file);
        assert.deepEqual(fs.readFileSync(file), Buffer.from(prepared.email, 'base64'));
        saved = { ...saved, to: ['NonProd@example.invalid', 'manual@example.invalid', 'added@example.invalid'] };
        await json(client, route + '?version=3', { method: 'PUT', data: saved });
        await open(); await retrieve('Prod01'); await apply(false);
        assert.deepEqual((await json(client, route)).to, saved.to);
        await page.locator('iframe[title="Duyuru önizlemesi"]:visible').contentFrame().getByText('Prod01 scope', { exact: true }).waitFor();
        await retrieve('Prod01');
        const proposal = await json(client, route + '/source/jobs/' + (await json(client, route + '/source/jobs')).jobId + '/proposal');
        assert.ok(proposal.to.preservedManual.includes('added@example.invalid'));
        assert.ok(proposal.to.preservedRemoval.includes('remove@example.invalid'));
        await panel().getByText('Alıcı farkları', { exact: true }).click();
        assert.ok((await panel().innerText()).includes('added@example.invalid'));
        for (const width of [1440, 390]) {
            await page.setViewportSize({ width, height: 844 });
            await page.screenshot({ path: path.join(out, 'source-review-' + width + '.png'), fullPage: true });
            assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1), false);
            if (width === 390) await click('Önizleme');
            await page.getByText('Kaydedilmiş önizleme', { exact: true }).waitFor();
            const frame = page.locator('iframe[title="Duyuru önizlemesi"]:visible');
            await frame.scrollIntoViewIfNeeded();
            await frame.contentFrame().getByText('Prod01 scope', { exact: true }).waitFor();
            assert.equal(await frame.contentFrame().locator('img').evaluateAll(a => a.length === 6 && a.every(i => i.complete && i.naturalWidth > 0)), true);
            await page.screenshot({ path: path.join(out, 'reviewed-preview-' + width + '.png') });
        }
        const editTab = page.getByRole('group', { name: 'Duyuru görünümü', exact: true }).getByRole('button', { name: 'Düzenle', exact: true });
        await editTab.focus();
        await page.keyboard.press('Enter');
        await page.waitForFunction(() => document.querySelector('[aria-label="Duyuru görünümü"] button')?.getAttribute('aria-pressed') === 'true');
        await page.locator('#announcement-Scope').waitFor();
        await apply(true);
        const after = await json(client, route);
        assert.ok(after.to.includes('Prod01@example.invalid')); assert.ok(!after.to.includes('remove@example.invalid'));
        const stale = { jobId: proposal.jobId, expectedVersion: proposal.draftVersion, expectedOverrideVersion: proposal.overrideVersion, fields: ['Scope'], applyRecipients: false, applyAffectedServices: false };
        assert.equal((await client.post(route + '/source/apply', { data: stale })).status(), 409);
        assert.equal((await client.post(route + '/source/apply', { data: { ...stale, expectedVersion: 6 } })).status(), 409);
        await retrieve('Prod02', 'Partial');
        await panel().getByText('Etkilenen servisler (0)', { exact: true }).click();
        assert.equal(await panel().getByRole('checkbox', { name: 'Servis değişikliklerini uygula', exact: true }).isDisabled(), true);
        assert.ok((await panel().innerText()).includes('Candidate A')); assert.ok((await panel().innerText()).includes('MISSING'));
        await page.locator('#source-profile').selectOption('NonProd');
        await page.getByRole('button', { name: 'Kaydet ve kaynağı sorgula', exact: true }).click();
        await page.locator('[data-source-state=Queued], [data-source-state=Running]').waitFor();
        const running = await json(client, route + '/source/jobs');
        await click('Taslaklar'); await open();
        await page.locator('[data-source-state=Succeeded]').waitFor();
        assert.equal((await json(client, route + '/source/jobs')).jobId, running.jobId, 'resume must not submit');
        assert.equal((await json(client, route)).scope, after.scope, 'later retrieval must not apply');
        assert.deepEqual((await json(client, preparedPath)), prepared, 'immutable history after later reviews');
        await navigate(page, ui, preparedPath.replace('/api/v1/', ''));
        const retainedDownload = page.waitForEvent('download'); await click('Hazırlanan maili indir');
        const later = path.join(out, 'retained.eml'); await (await retainedDownload).saveAs(later);
        assert.deepEqual(fs.readFileSync(later), fs.readFileSync(file));
        await page.getByRole('link', { name: 'Geçmiş', exact: true }).click();
        await page.getByRole('row').filter({ hasText: content.subject }).waitFor();
        assert.equal((await denied.get(route + '/source/jobs')).status(), 403);
        await open(); await page.locator('#source-profile').selectOption('NonProd'); await click('Kaydet ve kaynağı sorgula');
        await page.locator('[data-source-state=Queued], [data-source-state=Running]').waitFor();
        const sessions = await json(client, '/api/v1/sessions/active');
        for (const session of sessions.items.filter(s => !s.isCurrent))
            await json(client, '/api/v1/sessions/revoke', { method: 'POST', data: { sessionId: session.sessionId, reason: 'Local source polling access-loss acceptance' } });
        await panel().waitFor({ state: 'hidden' });
        assert.equal(await page.locator('iframe').count(), 0);
        await signIn(page, deniedUi); await navigate(page, deniedUi, 'announcements');
        await page.locator('.so-problem').waitFor(); assert.equal(await panel().count(), 0);
        fs.writeFileSync(path.join(out, 'results.json'), JSON.stringify({ draftId: id, firstJob: first.jobId, resumedJob: running.jobId,
            preparedId: prepared.id, preparedSha256: crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex'), checks: 'explicit UI source review/apply, decline/switch/removals, partial, versions, duplicate, resume, immutable preparation, mobile, denial; no send' }, null, 2));
    } catch (error) { await page.screenshot({ path: path.join(out, 'failed.png'), fullPage: true }); throw error; }
    finally { await client.dispose(); await denied.dispose(); await context.close(); await browser.close(); }
})().catch(e => { console.error(e); process.exitCode = 1; });
