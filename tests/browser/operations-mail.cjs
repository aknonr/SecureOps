// Real loopback API/UI/console Worker and SMTP sink; no corporate destination is permitted.
const assert = require('node:assert/strict'), fs = require('node:fs'), path = require('node:path'), crypto = require('node:crypto');
const { chromium, request } = require(process.argv[2]);
const { loopback, navigate, signIn, apiContext, json } = require('./journey-support.cjs');
const ui = loopback(process.argv[3]), api = loopback(process.argv[4]);
const out = path.resolve(process.argv[5]), sink = path.resolve(process.argv[6]), resumed = process.argv[7] === 'resumed';
const files = () => fs.readdirSync(sink).filter(x => x.endsWith('.eml'));
const mode = value => fs.writeFileSync(path.join(sink, 'mode.txt'), value);
async function until(probe, seconds = 30) {
    const end = Date.now() + seconds * 1000;
    while (Date.now() < end) { const result = await probe(); if (result) return result; await new Promise(r => setTimeout(r, 250)); }
    throw Error('Acceptance condition timed out');
}
(async () => {
    if (!resumed) fs.mkdirSync(out, { recursive: false });
    const started = Date.now();
    const browser = await chromium.launch({ executablePath: 'C:/Program Files/Google/Chrome/Application/chrome.exe', headless: true });
    const context = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1440, height: 900 } });
    await context.route('**/*', r => ['localhost', '127.0.0.1'].includes(new URL(r.request().url()).hostname) ? r.continue() : r.abort());
    const page = await context.newPage(), client = await apiContext(request, api), denied = await apiContext(request, api, 'team-lead');
    const click = name => page.getByRole('button', { name, exact: true }).click();
    const history = id => json(client, '/api/v1/announcements/' + id + '/mail');
    const previews = data => json(client, '/api/v1/announcements/mail/preview', { method: 'POST', data });
    const confirm = p => json(client, '/api/v1/announcements/mail/confirm', { method: 'POST', data: { previewToken: p.previewToken } });
    const state = (id, expected, kind) => until(async () => (await history(id)).find(x => x.state === expected && (!kind || x.kind === kind)));
    async function prepare(label) {
        const id = crypto.randomUUID(), reference = '/api/v1/announcements/' + id;
        const content = { ocoReference: 'OCO-SYNTHETIC', scope: 'Synthetic local scope', subject: label + ' ' + id.slice(0, 8),
            announcementDate: '2026-09-17', workStart: '2026-09-18T10:00:00+03:00', workEnd: '2026-09-18T11:00:00+03:00',
            description: 'Türkçe & inceleme <b> düz metin', impact: 'Synthetic impact', checks: 'Synthetic checks', notes: '',
            to: ['reader@example.invalid'], cc: ['copy@example.invalid'], bannerRevision: 'bundle-v1', templateRevision: 'oco-table-v2',
            dateTextRevision: 'tr-v1', restartStart: null, restartEnd: null,
            affectedServices: Array.from({ length: 155 }, (_, i) => `Sentetik servis ${i + 1} - Türkçe & inceleme`) };
        await json(client, reference + '?version=0', { method: 'PUT', data: content });
        await navigate(page, ui, 'announcements');
        if (await page.getByRole('button', { name: 'Şimdi değil', exact: true }).count()) await click('Şimdi değil');
        await click('Düzenle: ' + content.subject);
        await click('İncelemeye hazırla');
        await page.waitForURL(/announcements\/preparations\/[a-f0-9-]+$/);
        const preparationId = new URL(page.url()).pathname.split('/').at(-1);
        const snapshot = await json(client, '/api/v1/announcements/preparations/' + preparationId);
        return { id, reference, content, preparationId, snapshot };
    }
    async function capture(name) {
        for (const theme of ['light', 'dark']) {
            await page.evaluate(value => window.secureOpsAppearance.write(value), theme);
            await navigate(page, ui, new URL(page.url()).pathname);
            await page.locator('.mail-command').waitFor();
            for (const width of [1440, 1366, 390]) {
                await page.setViewportSize({ width, height: width === 1366 ? 768 : 900 });
                await page.locator('.mail-command').scrollIntoViewIfNeeded();
                await page.locator('iframe').first().contentFrame().locator('img').first().waitFor();
                await until(() => page.locator('iframe').first().contentFrame().locator('img').evaluateAll(a => a.length === 6 && a.every(i => i.complete && i.naturalWidth > 0)));
                await page.waitForTimeout(300);
                assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1), false, name + ' overflow');
                await page.screenshot({ path: path.join(out, `${name}-${theme}-${width}.png`), fullPage: true });
            }
        }
        await page.setViewportSize({ width: 1440, height: 900 });
    }
    try {
        await signIn(page, ui);
        if (resumed) {
            const checkpoint = JSON.parse(fs.readFileSync(path.join(out, 'checkpoint.json')));
            const before = files().length;
            await state(checkpoint.unknown.id, 'Unknown', 'Send');
            await navigate(page, ui, 'announcements/preparations/' + checkpoint.unknown.preparationId);
            await page.getByText('Duyuru · Sonuç belirsiz', { exact: true }).waitFor();
            assert.equal(await page.getByRole('button', { name: 'Duyuruyu gönder', exact: true }).count(), 0);
            assert.deepEqual(await json(client, '/api/v1/announcements/preparations/' + checkpoint.success.preparationId), checkpoint.success.snapshot);
            const repeat = await previews({ preparationId: checkpoint.unknown.preparationId, kind: 'Send' });
            assert.equal((await client.post('/api/v1/announcements/mail/confirm', { data: { previewToken: repeat.previewToken } })).status(), 409);
            await new Promise(r => setTimeout(r, 2000));
            assert.equal(files().length, before, 'restart/replay must not repeat SMTP');
            await capture('after-restart');
            fs.writeFileSync(path.join(out, 'restart-result.json'), JSON.stringify({ seconds: (Date.now() - started) / 1000, messages: before, checks: 'SQL state, immutable bytes, unknown block and no second SMTP after real Worker restart' }, null, 2));
            return;
        }
        mode('Accepted');
        const success = await prepare('SMTP acceptance');
        const download = page.waitForEvent('download'); await click('Hazırlanan maili indir');
        await (await download).saveAs(path.join(out, 'representative.eml'));
        fs.writeFileSync(path.join(out, 'saved-preview.html'), success.snapshot.html);
        fs.writeFileSync(path.join(out, 'saved-draft.json'), JSON.stringify(success.content));
        assert.deepEqual(fs.readFileSync(path.join(out, 'representative.eml')), Buffer.from(success.snapshot.email, 'base64'));
        const frame = page.locator('iframe').first();
        assert.equal(await frame.contentFrame().locator('img').evaluateAll(a => a.length === 6 && a.every(i => i.complete && i.naturalWidth > 0)), true);
        const initial = files().length;
        await click('Kendime deneme gönder');
        const review = page.getByRole('region', { name: 'Gönderim onayı' });
        await review.waitFor();
        await until(() => review.evaluate(e => document.activeElement === e));
        assert.ok((await review.innerText()).includes('actor@example.invalid'));
        assert.ok(!(await review.innerText()).includes('reader@example.invalid'));
        await click('Vazgeç'); assert.equal(files().length, initial);
        await click('Kendime deneme gönder'); await click('Denemeyi onayla');
        const self = await state(success.id, 'Accepted', 'SelfTest');
        assert.deepEqual(self.to, ['actor@example.invalid']); assert.deepEqual(self.cc, []);
        assert.equal(files().length, initial + 1);
        const selfFile = files().at(-1);
        const selfEnvelope = JSON.parse(fs.readFileSync(path.join(sink, selfFile.replace('.eml', '.json'))));
        assert.deepEqual(selfEnvelope.recipients, ['<actor@example.invalid>']);
        assert.deepEqual(await json(client, success.reference), success.content);
        await click('Kayıtlı sonucu kontrol et');
        await click('Duyuruyu gönder');
        assert.ok((await review.innerText()).includes('reader@example.invalid'));
        assert.ok((await review.innerText()).includes('copy@example.invalid'));
        await page.getByRole('button', { name: 'Gönderimi onayla', exact: true }).dblclick();
        await state(success.id, 'Accepted', 'Send');
        assert.equal(files().length, initial + 2, 'double click creates one distribution');
        await click('Kayıtlı sonucu kontrol et'); await capture('accepted');
        assert.equal((await denied.post('/api/v1/announcements/mail/preview', { data: { preparationId: success.preparationId, kind: 'SelfTest' } })).status(), 403);
        // Same protected command replay returns the recorded result, not another SMTP invocation.
        const replay = await previews({ preparationId: success.preparationId, kind: 'SelfTest' });
        const confirmations = await Promise.all([confirm(replay), confirm(replay)]);
        assert.equal(confirmations[0].commandId, confirmations[1].commandId);
        await state(success.id, 'Accepted', 'SelfTest');
        await until(() => files().length === initial + 3);
        await confirm(replay); await new Promise(r => setTimeout(r, 500));
        assert.equal(files().length, initial + 3);
        // Respect the real six-per-minute command limit, including the deliberate double click.
        await new Promise(r => setTimeout(r, 61000));
        const unknown = await prepare('Ambiguous SMTP'); mode('Unknown');
        await click('Duyuruyu gönder'); await click('Gönderimi onayla');
        const uncertain = await state(unknown.id, 'Unknown', 'Send');
        await click('Kayıtlı sonucu kontrol et');
        await page.getByText('Duyuru · Sonuç belirsiz', { exact: true }).waitFor();
        assert.equal(await page.getByRole('button', { name: 'Duyuruyu gönder', exact: true }).count(), 0);
        assert.equal(files().length, initial + 4);
        await capture('uncertain');
        fs.writeFileSync(path.join(out, 'checkpoint.json'), JSON.stringify({ success, unknown, uncertain, initial }, null, 2));
        fs.writeFileSync(path.join(out, 'result.json'), JSON.stringify({ seconds: (Date.now() - started) / 1000, messages: files().length - initial,
            checks: 'original image render/download, keyboard review focus, cancel no send, self-only, unchanged audience, UI double click, protected replay, denied, DATA interruption Unknown, two themes/three widths; synthetic SMTP only' }, null, 2));
    } catch (error) { await page.screenshot({ path: path.join(out, 'failed.png'), fullPage: true }); throw error; }
    finally { mode('Accepted'); await client.dispose(); await denied.dispose(); await context.close(); await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
