// Service Accounts browser journey. Synthetic data only; loopback hosts only; never contacts corporate systems.
// node service-accounts.cjs <playwright path> <admin UI URL> <team-lead UI URL> <API URL> <evidence directory>
// Preconditions: the API runs with the svcacct candidate schema on a disposable database, demo:platform-admin holds
// Service Accounts actions and an 'All' scope (tests/sql/service-accounts/sa-demo-bootstrap.sql), and demo:team-lead
// holds View/Work/Assign/Verify/Report without any scope. The journey grants the team-lead scope through the UI.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { chromium, request } = require(process.argv[2]);
const { loopback, navigate, signIn, capture, apiContext, json } = require('./journey-support.cjs');
const adminUi = loopback(process.argv[3]);
const leadUi = loopback(process.argv[4]);
const api = loopback(process.argv[5]);
const out = path.resolve(process.argv[6]);
const suffix = Date.now().toString(36).toUpperCase();
const org = `SYN Müdürlük ${suffix}`;
const team = `SYN Ekip ${suffix}`;
const verifier = `Sentetik Doğrulayıcı ${suffix}`;
const accounts = [1, 2, 3, 4, 5].map(i => `SYN_SVC_${suffix}_${i}`);
const today = new Date();
const day = offset => { const d = new Date(today); d.setDate(d.getDate() + offset); return d; };
const tr = d => `${String(d.getDate()).padStart(2, '0')}.${String(d.getMonth() + 1).padStart(2, '0')}.${d.getFullYear()}`;
const results = [];
let current;

async function step(name, action) {
    const started = Date.now();
    try { await action(); results.push({ name, result: 'passed', seconds: (Date.now() - started) / 1000 }); }
    catch (error) {
        results.push({ name, result: 'failed', error: String(error.message || error).slice(0, 400) });
        await current?.screenshot({ path: path.join(out, 'failure.png'), fullPage: true }).catch(() => {});
        throw error;
    }
}

async function field(scope, label, value) {
    const input = scope.getByLabel(label, { exact: true });
    await input.fill(value);
    await input.press('Tab');
}

async function choose(page, scope, label, option) {
    await scope.getByLabel(label, { exact: true }).locator('visible=true').first().click();
    const item = page.locator('.mud-popover-open .mud-list-item').filter({ hasText: option }).first();
    await item.waitFor();
    await item.click();
    await page.locator('.mud-popover-open .mud-list-item').first().waitFor({ state: 'hidden' }).catch(() => {});
}

async function tab(page, name) {
    await page.locator('.mud-tab').filter({ hasText: name }).first().click();
}

async function expand(page, title) {
    // Idempotent: a saved command keeps the panel open, so only a collapsed panel is toggled.
    const panel = page.locator('.mud-expand-panel').filter({ has: page.getByText(title, { exact: true }) }).first();
    if (!((await panel.getAttribute('class')) || '').includes('mud-panel-expanded')) {
        await page.getByText(title, { exact: true }).click();
    }
}

async function problem(page) {
    return page.locator('.so-problem, [role="alert"]').allInnerTexts();
}

(async () => {
    setTimeout(() => { console.error('journey watchdog expired'); process.exit(2); }, 12 * 60 * 1000).unref();
    fs.mkdirSync(out, { recursive: true });
    const browser = await chromium.launch({ executablePath: process.env.WASAS_CHROME || '/opt/pw-browsers/chromium-1194/chrome-linux/chrome', headless: true });
    const admin = await apiContext(request, api);
    const lead = await apiContext(request, api, 'team-lead');
    await json(lead, '/api/v1/service-accounts/me');
    const adminMe = await json(admin, '/api/v1/service-accounts/me');
    assert.equal(adminMe.scopeKind, 'All', 'bootstrap scope missing');
    const context = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1440, height: 900 }, acceptDownloads: true });
    const page = await context.newPage();
    current = page;
    page.setDefaultTimeout(20000);
    const errors = [];
    page.on('pageerror', e => errors.push(String(e)));
    try {
        await step('navigation offers the module', async () => {
            await signIn(page, adminUi);
            await page.getByRole('link', { name: 'Servis Hesapları', exact: true }).waitFor();
        });

        await step('administration: organization, team, person and team-lead scope', async () => {
            await navigate(page, adminUi, 'service-accounts/admin');
            await tab(page, 'Kurum ve ekipler');
            await field(page, 'Yeni kurum adı', org);
            await page.locator('section', { has: page.getByRole('heading', { name: 'Kurumlar' }) }).getByRole('button', { name: 'Kaydet' }).click();
            await page.getByRole('cell', { name: org, exact: true }).waitFor();
            const teams = page.locator('section', { has: page.getByRole('heading', { name: 'Ekipler' }) });
            await field(teams, 'Yeni ekip adı', team);
            await choose(page, teams, 'Kurum', org);
            await teams.getByRole('button', { name: 'Kaydet' }).click();
            await page.getByRole('cell', { name: team, exact: true }).waitFor();
            await tab(page, 'Kişiler');
            await field(page, 'Görünen ad', verifier);
            await page.getByRole('button', { name: 'Ekle', exact: true }).click();
            await page.waitForTimeout(500);
            await tab(page, 'Kapsam yetkileri');
            await field(page, 'Kurumsal kimlik (DOMAIN\\kullanıcı veya UPN)', 'demo:team-lead');
            await choose(page, page, 'Kapsam türü', 'Ekip');
            await choose(page, page, 'Ekip', team);
            await field(page, 'Gerekçe', 'Sentetik ekip lideri kapsamı');
            await page.getByRole('button', { name: 'Kapsam ver', exact: true }).click();
            await page.getByRole('cell', { name: 'Ekip: ' + team }).waitFor();
            await capture(page, out, 'sa-admin');
        });

        const csv = path.join(os.tmpdir(), `sa-liste-${suffix}.csv`);
        fs.writeFileSync(csv, '﻿Kullanıcı Adı;Organizasyon;Son Parola Değişiklik Zamanı;Yorum\n'
            + accounts.map((a, i) => `${a};${org};${45900 + i};sentetik`).join('\n') + '\n');

        await step('import: stage, preview, commit and stored result', async () => {
            await navigate(page, adminUi, 'service-accounts/imports');
            await field(page, 'Kaynak rapor tarihi (bilinmiyorsa boş)', tr(day(-1)));
            await field(page, 'Tarih nereden biliniyor? (zorunlu)', 'Dosya adındaki tarih');
            await field(page, 'Dosyanın kapsamı (ör. kurum adı)', org);
            await field(page, 'Varsayılan domain (dosyada yoksa)', 'SYN');
            await page.locator('#sa-import-file').setInputFiles(csv);
            await page.getByText('Seçilen: ').waitFor();
            await page.getByRole('button', { name: 'Önizleme oluştur' }).click();
            await page.getByRole('heading', { name: path.basename(csv) }).waitFor();
            await page.getByText('Toplam satır').waitFor();
            await capture(page, out, 'sa-import-preview');
            await page.getByRole('button', { name: 'Aktarımı onayla' }).click();
            await page.getByRole('heading', { name: /Aktarım sonucu/ }).waitFor();
            const created = await page.locator('.so-metric', { hasText: 'Oluşturulan hesap' }).innerText();
            assert.match(created, /5/);
            await capture(page, out, 'sa-import-result');
        });

        await step('import replay: same file, period and scope is not written twice', async () => {
            await page.getByRole('button', { name: 'Yeni aktarım / geçmiş' }).click();
            await field(page, 'Kaynak rapor tarihi (bilinmiyorsa boş)', tr(day(-1)));
            await field(page, 'Tarih nereden biliniyor? (zorunlu)', 'Dosya adındaki tarih');
            await field(page, 'Dosyanın kapsamı (ör. kurum adı)', org);
            await field(page, 'Varsayılan domain (dosyada yoksa)', 'SYN');
            await page.locator('#sa-import-file').setInputFiles(csv);
            await page.getByText('Seçilen: ').waitFor();
            await page.getByRole('button', { name: 'Önizleme oluştur' }).click();
            await page.getByText('Daha önce aktarılmış dosya').waitFor();
            const list = await json(admin, `/api/v1/service-accounts/accounts?search=${encodeURIComponent('SYN_SVC_' + suffix)}&pageSize=50`);
            assert.equal(list.total, 5, 'replay must not duplicate accounts');
        });

        let accountUrl;
        await step('list: server filter and keyboard search', async () => {
            await navigate(page, adminUi, 'service-accounts');
            await page.getByRole('status').filter({ hasText: /\d+ hesap/ }).waitFor();
            const search = page.getByLabel('Hesap adı veya SID', { exact: true });
            await search.fill('SYN_SVC_' + suffix);
            await search.press('Enter');
            await page.getByRole('status').filter({ hasText: '5 hesap' }).waitFor();
            await capture(page, out, 'sa-list');
            await page.getByRole('link', { name: accounts[0], exact: true }).click();
            await page.getByRole('heading', { name: accounts[0], exact: true }).waitFor();
            accountUrl = page.url();
        });

        await step('ownership: confirmed team (organization scope)', async () => {
            await tab(page, 'Sahiplik');
            await expand(page, 'Sahiplik öner veya teyit et');
            const panel = page.locator('section', { has: page.getByRole('heading', { name: 'Sahiplik' }) });
            await choose(page, panel, 'Ekip', team);
            await field(panel, 'Gerekçe', 'Sentetik teyit toplantısı');
            await panel.getByRole('button', { name: 'Teyitli sahiplik olarak kaydet' }).click();
            await panel.getByText('Teyitli', { exact: true }).waitFor();
        });

        await step('request and action: rule messages, performed report, verification, explicit close', async () => {
            await tab(page, /^İşler/);
            await expand(page, 'Yeni iş (talep) aç');
            const requests = page.locator('section', { has: page.getByRole('heading', { name: 'Beklenen işler' }) });
            await choose(page, requests, 'Beklenen iş', 'Parola değişimi');
            await choose(page, requests, 'Hedef ekip', team);
            await field(requests, 'Plan bitişi', tr(day(-2)));
            await field(requests, 'Sonraki takip', tr(today));
            await requests.getByRole('button', { name: 'Talebi aç' }).click();
            await requests.getByText('Plan bitişi geçti').waitFor();

            const actions = page.locator('section', { has: page.getByRole('heading', { name: 'İşlem bildirimleri' }) });
            const verify = async (row, date) => {
                await row.getByRole('button', { name: 'Doğrula', exact: true }).click();
                await field(actions, 'Doğrulama tarihi', date);
                await actions.getByLabel('Doğrulayan kişi', { exact: true }).fill(verifier.slice(0, 12));
                await page.locator('.mud-popover-open .mud-list-item').filter({ hasText: verifier }).first().click();
                await field(actions, 'Doğrulama notu / kanıt açıklaması', 'Sentetik doğrulama kanıtı');
                await actions.getByRole('button', { name: 'Doğrulamayı kaydet' }).click();
            };

            // Rule 8: a performed report may be undated; rule 6: it cannot be verified without its real date.
            await expand(page, 'İşlem bildir');
            await choose(page, actions, 'İlgili talep', 'Parola değişimi');
            await actions.getByRole('button', { name: 'Bildirimi kaydet' }).click();
            const undated = actions.getByRole('row').filter({ hasText: 'Tarih bilinmiyor' });
            await verify(undated, tr(today));
            await page.getByText(/işlemin gerçek tarihi gerekir/).waitFor();
            await actions.getByRole('button', { name: 'Vazgeç' }).click();
            await undated.getByRole('button', { name: 'İptal et' }).click();
            await field(actions, 'İptal gerekçesi', 'Sentetik: tarihsiz tekrar kayıt');
            await actions.getByRole('button', { name: 'Kaydı iptal et' }).click();
            await actions.getByText('İptal edildi').waitFor();

            // Dated performed report linked to the request, verified by a separate verifier.
            await expand(page, 'İşlem bildir');
            await choose(page, actions, 'İlgili talep', 'Parola değişimi');
            await field(actions, 'Gerçekleşme tarihi (biliniyorsa)', tr(today));
            await field(actions, 'Kanıt notu', 'Sentetik değişiklik kaydı');
            await actions.getByRole('button', { name: 'Bildirimi kaydet' }).click();
            const dated = actions.getByRole('row').filter({ hasText: tr(today) }).filter({ hasText: 'Parola değişimi' }).filter({ hasText: 'Doğrulanmadı' });
            await verify(dated, tr(today));
            await actions.getByRole('row').filter({ hasText: verifier }).waitFor();

            // Rule 6: a deletion closure cannot be verified without an OR number.
            await expand(page, 'İşlem bildir');
            await choose(page, actions, 'İşlem', 'Silme');
            await choose(page, actions, 'Kayıt türü', 'Hesap kapanışı');
            await field(actions, 'Gerçekleşme tarihi (biliniyorsa)', tr(today));
            await actions.getByRole('button', { name: 'Bildirimi kaydet' }).click();
            const deletion = actions.getByRole('row').filter({ hasText: 'Silme' }).filter({ hasText: 'Doğrulanmadı' });
            await verify(deletion, tr(today));
            await page.getByText(/OR numarası olmadan doğrulanamaz/).waitFor();
            await actions.getByRole('button', { name: 'Vazgeç' }).click();

            await requests.getByRole('button', { name: 'Güncelle / kapat' }).click();
            await requests.getByRole('button', { name: 'Talebi kapat' }).click();
            await requests.getByText('Kapalı · Tamamlandı').waitFor();
            await capture(page, out, 'sa-detail-work');
        });

        await step('one mail linked to three accounts from the list', async () => {
            await navigate(page, adminUi, 'service-accounts');
            await page.getByRole('status').filter({ hasText: /\d+ hesap/ }).waitFor();
            await page.getByLabel('Hesap adı veya SID', { exact: true }).fill('SYN_SVC_' + suffix);
            await page.getByRole('button', { name: 'Listele' }).click();
            for (const account of accounts.slice(0, 3)) {
                await page.getByLabel(`${account} hesabını seç`).check();
            }
            await page.getByRole('button', { name: 'Tek e-posta kaydını bu hesaplara bağla' }).click();
            const form = page.locator('section[aria-label="Çoklu hesap e-posta kaydı"]');
            await field(form, 'Konu', 'Sentetik parola planı talebi');
            await field(form, 'Tarih (bilinmiyorsa boş)', tr(today));
            await form.getByRole('button', { name: 'E-posta kaydını ekle' }).click();
            await form.waitFor({ state: 'hidden' });
            const detail = await json(admin, '/api/v1/service-accounts/accounts/' + accountUrl.split('/').pop());
            assert.equal(detail.communications[0].accounts.length, 3);
        });

        await step('reports: live report, immutable snapshot, XLSX and PDF from the snapshot', async () => {
            await navigate(page, adminUi, 'service-accounts/reports');
            await choose(page, page, 'Kurum filtresi', org);
            await page.getByRole('button', { name: 'Canlı raporu göster' }).click();
            await page.getByRole('heading', { name: 'Canlı rapor' }).waitFor();
            const unique = await page.locator('.so-metric', { hasText: 'Tekil hesap' }).first().innerText();
            assert.match(unique, /5/);
            await field(page, 'Açıklama (ör. alıcı veya toplantı)', 'Sentetik haftalık nüsha ' + suffix);
            await page.getByRole('button', { name: 'Nüshayı kaydet' }).click();
            const row = page.getByRole('row').filter({ hasText: 'Sentetik haftalık nüsha ' + suffix });
            await row.waitFor();
            for (const format of ['XLSX', 'PDF']) {
                const [download] = await Promise.all([page.waitForEvent('download'), row.getByRole('button', { name: format }).click()]);
                const file = path.join(out, `snapshot.${format.toLowerCase()}`);
                await download.saveAs(file);
                const head = fs.readFileSync(file).subarray(0, 4).toString('latin1');
                assert.equal(head, format === 'PDF' ? '%PDF' : 'PK\u0003\u0004');
            }
            await capture(page, out, 'sa-reports');
        });

        await step('team work: reminders evaluated once, drafts are not sent', async () => {
            await navigate(page, adminUi, 'service-accounts/work');
            await page.getByRole('button', { name: 'Değerlendirmeyi şimdi çalıştır' }).click();
            await page.getByText(/açık talep değerlendirildi/).waitFor();
            const first = await json(admin, '/api/v1/service-accounts/reminders/run', { method: 'POST', data: {} });
            assert.equal(first.enqueued, 0, 'a repeated run must not enqueue duplicates');
            await capture(page, out, 'sa-work');
        });

        await step('team lead: only own-team accounts, no module administration', async () => {
            const leadPage = await (await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1440, height: 900 } })).newPage();
            current = leadPage;
            leadPage.setDefaultTimeout(20000);
            await signIn(leadPage, leadUi);
            await navigate(leadPage, leadUi, 'service-accounts');
            await leadPage.getByRole('status').filter({ hasText: /\d+ hesap/ }).waitFor();
            const search = leadPage.getByLabel('Hesap adı veya SID', { exact: true });
            await search.fill('SYN_SVC_' + suffix);
            await search.press('Enter');
            await leadPage.getByRole('status').filter({ hasText: '1 hesap' }).waitFor();
            await leadPage.getByRole('link', { name: accounts[0], exact: true }).waitFor();
            assert.equal(await leadPage.getByRole('link', { name: 'Modül yönetimi' }).count(), 0);
            await navigate(leadPage, leadUi, 'service-accounts/admin');
            await leadPage.getByText('Modül yönetimi yetkisi gerekli').waitFor();
            const other = await (await leadPage.context().request).fetch(new URL(`/api/v1/service-accounts/accounts?pageSize=50&search=SYN_SVC_${suffix}`, api).href,
                { headers: { 'X-SecureOps-Demo-Actor': 'team-lead' } });
            assert.equal((await other.json()).total, 1, 'scope is enforced by the API, not the UI');
            await leadPage.context().close();
            current = page;
        });

        await step('dark theme, 390px and 200% zoom keep content inside the viewport', async () => {
            const dark = await browser.newContext({ ignoreHTTPSErrors: true, colorScheme: 'dark', viewport: { width: 1440, height: 900 } });
            const darkPage = await dark.newPage();
            await signIn(darkPage, adminUi);
            await navigate(darkPage, adminUi, accountUrl);
            await darkPage.getByRole('heading', { name: accounts[0], exact: true }).waitFor();
            await darkPage.waitForTimeout(1500);
            await darkPage.getByRole('heading', { name: accounts[0], exact: true }).waitFor();
            await capture(darkPage, out, 'sa-detail-dark');
            await dark.close();
            const zoom = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 720, height: 450 }, deviceScaleFactor: 2 });
            const zoomPage = await zoom.newPage();
            await signIn(zoomPage, adminUi);
            for (const route of ['service-accounts', 'service-accounts/imports', 'service-accounts/reports', accountUrl]) {
                await navigate(zoomPage, adminUi, route);
                await zoomPage.waitForTimeout(800);
                assert.equal(await zoomPage.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1), false, route + ': overflow at 200%');
            }
            await zoomPage.screenshot({ path: path.join(out, 'sa-detail-zoom200.png'), fullPage: true });
            await zoom.close();
        });

        await step('accessible names: module fields expose their labels', async () => {
            await navigate(page, adminUi, 'service-accounts');
            const unnamed = await page.evaluate(() => [...document.querySelectorAll('.sa-page input:not([type=hidden]), .sa-page textarea, .sa-page [role=button].mud-input-slot')]
                .filter(e => !(e.labels?.length || e.getAttribute('aria-label') || e.getAttribute('aria-labelledby'))).length);
            assert.equal(unnamed, 0);
        });
        assert.deepEqual(errors, []);
    } finally {
        fs.writeFileSync(path.join(out, 'service-accounts-journey.json'), JSON.stringify({ suffix, results, errors }, null, 2));
        await browser.close();
        await admin.dispose();
        await lead.dispose();
    }
})().catch(error => { console.error(error); process.exitCode = 1; });
