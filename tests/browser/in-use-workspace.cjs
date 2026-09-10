// node in-use-workspace.cjs <playwright-core> <ui-loopback> <api-loopback> <evidence-dir> before|after|denied|unavailable
// Published local Demo hosts only. After mode uses the approved isolated SQL and synthetic fixtures.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium, request } = require(process.argv[2]);
const { loopback, navigate, signIn, capture, apiContext, json } = require('./journey-support.cjs');
const ui = loopback(process.argv[3]), api = loopback(process.argv[4]), out = path.resolve(process.argv[5]);
const mode = process.argv[6];
assert.ok(['before', 'effort-before', 'after', 'mapped', 'denied', 'unavailable'].includes(mode));
async function tour(page, surface) {
    const opener = page.locator(`#resource-guide-replay-${surface}`);
    await opener.focus(); await opener.press('Enter');
    const guide = page.getByRole('dialog', { name: 'Kullanım Rehberi', exact: true });
    await guide.waitFor();
    for (let index = 0; index < 3; index++) {
        await page.waitForFunction(() => document.querySelector('.so-resource-guide')?.dataset.targetAvailable === 'true');
        if (index < 2) await guide.getByRole('button', { name: 'İleri', exact: true }).click();
    }
    await guide.press('ArrowLeft'); await guide.press('ArrowRight'); await guide.press('Escape');
    await guide.waitFor({ state: 'hidden' });
    await page.waitForFunction(id => document.activeElement?.id === id, `resource-guide-replay-${surface}`);
}
(async () => {
    fs.mkdirSync(out, { recursive: true });
    const browser = await chromium.launch({ executablePath: process.env.WASAS_CHROME || 'C:/Program Files/Google/Chrome/Application/chrome.exe', headless: true });
    const client = await apiContext(request, api);
    const errors = [], requests = [];
    let page;
    try {
        const context = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1440, height: 900 }, reducedMotion: 'reduce' });
        await context.route('**/*', route => ['127.0.0.1', 'localhost'].includes(new URL(route.request().url()).hostname) ? route.continue() : route.abort());
        page = await context.newPage();
        page.on('pageerror', e => errors.push(e.message));
        page.on('request', r => requests.push(new URL(r.url()).pathname));
        await signIn(page, ui);
        if (mode === 'mapped') {
            const stored = await json(client, '/api/v1/in-use?search=OR-MAPPED-');
            assert.equal(stored.total, 1, 'Use the fresh isolated semantic SQL fixture');
            const record = stored.items[0], me = await json(client, '/api/v1/access/me');
            assert.equal(record.source.synthetic, true);
            assert.equal(record.source.serviceItemsState, 'Observed');
            const assigned = await json(client, `/api/v1/in-use/${record.id}/assignment`, { method: 'PUT', data: { expectedVersion: record.version, assigneeId: null, reason: 'Synthetic optional assignment review' } });
            await json(client, `/api/v1/in-use/${record.id}/draft`, { method: 'PUT', data: { expectedVersion: assigned.version, sourceVersion: assigned.sourceVersion,
                answers: assigned.source.servers.flatMap(s => ['InternetOut', 'InternetIn', 'Microsegmented'].map(check => ({ serverId: s.id, check, value: 'Unknown', evidence: '' }))), notes: '' } });
            await navigate(page, ui, `in-use/${record.id}`);
            const table = page.getByLabel('Servis öğesi tablosu', { exact: true });
            await table.waitFor();
            assert.equal(await table.locator('tbody tr').count(), 4);
            assert.match(await table.innerText(), /mapped-synthetic-0/);
            assert.match(await table.innerText(), /STAGING/);
            assert.match(await table.innerText(), /PROD/);
            assert.match(await table.innerText(), /Synthetic service 3/);
            assert.equal(await page.locator('select[aria-label*="Internet"],select[aria-label*="Microsegmented"]').count(), 3);
            for (const check of ['InternetOut', 'InternetIn', 'Microsegmented']) await page.getByLabel(`5000 ${check}`, { exact: true }).selectOption('No');
            await page.locator('.so-inuse-bulk summary').click();
            for (const box of await page.locator('.so-inuse-bulk input[type=checkbox]').all()) await box.check();
            await page.getByRole('button', { name: 'Değişiklikleri göster', exact: true }).click();
            await page.waitForFunction(() => document.querySelectorAll('.so-inuse-bulk li').length === 9);
            await page.getByRole('button', { name: 'Gösterilen değişiklikleri onayla', exact: true }).click();
            await page.getByRole('button', { name: 'Taslağı kaydet', exact: true }).click();
            await page.getByText('Yerel inceleme taslağı kaydedildi.', { exact: true }).waitFor();
            const saved = await json(client, `/api/v1/in-use/${record.id}`);
            assert.equal(saved.assigneeId, null, 'Review does not require or silently create assignment');
            assert.equal(saved.draft.reviewedBy, me.userId);
            assert.deepEqual(saved.source, record.source);
            await page.getByRole('button', { name: 'Excel önizleme', exact: true }).click();
            await page.getByLabel('Excel sayfası').waitFor();
            assert.match(await page.getByLabel('Excel hücre önizlemesi').innerText(), /mapped-synthetic-3/);
            const downloadEvent = page.waitForEvent('download');
            await page.getByRole('button', { name: "WASAS'a arşivle ve indir", exact: true }).click();
            const download = await downloadEvent;
            await download.saveAs(path.join(out, download.suggestedFilename()));
            for (const theme of ['dark', 'light']) {
                await page.evaluate(value => localStorage.setItem('wasas.appearance', value), theme);
                await navigate(page, ui, `in-use/${record.id}`);
                await page.getByLabel('Servis öğesi tablosu', { exact: true }).waitFor();
                await capture(page, out, `mapped-${theme}`);
            }
            await navigate(page, ui, 'in-use');
            await page.getByLabel('In Use kayıt ara').fill('OR-MAPPED-');
            await page.waitForFunction(() => document.querySelectorAll('.so-inuse-records li').length === 1);
            assert.match(await page.locator('.so-inuse-records').innerText(), /4 gözlenen kayıt; tamlık doğrulanmadı/);
            assert.equal(requests.some(p => /upload|bpm|jira/.test(p)), false);
        } else if (mode === 'denied') {
            await navigate(page, ui, 'in-use');
            await page.getByText('In Use erişimi gerekli', { exact: true }).waitFor();
            assert.equal(await page.locator('a[href="in-use"]').count(), 0);
            assert.equal(await page.getByLabel('In Use kayıt ara').count(), 0);
            await capture(page, out, 'after-denied');
        } else if (mode === 'unavailable') {
            await navigate(page, ui, 'in-use');
            await page.getByLabel('In Use kayıt ara').waitFor();
            await page.waitForFunction(() => !document.querySelector('#resource-guide-replay-inuse-list')?.disabled);
            const signal = path.join(out, 'unavailable-continue.signal');
            fs.writeFileSync(signal, 'waiting');
            await new Promise((resolve, reject) => {
                const watcher = fs.watch(out, (_, file) => {
                    if (file === path.basename(signal) && fs.readFileSync(signal, 'utf8').trim() === 'continue') {
                        clearTimeout(deadline); watcher.close(); resolve();
                    }
                });
                const deadline = setTimeout(() => { watcher.close(); reject(new Error('Local outage signal not received')); }, 60000);
                console.log('READY: stop only the local test API, then set unavailable-continue.signal to continue.');
            });
            await page.getByLabel('In Use kayıt ara').fill('OR-DEMO-INUSE');
            await page.getByText('Servise ulaşılamıyor', { exact: true }).waitFor();
            await capture(page, out, 'after-unavailable');
        } else if (mode === 'effort-before') {
            await json(client, '/api/v1/in-use/refresh', { method: 'POST', data: { commandId: require('node:crypto').randomUUID() } });
            const stored = await json(client, '/api/v1/in-use');
            const record = await json(client, `/api/v1/in-use/${stored.items.find(r => r.source.code === 'OR-DEMO-INUSE-01').id}`);
            await navigate(page, ui, `in-use/${record.id}`);
            await page.getByText('Sorumluluk ve kaynak kanıtı', { exact: true }).waitFor();
            for (const summary of await page.locator('.so-inuse-server summary').all()) await summary.click();
            assert.equal(await page.locator('.so-inuse-answer select').count(), 18);
            await capture(page, out, 'before-review');
        } else if (mode === 'before') {
            await navigate(page, ui, 'operational-records');
            assert.equal(await page.locator('a[href="in-use"]').count(), 0);
            await capture(page, out, 'before-rc6.13-no-inuse');
        } else {
            await navigate(page, ui, 'operational-records');
            await page.getByRole('button', { name: 'Kaynağı yenile', exact: true }).click();
            await page.getByText('Sınırlı kaynak yenilemesi tamamlandı.', { exact: false }).waitFor();
            const operational = await json(client, '/api/v1/operational-records/stored');
            const requester = operational.items.find(r => r.requester)?.requester;
            assert.ok(requester);
            await page.getByText(`Talep eden: ${requester}`, { exact: true }).first().waitFor();
            await capture(page, out, 'requester-list-light');
            await page.evaluate(() => localStorage.setItem('wasas.appearance', 'dark'));
            await navigate(page, ui, 'in-use');
            await page.waitForFunction(() => document.documentElement.classList.contains('so-dark'));
            await page.getByLabel('In Use kayıt ara', { exact: true }).waitFor();
            await page.waitForFunction(() => getComputedStyle(document.querySelector('.so-inuse-filters')).display === 'grid');
            const beforeTour = await json(client, '/api/v1/in-use');
            for (let repeat = 0; repeat < 3; repeat++) await tour(page, 'inuse-list');
            assert.equal(await page.evaluate(() => window.secureOpsResourceGuide.show('inuse-list', document.createElement('aside'))), false);
            assert.deepEqual(await json(client, '/api/v1/in-use'), beforeTour, 'Tour must not refresh or save state');
            await page.getByRole('button', { name: 'In Use kaynağını yenile', exact: true }).click();
            await page.getByText('Sınırlı kaynak okuması tamamlandı.', { exact: true }).waitFor();
            await page.getByLabel('In Use kayıt ara', { exact: true }).fill('OR-DEMO-INUSE');
            await page.getByLabel('In Use görünüm').focus();
            await page.waitForFunction(() => document.querySelectorAll('.so-inuse-records li').length === 2);
            await page.getByLabel('In Use sayfa boyutu').selectOption('10');
            await page.getByLabel('In Use kayıt ara', { exact: true }).fill('INUSE-PAGE-');
            await page.waitForFunction(() => document.querySelectorAll('.so-inuse-records li').length === 10
                && [...document.querySelectorAll('.so-inuse-records li > div:first-child > a')].every(a => a.textContent.includes('INUSE-PAGE-')));
            const firstPage = await page.locator('.so-inuse-records li > div:first-child > a').allTextContents();
            await page.getByRole('button', { name: 'Sonraki In Use sayfası', exact: true }).click();
            await page.waitForFunction(first => [...document.querySelectorAll('.so-inuse-records li > div:first-child > a')].length > 0
                && [...document.querySelectorAll('.so-inuse-records li > div:first-child > a')].every(a => !first.includes(a.textContent)), firstPage);
            await page.getByRole('button', { name: 'Önceki In Use sayfası', exact: true }).click();
            await page.getByLabel('In Use kayıt ara', { exact: true }).fill('OR-DEMO-INUSE');
            await page.waitForFunction(() => document.querySelectorAll('.so-inuse-records li').length === 2);
            await page.getByLabel('In Use görünüm').selectOption('unassigned');
            await page.getByLabel('In Use görünüm').selectOption('all');
            await page.waitForFunction(() => !document.querySelector('#resource-guide-replay-inuse-list')?.disabled);
            await capture(page, out, 'after-list-dark');
            const stored = await json(client, '/api/v1/in-use?search=OR-DEMO-INUSE');
            assert.equal(stored.total, 2);
            const record = await json(client, `/api/v1/in-use/${stored.items.find(r => r.source.code === 'OR-DEMO-INUSE-01').id}`);
            await page.getByRole('link', { name: 'OR-DEMO-INUSE-01', exact: true }).click();
            await page.getByText('Sorumluluk ve kaynak kanıtı', { exact: true }).waitFor();
            await tour(page, 'inuse-review');
            assert.deepEqual(await json(client, `/api/v1/in-use/${record.id}`), record);
            const me = await json(client, '/api/v1/access/me');
            if (!await page.locator('.so-inuse-assignment').evaluate(e => e.open)) await page.locator('.so-inuse-assignment summary').click();
            await page.getByLabel('In Use inceleyicisi', { exact: true }).selectOption(me.userId);
            await page.getByLabel('Atama gerekçesi', { exact: true }).fill('Synthetic browser review assignment');
            await page.getByRole('button', { name: 'Atamayı kaydet', exact: true }).click();
            await page.getByText('Yerel atama kaydedildi.', { exact: true }).waitFor();
            const assigned = await json(client, `/api/v1/in-use/${record.id}`);
            await json(client, `/api/v1/in-use/${record.id}/draft`, { method: 'PUT', data: { expectedVersion: assigned.version, sourceVersion: assigned.sourceVersion,
                answers: assigned.source.servers.flatMap(s => ['InternetOut', 'InternetIn', 'Microsegmented'].map(check => ({ serverId: s.id, check, value: 'Unknown', evidence: '' }))), notes: '' } });
            await navigate(page, ui, `in-use/${record.id}`);
            await page.getByLabel('Yanıtlanacak sunucu', { exact: true }).waitFor();
            assert.equal(await page.locator('select[aria-label*="Internet"],select[aria-label*="Microsegmented"]').count(), 3);
            assert.equal(await page.getByLabel('İnceleme notu', { exact: true }).count(), 0);
            await page.getByLabel('demo-server-01 InternetOut', { exact: true }).selectOption('Yes');
            await page.getByRole('button', { name: 'Taslağı kaydet', exact: true }).click();
            await page.getByText('Yerel inceleme taslağı kaydedildi.', { exact: true }).waitFor();
            await page.getByRole('button', { name: 'Excel önizleme', exact: true }).click();
            await page.getByLabel('Excel sayfası').waitFor();
            await page.getByRole('button', { name: "WASAS'a arşivle ve indir", exact: true }).click();
            await page.getByRole('alert').filter({ hasText: 'demo-server-01: İnternetten sunucuya erişim' }).waitFor();
            await page.waitForFunction(() => document.activeElement?.getAttribute('aria-label') === 'demo-server-01 InternetIn');
            await capture(page, out, 'after-validation');
            await page.getByLabel('demo-server-01 InternetIn', { exact: true }).selectOption('No');
            await page.getByLabel('demo-server-01 Microsegmented', { exact: true }).selectOption('Yes');
            await page.locator('.so-inuse-bulk summary').click();
            await page.locator('.so-inuse-bulk input[type=checkbox]').first().check();
            await page.getByRole('button', { name: 'Değişiklikleri göster', exact: true }).click();
            await page.locator('.so-inuse-bulk li').first().waitFor();
            assert.equal(await page.locator('.so-inuse-bulk li').count(), 3);
            await capture(page, out, 'after-bulk-preview');
            await page.getByRole('button', { name: 'Gösterilen değişiklikleri onayla', exact: true }).click();
            await page.getByText('Toplu cevaplar taslakta. Kaydetmeden önce sunucu bazında inceleyin.', { exact: true }).waitFor();
            assert.equal(await page.getByRole('button', { name: 'Excel önizleme', exact: true }).isDisabled(), true);
            await page.setViewportSize({ width: 390, height: 844 });
            await page.getByLabel('Yanıtlanacak sunucu', { exact: true }).selectOption('demo-server-02');
            await page.getByLabel('demo-server-02 InternetOut', { exact: true }).selectOption('No');
            await page.getByRole('button', { name: 'Taslağı kaydet', exact: true }).click();
            await page.getByText('Yerel inceleme taslağı kaydedildi.', { exact: true }).waitFor();
            let saved = await json(client, `/api/v1/in-use/${record.id}`);
            assert.deepEqual(saved.draft.answers.filter(a => a.check === 'InternetOut').map(a => a.value), ['Yes', 'No']);
            assert.deepEqual(saved.source, record.source, 'Bulk copy must never alter source evidence');
            await capture(page, out, 'after-review-dark');
            if (!await page.locator('.so-inuse-bulk').evaluate(e => e.open)) await page.locator('.so-inuse-bulk summary').click();
            await page.locator('.so-inuse-bulk input[type=checkbox]').first().check();
            await page.getByRole('button', { name: 'Değişiklikleri göster', exact: true }).click();
            await page.locator('.so-inuse-bulk li').first().waitFor();
            assert.match(await page.locator('.so-inuse-bulk li').innerText(), /Hayır → Evet/);
            assert.deepEqual((await json(client, `/api/v1/in-use/${record.id}`)).draft, saved.draft, 'Preview must not overwrite an exception');
            await page.getByRole('button', { name: 'Excel önizleme', exact: true }).click();
            await page.getByLabel('Excel sayfası').waitFor();
            await capture(page, out, 'after-workbook-dark');
            const downloadEvent = page.waitForEvent('download');
            await page.getByRole('button', { name: "WASAS'a arşivle ve indir", exact: true }).click();
            const download = await downloadEvent;
            await download.saveAs(path.join(out, download.suggestedFilename()));
            assert.ok(fs.statSync(path.join(out, download.suggestedFilename())).size > 1000);
            // Another client changes assignment. Preview/download must fail on the old version.
            await json(client, `/api/v1/in-use/${record.id}/assignment`, { method: 'PUT', data: { expectedVersion: saved.version, assigneeId: me.userId, reason: 'Synthetic concurrent change' } });
            await page.getByRole('button', { name: 'Arşivi indir', exact: true }).click();
            await page.getByText('In Use veri sürümü değişti', { exact: true }).waitFor();
            assert.equal(await page.getByLabel('Excel sayfası').count(), 0);
            await capture(page, out, 'after-concurrency');
            await page.getByRole('button', { name: 'Kayıtlı veriyi yeniden oku', exact: true }).click();
            await page.getByText('In Use veri sürümü değişti', { exact: true }).waitFor({ state: 'hidden' });
            saved = await json(client, `/api/v1/in-use/${record.id}`);
            await page.getByLabel('demo-server-01 InternetIn', { exact: true }).selectOption('Yes');
            await json(client, `/api/v1/in-use/${record.id}/assignment`, { method: 'PUT', data: { expectedVersion: saved.version, assigneeId: me.userId, reason: 'Synthetic concurrent save' } });
            await page.getByRole('button', { name: 'Taslağı kaydet', exact: true }).click();
            await page.getByText('In Use veri sürümü değişti', { exact: true }).waitFor();
            assert.equal(await page.getByLabel('demo-server-01 InternetIn', { exact: true }).inputValue(), 'Yes');
            await page.getByRole('button', { name: 'Cevapları koru ve güncel kayıtla karşılaştır', exact: true }).click();
            await page.getByLabel('Güncel kayıt karşılaştırması', { exact: true }).waitFor();
            assert.equal(await page.getByLabel('demo-server-01 InternetIn', { exact: true }).inputValue(), 'Yes');
            await capture(page, out, 'after-conflict-comparison');
            await page.getByRole('button', { name: 'Farkları inceledim; yerel cevaplarla devam et', exact: true }).click();
            await page.getByRole('button', { name: 'Taslağı kaydet', exact: true }).click();
            await page.getByText('Yerel inceleme taslağı kaydedildi.', { exact: true }).waitFor();
            assert.equal((await json(client, `/api/v1/in-use/${record.id}`)).draft.answers.find(a => a.serverId === 'demo-server-01' && a.check === 'InternetIn').value, 'Yes');
            await page.evaluate(() => localStorage.setItem('wasas.appearance', 'light'));
            await navigate(page, ui, `in-use/${record.id}`);
            await page.getByLabel('Yanıtlanacak sunucu', { exact: true }).waitFor();
            await capture(page, out, 'after-review-light');
            await navigate(page, ui, 'in-use');
            await page.waitForFunction(() => !document.documentElement.classList.contains('so-dark'));
            await page.getByLabel('In Use kayıt ara').fill('OR-DEMO-INUSE');
            await page.waitForFunction(() => document.querySelectorAll('.so-inuse-records li').length === 2);
            await capture(page, out, 'after-list-light');
            await page.getByLabel('In Use kayıt ara').fill('NoSuchSyntheticRecord');
            await page.getByLabel('In Use görünüm').focus();
            await page.getByText('Kayıtlı eşleşme yok', { exact: true }).waitFor();
            await capture(page, out, 'after-empty-light');
            const preferences = await json(client, '/api/v1/resources/me');
            await navigate(page, ui, 'resources');
            await page.getByRole('button', { name: 'Çalışma alanını düzenle', exact: true }).click();
            await tour(page, 'personalize');
            await page.getByRole('button', { name: 'Vazgeç', exact: true }).click();
            assert.deepEqual(await json(client, '/api/v1/resources/me'), preferences, 'Shared modal guide must preserve personal ownership/preferences');
            const other = await apiContext(request, api, 'team-lead');
            assert.equal((await other.get('/api/v1/in-use')).status(), 403);
            await other.dispose();
            assert.equal(requests.some(p => /upload|bpm|jira/.test(p)), false);
        }
        assert.deepEqual(errors, []);
        fs.writeFileSync(path.join(out, `${mode}-browser-result.json`), JSON.stringify({ mode, passed: true, pageErrors: errors, viewports: ['1440x900', '390x844'] }, null, 2));
    } catch (error) {
        if (page) { await page.screenshot({ path: path.join(out, `${mode}-failure.png`), fullPage: true }); }
        throw error;
    } finally { await client.dispose(); await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
