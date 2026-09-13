// Local published Demo hosts with -FinalPresentation. Args: playwright-core, UI, API, denied UI, fresh evidence directory, optional start|final-template|editor.
const assert = require('node:assert/strict'), fs = require('node:fs'), path = require('node:path'), crypto = require('node:crypto');
const { chromium, request } = require(process.argv[2]);
const { loopback, navigate, signIn, capture, apiContext, json } = require('./journey-support.cjs');
const ui = loopback(process.argv[3]), api = loopback(process.argv[4]), deniedUi = loopback(process.argv[5]), out = path.resolve(process.argv[6]);
const finalMode = ['final-template', 'editor'].includes(process.argv[7]);
(async () => {
    fs.mkdirSync(out, { recursive: true });
    const browser = await chromium.launch({ executablePath: 'C:/Program Files/Google/Chrome/Application/chrome.exe', headless: true });
    const client = await apiContext(request, api), denied = await apiContext(request, api, 'team-lead');
    const errors = [], checks = [];
    const context = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1440, height: 900 }, reducedMotion: 'reduce' });
    await context.route('**/*', r => ['localhost', '127.0.0.1'].includes(new URL(r.request().url()).hostname) ? r.continue() : r.abort());
    const page = await context.newPage(); page.on('pageerror', e => errors.push(e.message));
    const ready = () => page.locator('.so-announcements[aria-busy=false]').waitFor();
    const click = async text => { await page.getByRole('button', { name: text, exact: true }).click(); await ready(); };
    try {
        await signIn(page, ui); await navigate(page, ui, 'announcements'); await ready();
        const prefs = await json(client, '/api/v1/resources/me');
        assert.equal(prefs.announcementGuideDismissed, false, 'Use a fresh owner/DB for replay');
        if (process.argv[7] === 'start') {
            await click('Başlat'); await page.getByRole('dialog', { name: 'Kullanım Rehberi' }).waitFor();
            await page.keyboard.press('Escape');
        } else { await click('Şimdi değil'); }
        await page.locator('[data-guide-invitation]').waitFor({ state: 'hidden' });
        assert.equal((await json(client, '/api/v1/resources/me')).announcementGuideDismissed, true);
        await navigate(page, ui, 'announcements'); await ready();
        assert.equal(await page.getByText('Bu modülün nasıl kullanıldığını öğrenmek ister misiniz?').count(), 0);
        await click('Yeni duyuru');
        assert.equal(await page.locator('#announcement-TemplateRevision').count(), 0);
        await page.locator('#announcement-BannerRevision').selectOption('bundle-v1');
        await page.getByRole('button', { name: 'Kaydet', exact: true }).focus(); await page.keyboard.press('Enter');
        await page.getByText('Taslak kaydedildi.', { exact: true }).waitFor(); await click('Önizle');
        await page.getByText('Duyuru alanlarını kontrol edin', { exact: true }).waitFor();
        await click('Konu alanına git'); assert.equal(await page.locator('#announcement-Subject').evaluate(e => e === document.activeElement), true);
        const values = { Subject: 'Şule & Işık <b>', OcoReference: 'OCO-SYNTHETIC', Scope: 'Yerel test kapsamı', AnnouncementDate: '2026-09-12',
            Description: 'Türkçe &lt;b&gt; <script>window.invalid=true</script>', Impact: 'Kısa kesinti', Checks: 'Sağlık kontrolü' };
        for (const [key, value] of Object.entries(values)) await page.locator('#announcement-' + key).fill(value);
        for (const [key, label, time] of [['WorkStart','Çalışma başlangıcı','01:00'], ['WorkEnd','Çalışma bitişi','02:00']]) {
            await page.locator('#announcement-' + key).fill('2026-09-13');
            await page.getByLabel(label + ' saat', { exact: true }).selectOption(time.split(':')[0]);
            await page.getByLabel(label + ' dakika', { exact: true }).selectOption(time.split(':')[1]);
        }
        await page.getByText('Etkilenen servisler (0)', { exact: true }).click();
        await page.locator('#announcement-AffectedServices').fill('Yerel sentetik servis');
        await click('Alıcı ekle'); await page.getByLabel('Alıcı adresi', { exact: true }).fill('reader@example.invalid');
        await click('Bilgi ekle'); await page.getByLabel('Bilgi adresi', { exact: true }).fill('copy@example.invalid');
        assert.equal(await page.getByRole('button', { name: 'Önizle', exact: true }).isDisabled(), true);
        await click('Kaydet'); await page.getByText('Taslak kaydedildi.', { exact: true }).waitFor();
        let list = await json(client, '/api/v1/announcements');
        const id = list.items.find(d => d.subject === values.Subject).id;
        const route = '/api/v1/announcements/' + id;
        const stored = await json(client, route);
        assert.equal(stored.description, values.Description); assert.equal(stored.workStart, '2026-09-13T01:00:00+03:00');
        await capture(page, out, 'editor');
        const beforeTour = await json(client, '/api/v1/announcements');
        await click('Kullanım rehberi');
        const tour = page.getByRole('dialog', { name: 'Kullanım Rehberi' }); await tour.waitFor();
        await page.keyboard.press('ArrowRight'); await page.keyboard.press('ArrowLeft');
        for (let i = 0; i < 4; i++) await tour.getByRole('button', { name: 'İleri', exact: true }).click();
        await capture(page, out, 'tour');
        await page.setViewportSize({ width: 390, height: 844 });
        await tour.getByRole('button', { name: 'Geri', exact: true }).click();
        await tour.getByRole('button', { name: 'İleri', exact: true }).click();
        await page.waitForFunction(() => { const r = document.querySelector('[data-guide=announcement-download]').getBoundingClientRect(); return r.top >= 0 && r.bottom <= innerHeight; });
        await page.screenshot({ path: path.join(out, 'tour-mobile-anchored.png') }); await page.keyboard.press('Escape');
        await page.setViewportSize({ width: 1440, height: 900 });
        assert.deepEqual(await json(client, '/api/v1/announcements'), beforeTour);
        assert.equal(await page.getByRole('button', { name: 'Kullanım rehberi', exact: true }).evaluate(e => e === document.activeElement), true);
        await click('Önizle');
        const frame = page.frameLocator('iframe[title="Duyuru önizlemesi"]:visible');
        await frame.locator('body').waitFor();
        assert.equal(await page.locator('iframe:visible').getAttribute('sandbox'), '');
        assert.match(await frame.locator('body').innerText(), /Türkçe &lt;b&gt; <script>/);
        assert.equal(await frame.locator('script').count(), 0);
        assert.equal(await frame.locator('img').evaluateAll(images => images.length === 6 && images.every(e => e.complete && e.naturalWidth > 0)), true);
        await capture(page, out, 'preview');
        for (const width of [1440, 390]) {
            await page.setViewportSize({ width, height: 844 });
            if (width === 390) await page.waitForFunction(() => document.querySelector('.so-drawer').getBoundingClientRect().right <= 1);
            await page.locator('iframe:visible').scrollIntoViewIfNeeded();
            await page.screenshot({ path: path.join(out, `preview-visible-${width}.png`) });
        }
        await page.setViewportSize({ width: 1440, height: 900 });
        const downloadEvent = page.waitForEvent('download'); await click('Maili indir');
        const download = await downloadEvent; assert.match(download.suggestedFilename(), /^announcement-[a-f0-9]{32}-v2.eml$/);
        await download.saveAs(path.join(out, download.suggestedFilename()));
        assert.deepEqual(await json(client, '/api/v1/announcements'), beforeTour);
        if (process.argv[7] === 'editor') await editorChecks(page, client, denied, route, stored, click, frame, checks);
        checks.push('SQL save/read, explicit sandbox preview, image, Turkish inert text, authenticated download without revision/send, guide persistence and keyboard nonmutation');
        await page.locator('#announcement-Subject').fill('Korunan yerel düzenleme');
        const version = (await client.get(route)).headers().etag.replaceAll('"', '');
        await json(client, route + '?version=' + version, { method: 'PUT', data: { ...stored, subject: 'Diğer sekme' } });
        await click('Kaydet'); await page.getByText('Taslağın daha yeni bir kaydı var', { exact: true }).waitFor();
        assert.equal(await page.locator('#announcement-Subject').inputValue(), 'Korunan yerel düzenleme');
        await click('Güncel kayıtla karşılaştır'); await capture(page, out, 'conflict');
        await click('Farkları inceledim; düzenlemelerimle devam et'); await click('Kaydet');
        assert.equal((await json(client, route)).subject, 'Korunan yerel düzenleme');
        await page.locator('#announcement-Subject').fill('Kaydedilmemiş'); await click('Taslaklar');
        await page.getByRole('button', { name: 'Düzenlemeye dön', exact: true }).click();
        assert.equal(await page.locator('#announcement-Subject').inputValue(), 'Kaydedilmemiş');
        await page.locator('a[href="resources"]').click();
        await page.getByRole('button', { name: 'Düzenlemeye dön', exact: true }).click();
        assert.equal(await page.locator('#announcement-Subject').inputValue(), 'Kaydedilmemiş');
        await page.locator('.so-route-progress.is-active').waitFor({ state: 'hidden' });
        await click('Taslaklar'); await page.getByRole('button', { name: 'Değişiklikleri bırak', exact: true }).click(); await ready();
        if (finalMode) {
            const services = Array.from({ length: 155 }, (_, i) => `Sentetik servis ${String(i + 1).padStart(3, '0')} - Türkçe & inceleme <b> uygulama hizmeti`);
            await click('Yeni duyuru'); await ready();
            await page.locator('#announcement-BannerRevision').selectOption('bundle-v1');
            for (const [key, value] of Object.entries(values)) await page.locator('#announcement-' + key).fill(value);
            for (const [key, label, time] of [['WorkStart','Çalışma başlangıcı','01:00'], ['WorkEnd','Çalışma bitişi','02:00']]) {
                await page.locator('#announcement-' + key).fill('2026-09-13');
                await page.getByLabel(label + ' saat', { exact: true }).selectOption(time.split(':')[0]);
                await page.getByLabel(label + ' dakika', { exact: true }).selectOption(time.split(':')[1]);
            }
            await page.getByText('Etkilenen servisler (0)', { exact: true }).click();
            await page.locator('#announcement-AffectedServices').fill(services.join('\n'));
            await page.getByLabel('Alıcı adresi', { exact: true }).fill('final-audience@example.invalid');
            await click('Kaydet'); await click('Önizle');
            const preview = page.frameLocator('iframe:visible'); await preview.locator('img').first().waitFor();
            assert.equal(await preview.locator('img').count(), 6);
            assert.equal(await preview.locator('img').evaluateAll(images => images.every(i => i.complete && i.naturalWidth > 0 && i.src.startsWith('data:'))), true);
            assert.ok((await preview.locator('body').innerText()).includes(services[154]));
            assert.equal(await preview.locator('script').count(), 0);
            for (const width of [1440, 390]) {
                await page.setViewportSize({ width, height: 844 });
                if (width === 390) await page.waitForFunction(() => document.querySelector('.so-drawer').getBoundingClientRect().right <= 1);
                await page.locator('iframe:visible').scrollIntoViewIfNeeded(); await preview.locator('img').first().scrollIntoViewIfNeeded();
                await page.screenshot({ path: path.join(out, `final-preview-${width}.png`) });
                await preview.locator('h1').click();
                await page.keyboard.press('Control+End');
                // Observe native scrolling without running timer polling inside the no-script sandbox.
                let reachedFooter = false;
                for (let attempt = 0; attempt < 50 && !reachedFooter; attempt++) {
                    reachedFooter = await preview.locator('footer').evaluate(e => e.getBoundingClientRect().bottom <= innerHeight + 1);
                    if (!reachedFooter) await new Promise(resolve => setTimeout(resolve, 100));
                }
                assert.ok(reachedFooter, 'Keyboard reaches the complete footer in the visible sandbox');
                await page.screenshot({ path: path.join(out, `final-footer-${width}.png`) });
            }
            await page.setViewportSize({ width: 1440, height: 900 });
            const event = page.waitForEvent('download'); await click('Maili indir');
            const eml = await event; await eml.saveAs(path.join(out, 'final-template.eml'));
            await page.setViewportSize({ width: 1440, height: 900 }); await click('Taslaklar');
            checks.push('Final template: 155 separate services, six image roles, inert markup, saved preview and authenticated email');
        }
        for (let i = 0; i < 26; i++) await json(client, '/api/v1/announcements/' + crypto.randomUUID(), { method: 'PUT', data: { ...stored, subject: 'Sayfa taslağı ' + i } });
        await navigate(page, ui, 'announcements'); await ready();
        assert.equal(await page.locator('tbody tr').count(), 25);
        await click('Sonraki sayfa');
        await page.waitForFunction(n => document.querySelectorAll('tbody tr').length === n, finalMode ? 3 : 2);
        await capture(page, out, 'list-page2');
        await click('Önceki sayfa'); await page.waitForFunction(() => document.querySelectorAll('tbody tr').length === 25);
        for (const url of ['/api/v1/announcements', route, route + '?version=4&format=html', route + '?version=4&format=eml', '/api/v1/announcements/banners'])
            assert.equal((await denied.get(url)).status(), 403);
        const deniedContext = await browser.newContext({ ignoreHTTPSErrors: true }); const deniedPage = await deniedContext.newPage();
        await signIn(deniedPage, deniedUi); await navigate(deniedPage, deniedUi, 'announcements');
        assert.equal(await deniedPage.getByRole('button', { name: 'Yeni duyuru', exact: true }).count(), 0);
        checks.push('Real stale-version conflict and explicit comparison/save, cancel discard, SQL pagination, API and direct UI capability denial');
        assert.deepEqual(errors, []); fs.writeFileSync(path.join(out, 'results.json'), JSON.stringify({ checks, errors }, null, 2));
    } catch (e) { await page.screenshot({ path: path.join(out, 'failed.png'), fullPage: true }); throw e; }
    finally { await client.dispose(); await denied.dispose(); await browser.close(); }
})().catch(e => { console.error(e); process.exitCode = 1; });

async function editorChecks(page, client, denied, route, stored, click, frame, checks) {
    const before = await json(client, '/api/v1/announcements');
    for (const mode of ['Aydınlık', 'Koyu']) {
        await page.getByRole('button', { name: 'Hesap menüsü', exact: true }).click();
        await page.locator('.so-user-menu-popover.mud-popover-open .mud-list-item').filter({ hasText: new RegExp('^' + mode) }).click();
        await page.waitForFunction(dark => document.documentElement.classList.contains('so-dark') === dark, mode === 'Koyu');
        const date = page.locator('#announcement-WorkStart'); await date.scrollIntoViewIfNeeded();
        assert.equal(await date.evaluate(e => getComputedStyle(e).colorScheme), mode === 'Koyu' ? 'dark' : 'light');
        const original = await date.inputValue(), rect = await date.boundingBox();
        await date.click({ position: { x: rect.width - 15, y: rect.height / 2 } });
        await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))));
        await page.screenshot({ path: path.join(out, 'calendar-' + (mode === 'Koyu' ? 'dark' : 'light') + '.png') });
        await page.keyboard.press('ArrowRight'); await page.keyboard.press('Enter');
        assert.notEqual(await date.inputValue(), original, 'Visible picker accepts keyboard date selection'); await date.fill(original);
    }
    assert.equal(await page.getByLabel('Çalışma başlangıcı dakika', { exact: true }).locator('option').count(), 61);
    await page.getByLabel('Çalışma başlangıcı dakika', { exact: true }).focus(); await page.keyboard.press('End'); await page.keyboard.press('Enter');
    assert.equal(await page.getByLabel('Çalışma başlangıcı dakika', { exact: true }).inputValue(), '59');
    const details = page.locator('.so-announcement-field').filter({ has: page.locator('#announcement-WorkStart') });
    await details.locator('summary').click(); await page.getByLabel('Çalışma başlangıcı saniye', { exact: true }).fill('37');
    await page.getByLabel('Çalışma başlangıcı gösterim saat dilimi', { exact: true }).selectOption('+00:00');
    await page.waitForFunction(() => document.querySelector('#announcement-WorkStart').value === '2026-09-12');
    assert.equal(await page.locator('#announcement-WorkStart').inputValue(), '2026-09-12');
    assert.equal(await page.getByLabel('Çalışma başlangıcı saat', { exact: true }).inputValue(), '22');
    assert.equal(await page.getByLabel('Çalışma başlangıcı saniye', { exact: true }).inputValue(), '37');
    for (let i = 0; i < 8; i++) await page.locator('#announcement-Description').fill('Hızlı düzenleme ' + i);
    await frame.getByText('Hızlı düzenleme 7', { exact: true }).waitFor();
    assert.deepEqual(await json(client, '/api/v1/announcements'), before);
    await page.locator('#announcement-WorkEnd').fill('2026-09-11');
    await page.getByText('Güncel alanlar eksik veya geçersiz', { exact: true }).waitFor(); assert.equal(await page.locator('iframe:visible').count(), 1);
    await page.getByText('Önceki önizleme; güncel alanları yansıtmıyor', { exact: true }).waitFor();
    await page.locator('#announcement-WorkEnd').fill('2026-09-13');
    await frame.locator('body').waitFor();
    await page.setViewportSize({ width: 390, height: 844 }); await page.getByRole('button', { name: 'Düzenle', exact: true }).click();
    await page.locator('#announcement-Description').fill('Mobil korunmuş düzenleme'); assert.equal(await page.locator('iframe:visible').count(), 0);
    await page.getByRole('button', { name: 'Önizleme', exact: true }).focus(); await page.keyboard.press('Enter');
    await page.waitForFunction(() => document.querySelector('[aria-label="Duyuru görünümü"] button[aria-pressed=true]')?.textContent.includes('Önizleme'));
    await frame.getByText('Mobil korunmuş düzenleme', { exact: true }).waitFor();
    await page.locator('iframe:visible').scrollIntoViewIfNeeded(); await frame.locator('h1').scrollIntoViewIfNeeded();
    await page.screenshot({ path: path.join(out, 'live-mobile.png') });
    await page.setViewportSize({ width: 1440, height: 900 }); await click('Kaydet');
    await page.getByText('Taslak kaydedildi.', { exact: true }).waitFor();
    assert.equal((await json(client, route)).workStart, '2026-09-12T22:59:37+00:00');
    await frame.getByText('Mobil korunmuş düzenleme', { exact: true }).waitFor();
    await page.locator('iframe:visible').scrollIntoViewIfNeeded(); await frame.locator('h1').scrollIntoViewIfNeeded(); await page.screenshot({ path: path.join(out, 'live-desktop.png') });
    assert.equal((await denied.post('/api/v1/announcements/preview', { data: stored })).status(), 403);
    // Reuse the same UUID to exercise explicit legacy upgrade and concurrency, without changing list counts.
    await click('Taslaklar');
    const revision = (await client.get(route)).headers().etag.replaceAll('"', '');
    await json(client, route + '?version=' + revision, { method: 'PUT', data: { ...stored, templateRevision: 'oco-v1', dateTextRevision: 'iso-v1', affectedServices: null, bannerRevision: 'synthetic' } });
    await page.reload(); await page.getByRole('button', { name: 'Düzenle: ' + stored.subject, exact: true }).click();
    await page.locator('#announcement-Subject').fill('Korunan yükseltme düzenlemesi');
    await click('Yeni duyuru biçimine yükselt'); await page.getByRole('button', { name: 'Yükselt', exact: true }).click();
    assert.equal(await page.locator('#announcement-Subject').inputValue(), 'Korunan yükseltme düzenlemesi');
    assert.equal((await json(client, route)).templateRevision, 'oco-v1');
    await page.locator('#announcement-BannerRevision').selectOption('bundle-v1');
    await page.getByText('Etkilenen servisler (0)', { exact: true }).click(); await page.locator('#announcement-AffectedServices').fill('İncelenen servis');
    await click('Kaydet'); await page.getByText('Taslak kaydedildi.', { exact: true }).waitFor();
    assert.equal((await json(client, route)).templateRevision, 'oco-table-v2');
    checks.push('New v2, legacy explicit upgrade preserving edits, light/dark picker, all minutes, seconds/instant preservation, rapid/hidden/error live preview without save');
}
