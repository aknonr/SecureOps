// Loopback API/UI; persisted synthetic SQL authorization; Mock directory; no Worker or corporate calls.
// node admin-service-accounts-directory.cjs <playwright package> <admin UI> <ordinary UI> <API> <evidence>
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium, request } = require(process.argv[2]);
const { loopback, navigate, signIn, capture, apiContext, json } = require('./journey-support.cjs');
const adminUi = loopback(process.argv[3]), ordinaryUi = loopback(process.argv[4]), api = loopback(process.argv[5]);
const out = path.resolve(process.argv[6]);

(async () => {
    fs.mkdirSync(out, { recursive: true });
    const browser = await chromium.launch({ executablePath: process.env.WASAS_CHROME || undefined, headless: true });
    const adminContext = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1440, height: 900 } });
    const ordinaryContext = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1440, height: 900 } });
    const adminPage = await adminContext.newPage(), ordinaryPage = await ordinaryContext.newPage();
    const admin = await apiContext(request, api), ordinary = await apiContext(request, api, 'team-lead');
    const results = [], errors = [];
    for (const page of [adminPage, ordinaryPage]) page.on('pageerror', error => errors.push(error.message));
    async function step(name, action) {
        try { await action(); results.push({ name, result: 'passed' }); }
        catch (error) { results.push({ name, result: 'failed', error: String(error.message).slice(0, 500) }); throw error; }
    }
    try {
        await step('persisted Admin has navigation/admin only; ordinary Identity.Lookup does not imply module access', async () => {
            const a = await json(admin, 'api/v1/access/me'), o = await json(ordinary, 'api/v1/access/me');
            assert.equal(a.accessStatus, 'Approved'); assert.ok(a.roles.includes('Admin'));
            assert.deepEqual(a.capabilities.filter(c => c.startsWith('ServiceAccounts.')).sort(), ['ServiceAccounts.Administer', 'ServiceAccounts.View']);
            assert.ok(o.capabilities.includes('Identity.Lookup')); assert.ok(!o.capabilities.includes('ServiceAccounts.View'));
            assert.equal((await ordinary.get('api/v1/service-accounts/me')).status(), 403);
            const me = await json(admin, 'api/v1/service-accounts/me');
            assert.equal(me.hasScope, false); assert.equal(me.scopeKind, 'None');
            assert.equal((await admin.post('api/v1/service-accounts/scope-grants', { data: { corporateIdentity: 'demo:platform-admin', scopeKind: 'All', reason: 'Synthetic forbidden self-grant' } })).status(), 403);
            assert.equal((await admin.post('api/v1/service-accounts/accounts', { data: { accountName: 'SYN-FORBIDDEN', reason: 'Synthetic' } })).status(), 403);
        });
        await signIn(adminPage, adminUi); await signIn(ordinaryPage, ordinaryUi);
        await step('Admin opens module and administrative pages without silently acquiring data scope', async () => {
            await navigate(adminPage, adminUi, 'service-accounts');
            await adminPage.getByRole('heading', { name: 'Önce veri kapsamınız tanımlanmalı', exact: true }).waitFor();
            await capture(adminPage, out, 'admin-no-scope');
            await navigate(adminPage, adminUi, 'service-accounts/admin');
            await adminPage.getByText('Kapsam yetkileri', { exact: true }).first().waitFor();
            await capture(adminPage, out, 'admin-administration');
            await navigate(adminPage, adminUi, 'access/roles');
            await adminPage.getByRole('heading', { name: 'Rol tanımları', exact: true }).waitFor();
        });
        await step('ordinary user is denied module and administration pages', async () => {
            await navigate(ordinaryPage, ordinaryUi, 'service-accounts');
            await ordinaryPage.getByText('Servis hesaplarını görüntüleme yetkisi gerekli', { exact: true }).waitFor();
            await capture(ordinaryPage, out, 'ordinary-module-denied');
            await navigate(ordinaryPage, ordinaryUi, 'service-accounts/admin');
            await ordinaryPage.getByText('Modül yönetimi yetkisi gerekli', { exact: true }).waitFor();
        });
        await step('general lookup: first and full names plus selected exact account; no inventory links', async () => {
            await navigate(ordinaryPage, ordinaryUi, 'identity-lookup');
            await ordinaryPage.getByRole('tab', { name: 'Ad / ad soyad', exact: true }).click();
            const input = ordinaryPage.getByLabel('Ad veya ad soyad', { exact: true });
            await input.fill('Ayşe');
            await ordinaryPage.getByRole('button', { name: 'Dizinde ara', exact: true }).click();
            await ordinaryPage.getByRole('button', { name: 'syn.ayse.yilmaz', exact: true }).waitFor();
            assert.equal(await ordinaryPage.getByRole('link', { name: 'Kaydı aç', exact: true }).count(), 0);
            await capture(ordinaryPage, out, 'general-first-name');
            await input.fill('ayse yilmaz');
            await ordinaryPage.getByRole('button', { name: 'Dizinde ara', exact: true }).click();
            await ordinaryPage.getByRole('button', { name: 'syn.ayse.yilmaz', exact: true }).waitFor();
            await capture(ordinaryPage, out, 'general-full-name');
            await ordinaryPage.getByRole('button', { name: 'syn.ayse.yilmaz', exact: true }).click();
            await ordinaryPage.getByRole('heading', { name: 'Ayşe Yılmaz', exact: true }).waitFor();
            await capture(ordinaryPage, out, 'general-selected-exact');
            await ordinaryPage.getByRole('button', { name: 'Yeni Sorgu', exact: true }).click();
            await ordinaryPage.getByLabel('Hesap', { exact: true }).fill('pam12356');
            await ordinaryPage.getByRole('button', { name: 'Sorgula', exact: true }).click();
            await ordinaryPage.getByRole('heading', { name: 'Example Admin', exact: true }).waitFor();
            await capture(ordinaryPage, out, 'general-exact-account');
        });
        await step('HTTP name query protections are shared and produce bounded minimal results', async () => {
            const first = await json(ordinary, 'api/v1/identity/name-search', { method: 'POST', data: { query: 'ismail' } });
            const full = await json(ordinary, 'api/v1/identity/name-search', { method: 'POST', data: { query: 'ismail isik' } });
            assert.deepEqual(first.matches.map(m => m.account), ['syn.ismail.isik']);
            assert.deepEqual(full.matches.map(m => m.account), ['syn.ismail.isik']);
            assert.ok(full.matches.every(m => m.serviceAccountId === null));
            for (const query of ['ab', 'ay*', '*)(objectClass=*']) {
                assert.equal((await ordinary.post('api/v1/identity/name-search', { data: { query } })).status(), 400);
            }
        });
        assert.deepEqual(errors, []);
    } catch (error) {
        await adminPage.screenshot({ path: path.join(out, 'admin-failure.png'), fullPage: true }).catch(() => {});
        await ordinaryPage.screenshot({ path: path.join(out, 'failure.png'), fullPage: true }).catch(() => {});
        throw error;
    } finally {
        fs.writeFileSync(path.join(out, 'journey.json'), JSON.stringify({ environment: 'loopback Mock directory; persisted synthetic SQL authorization', results, errors }, null, 2));
        await admin.dispose(); await ordinary.dispose();
        await browser.close();
    }
})().catch(error => { console.error(error); process.exitCode = 1; });
