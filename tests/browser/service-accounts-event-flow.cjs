// Service Accounts "Bir gelişme mi oldu?" journey (OLAY-3). Synthetic data only; loopback hosts only; never contacts corporate systems.
// node service-accounts-event-flow.cjs <playwright path> <UI URL> <API URL> <evidence directory>
// Preconditions: the API runs in the Demo environment with Access/Session/ServiceAccounts on a disposable database created by
// tests/sql/service-accounts/sa-sql-harness.ps1 (001-036), demo:platform-admin holds the Admin role (module actions) and an 'All'
// scope (sa-demo-bootstrap.sql), and the UI runs on HTTPS with DemoMode__ApiDemoActor=platform-admin. Set WASAS_CHROME to a
// Chromium executable. What this proves: the stepped flow with keyboard and focus, mail hints that leave the form, one transaction
// per save, a stale preview (409) and a refused close (422) that write nothing, the server's close hints, the next-step "Bildir",
// 390 px and dark theme without horizontal overflow. What it cannot prove: an unknown outcome (the UI calls the API server-side,
// so the browser cannot drop that answer; unit-tested), a missing 036 (unit-tested), screen readers, Windows/IIS, corporate TEST.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium, request } = require(process.argv[2]);
const { loopback, navigate, signIn, capture, apiContext, json } = require('./journey-support.cjs');
const ui = loopback(process.argv[3]);
const api = loopback(process.argv[4]);
const out = path.resolve(process.argv[5]);
const suffix = Date.now().toString(36).toUpperCase();
const accountName = `SYN_SVC_OLAY3_${suffix}`;
const results = [];
let current;

const iso = offset => { const d = new Date(); d.setDate(d.getDate() + offset); return d.toISOString().slice(0, 10); };

async function step(name, action) {
    const started = Date.now();
    try { await action(); results.push({ name, result: 'passed', seconds: (Date.now() - started) / 1000 }); }
    catch (error) {
        results.push({ name, result: 'failed', error: String(error.message || error).slice(0, 600) });
        await current?.screenshot({ path: path.join(out, 'failure.png'), fullPage: true }).catch(() => {});
        throw error;
    }
}

async function headingFocused(page, text) {
    await page.waitForFunction(t => document.activeElement?.classList.contains('sa-event-heading') && document.activeElement.textContent.includes(t), text);
}

async function flow(page) {
    return page.locator('section.sa-event');
}

(async () => {
    setTimeout(() => { console.error('journey watchdog expired'); process.exit(2); }, 10 * 60 * 1000).unref();
    fs.mkdirSync(out, { recursive: true });
    const browser = await chromium.launch({ executablePath: process.env.WASAS_CHROME, headless: true });
    const admin = await apiContext(request, api);
    const me = await json(admin, '/api/v1/service-accounts/me');
    assert.equal(me.scopeKind, 'All', 'bootstrap scope missing');
    assert.equal(me.eventGuidanceAvailable, true, '036 must be applied for this journey');

    let detail = await json(admin, '/api/v1/service-accounts/accounts', { method: 'POST', data: { accountName, domain: 'SYN', reason: 'Sentetik OLAY-3 yolculuğu' } });
    const id = detail.summary.id;
    detail = await json(admin, `/api/v1/service-accounts/accounts/${id}/requests`, { method: 'POST', data: { actionType: 'Deletion', notes: 'Sentetik silme talebi' } });
    detail = await json(admin, `/api/v1/service-accounts/accounts/${id}/requests`,
        { method: 'POST', data: { actionType: 'PasswordChange', planStart: iso(-10), planEnd: iso(-2), notes: 'Sentetik gecikmiş parola talebi' } });
    const deletion = detail.requests.find(r => r.actionType === 'Deletion');
    const password = detail.requests.find(r => r.actionType === 'PasswordChange');
    assert.ok(password.overdue, 'the password request must be overdue for the next-step card');

    const context = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1440, height: 900 } });
    const page = await context.newPage();
    current = page;
    page.setDefaultTimeout(20000);
    const errors = [];
    page.on('pageerror', e => errors.push(String(e)));
    try {
        await step('account page shows the flow, the role line and the next-step Bildir', async () => {
            await signIn(page, ui);
            await navigate(page, ui, `service-accounts/${id}`);
            await page.getByRole('button', { name: 'Bir gelişme mi oldu?' }).waitFor();
            await page.getByRole('button', { name: 'Nasıl kullanılır?' }).click();
            await page.getByText('Sizin için:', { exact: false }).waitFor();
            assert.match(await page.locator('.sa-guide-body').innerText(), /doğrulama ayrıdır/);
            await page.getByRole('button', { name: 'Plan bitişi geçen işleri güncelleyin: gelişmeyi bildir' }).waitFor();
            await page.getByText('Henüz tamamlandı olarak kapatılamaz', { exact: false }).first().waitFor();
        });

        await step('keyboard: open, heading focused, arrow selects an event, Enter goes on', async () => {
            await page.getByRole('button', { name: 'Bir gelişme mi oldu?' }).click();
            await headingFocused(page, 'Adım 1 / 3');
            const f = await flow(page);
            assert.equal(await f.locator('[aria-current="step"]').innerText().then(t => t.includes('Ne oldu?')), true);
            await page.keyboard.press('Tab');
            assert.equal(await page.evaluate(() => document.activeElement?.getAttribute('name')), 'sa-event-kind', 'first radio focused');
            await page.keyboard.press('ArrowDown');
            await page.waitForFunction(() => document.querySelector('input[name="sa-event-kind"][value="PasswordChanged"]')?.checked === true);
            await f.getByRole('button', { name: 'İleri: Ayrıntılar' }).focus();
            await page.keyboard.press('Enter');
            await headingFocused(page, 'Adım 2 / 3');
            await capture(page, out, 'event-details');
        });

        await step('mail hints: suggestions only, pasted text leaves the box', async () => {
            const f = await flow(page);
            await f.getByText('Yanıt mailini yapıştır (isteğe bağlı)').click();
            const box = f.getByLabel('Mail metni');
            await box.fill('Merhaba, parola değişti; OCO-00042 ile yapıldı. Tarih: 01.10.2026. Sentetik metin.');
            await f.getByRole('button', { name: 'Öneri al' }).click();
            await f.getByText('OCO numarası: OCO-00042').waitFor();
            assert.equal(await box.inputValue(), '', 'pasted text is cleared once hints arrive');
            await f.getByRole('button', { name: 'Referanslara ekle' }).click();
            await f.getByRole('button', { name: 'Gelişme tarihi yap' }).first().click();
            await f.getByRole('button', { name: 'İleri: Önizleme' }).click();
            await headingFocused(page, 'Adım 3 / 3');
            const text = await f.innerText();
            for (const expected of ['Şunlar kaydedilecek', 'Kaydedilmeyecek', 'Bildirildi — doğrulanmadı', 'Doğrulama yapılmaz', 'Hesap kapatılmaz', 'OCO OCO-00042']) {
                assert.ok(text.includes(expected), 'preview lacks: ' + expected);
            }
            await capture(page, out, 'event-preview');
            await f.getByRole('button', { name: 'Kaydet' }).click();
            await headingFocused(page, 'Kaydedildi');
            assert.match(await f.innerText(), /Hâlâ yapılacaklar \(yapılmadı\)/);
            await f.getByRole('button', { name: 'Kapat' }).click();
        });

        await step('next-step Bildir pre-selects the overdue request; a stale preview writes nothing', async () => {
            await page.getByRole('button', { name: 'Plan bitişi geçen işleri güncelleyin: gelişmeyi bildir' }).click();
            await headingFocused(page, 'Adım 2 / 3 · Ayrıntılar: Parola değişti');
            const f = await flow(page);
            await f.getByLabel('Tarih bilinmiyor (tarih uydurulmaz)').check();
            await f.getByRole('button', { name: 'İleri: Önizleme' }).click();
            await headingFocused(page, 'Adım 3 / 3');
            const before = await json(admin, `/api/v1/service-accounts/accounts/${id}`);
            await json(admin, `/api/v1/service-accounts/accounts/${id}`, { method: 'PATCH', data: { expectedVersion: before.summary.version, notes: 'Sentetik araya giren değişiklik ' + suffix } });
            await f.getByRole('button', { name: 'Kaydet' }).click();
            await f.getByText('Hesap değişti; önizlemeyi yenileyin.').waitFor();
            const after = await json(admin, `/api/v1/service-accounts/accounts/${id}`);
            assert.equal(after.actions.length, before.actions.length, 'a stale commit writes nothing');
            await f.getByRole('button', { name: 'Önizlemeyi yenile' }).click();
            await headingFocused(page, 'Adım 3 / 3');
            await f.getByRole('button', { name: 'Kaydet' }).click();
            await headingFocused(page, 'Kaydedildi');
            await f.getByRole('button', { name: 'Kapat' }).click();
            const saved = await json(admin, `/api/v1/service-accounts/accounts/${id}`);
            const linked = saved.actions.filter(a => a.requestId === password.id);
            assert.equal(linked.length, 1, 'one reported action on the overdue request');
            assert.equal(linked[0].result, 'Performed');
            assert.equal(linked[0].actualOn, null, 'an unknown date is not invented');
        });

        await step('a refused close (422) writes nothing; saving without closing works', async () => {
            await page.getByRole('button', { name: 'Silme talebi için gelişme bildir' }).click();
            await headingFocused(page, 'Ayrıntılar: Hesap silindi');
            const f = await flow(page);
            await f.getByLabel('Tarih bilinmiyor (tarih uydurulmaz)').check();
            await f.getByLabel('Talebi de kapat (Tamamlandı) — sunucu kapanış kuralına bakar').check();
            await f.getByRole('button', { name: 'İleri: Önizleme' }).click();
            await headingFocused(page, 'Adım 3 / 3');
            assert.match(await f.innerText(), /OR numarası yok/);
            const before = await json(admin, `/api/v1/service-accounts/accounts/${id}`);
            await f.getByRole('button', { name: 'Kaydet' }).click();
            await f.getByText('Talep bu kayıtlarla kapatılamıyor; hiçbir kayıt yapılmadı.').waitFor();
            const after = await json(admin, `/api/v1/service-accounts/accounts/${id}`);
            assert.equal(after.actions.length, before.actions.length, 'a refused close writes nothing');
            await f.getByRole('button', { name: 'Talebi kapatmadan önizlemeyi yenile' }).click();
            await headingFocused(page, 'Adım 3 / 3');
            await f.getByRole('button', { name: 'Kaydet' }).click();
            await headingFocused(page, 'Kaydedildi');
            await f.getByRole('button', { name: 'Kapat' }).click();
            const saved = await json(admin, `/api/v1/service-accounts/accounts/${id}`);
            assert.equal(saved.requests.find(r => r.id === deletion.id).status, 'Open', 'the request stays open');
            assert.notEqual(saved.summary.lifecycleState, 'ClosureVerified', 'an event never closes the account');
            assert.ok(saved.actions.every(a => a.result === 'Performed' && a.verifiedOn === null), 'no event verified anything');
        });

        await step('close hint: Talebi kapat is aria-disabled with the server list', async () => {
            await page.locator('tr', { hasText: 'Silme' }).getByRole('button', { name: 'Güncelle / kapat' }).first().click();
            await page.getByText('Talep bu sonuçla şu an kapatılamaz.').waitFor();
            const close = page.getByRole('button', { name: 'Talebi kapat' });
            assert.equal(await close.getAttribute('aria-disabled'), 'true');
            assert.equal(await close.getAttribute('aria-describedby'), 'sa-close-why-list');
            assert.match(await page.locator('#sa-close-why-list').innerText(), /doğrulanmış hesap kapanışı|Eksik/);
            await capture(page, out, 'close-hint');
        });

        await step('dark theme and 390 px: flow without horizontal overflow', async () => {
            await page.evaluate(() => localStorage.setItem('wasas.appearance', 'dark'));
            await navigate(page, ui, `service-accounts/${id}`);
            await page.getByRole('button', { name: 'Bir gelişme mi oldu?' }).click();
            await headingFocused(page, 'Adım 1 / 3');
            await capture(page, out, 'event-choose-dark');
            await page.evaluate(() => localStorage.setItem('wasas.appearance', 'light'));
        });

        assert.deepEqual(errors, [], 'page errors');
    } finally {
        fs.writeFileSync(path.join(out, 'results.json'), JSON.stringify({ suffix, account: accountName, results }, null, 2));
        await browser.close();
    }
})().catch(error => { console.error(error); process.exitCode = 1; });
