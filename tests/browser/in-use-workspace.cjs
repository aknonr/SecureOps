// node in-use-workspace.cjs <playwright-core> <ui-loopback> <api-loopback> <evidence-dir> before|after|denied|unavailable
// Published local Demo hosts only. After mode uses the approved isolated SQL and synthetic fixtures.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium, request } = require(process.argv[2]);
const { loopback, navigate, signIn, capture, apiContext, json } = require('./journey-support.cjs');
const ui = loopback(process.argv[3]), api = loopback(process.argv[4]), out = path.resolve(process.argv[5]);
const mode = process.argv[6];
assert.ok(['before', 'after', 'denied', 'unavailable'].includes(mode));
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
        if (mode === 'denied') {
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
            const record = stored.items.find(r => r.source.code === 'OR-DEMO-INUSE-01');
            await page.getByRole('link', { name: 'OR-DEMO-INUSE-01', exact: true }).click();
            await page.getByText('Sorumluluk ve kaynak kanıtı', { exact: true }).waitFor();
            await tour(page, 'inuse-review');
            assert.deepEqual(await json(client, `/api/v1/in-use/${record.id}`), record);
            const me = await json(client, '/api/v1/access/me');
            await page.getByLabel('In Use inceleyicisi', { exact: true }).selectOption(me.userId);
            await page.getByLabel('Atama gerekçesi', { exact: true }).fill('Synthetic browser review assignment');
            await page.getByRole('button', { name: 'Atamayı kaydet', exact: true }).click();
            await page.getByText('Yerel atama kaydedildi.', { exact: true }).waitFor();
            await page.locator('.so-inuse-bulk summary').click();
            await page.getByLabel('Toplu kanıt', { exact: true }).fill('Synthetic explicit bulk unknown');
            const selections = page.locator('.so-inuse-bulk input[type=checkbox]');
            await selections.nth(0).check(); await selections.nth(1).check(); await selections.nth(2).check();
            await page.getByRole('button', { name: 'Seçili cevapları taslağa uygula', exact: true }).click();
            await page.getByText('Toplu cevaplar taslakta. Kaydetmeden önce sunucu bazında inceleyin.', { exact: true }).waitFor();
            assert.equal(await page.getByRole('button', { name: 'Excel önizleme', exact: true }).isDisabled(), true);
            const servers = page.locator('.so-inuse-server');
            for (let index = 0; index < 2; index++) {
                await servers.nth(index).locator('summary').click();
                await page.getByLabel(`demo-server-0${index + 1} InternetOut`, { exact: true }).selectOption(index ? 'No' : 'Yes');
                await page.getByLabel(`demo-server-0${index + 1} InternetOut kanıt`, { exact: true }).fill('Synthetic separately verified evidence ' + index);
            }
            await page.getByLabel('İnceleme notu', { exact: true }).fill('Synthetic browser evidence; not corporate verification');
            await page.getByRole('button', { name: 'Taslağı kaydet', exact: true }).click();
            await page.getByText('Yerel inceleme taslağı kaydedildi.', { exact: true }).waitFor();
            let saved = await json(client, `/api/v1/in-use/${record.id}`);
            assert.deepEqual(saved.draft.answers.filter(a => a.check === 'InternetOut').map(a => a.value), ['Yes', 'No']);
            await capture(page, out, 'after-review-dark');
            await page.getByRole('button', { name: 'Excel önizleme', exact: true }).click();
            await page.getByLabel('Excel sayfası').waitFor();
            await capture(page, out, 'after-workbook-dark');
            const downloadEvent = page.waitForEvent('download');
            await page.getByRole('button', { name: 'Excel indir', exact: true }).click();
            const download = await downloadEvent;
            await download.saveAs(path.join(out, download.suggestedFilename()));
            assert.ok(fs.statSync(path.join(out, download.suggestedFilename())).size > 1000);
            // Another client changes assignment. Preview/download must fail on the old version.
            await json(client, `/api/v1/in-use/${record.id}/assignment`, { method: 'PUT', data: { expectedVersion: saved.version, assigneeId: me.userId, reason: 'Synthetic concurrent change' } });
            await page.getByRole('button', { name: 'Excel indir', exact: true }).click();
            await page.getByText('In Use veri sürümü değişti', { exact: true }).waitFor();
            assert.equal(await page.getByLabel('Excel sayfası').count(), 0);
            await capture(page, out, 'after-concurrency');
            await page.getByRole('button', { name: 'Kayıtlı veriyi yeniden oku', exact: true }).click();
            await page.getByText('In Use veri sürümü değişti', { exact: true }).waitFor({ state: 'hidden' });
            await page.evaluate(() => localStorage.setItem('wasas.appearance', 'light'));
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
