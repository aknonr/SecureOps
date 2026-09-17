const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { loopback, navigate, signIn, apiContext, json } = require('./journey-support.cjs');
const [driver, uiValue, apiValue, out] = process.argv.slice(2);
const ui = loopback(uiValue), api = loopback(apiValue);
const { chromium, request } = require(driver);
(async () => {
    const start = Date.now(), observations = [];
    fs.mkdirSync(out, { recursive: true });
    const browser = await chromium.launch({ channel: 'msedge', headless: true });
    const context = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1440, height: 900 }, reducedMotion: 'reduce' });
    const page = await context.newPage();
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    const admin = await apiContext(request, api), denied = await apiContext(request, api, 'team-lead');
    async function ready() { await page.locator('.so-loading').last().waitFor({ state: 'hidden' }); }
    async function capture(name) {
        for (const theme of ['Aydınlık', 'Koyu']) {
        await page.getByRole('button', { name: 'Hesap menüsü', exact: true }).click();
        await page.locator('.so-user-menu-popover .mud-list-item').filter({ hasText: theme }).click();
        for (const [width, height] of [[1440, 900], [1366, 768], [390, 844]]) {
            await page.setViewportSize({ width, height });
            if (width === 390) await page.waitForFunction(() => document.querySelector('.so-drawer')?.getBoundingClientRect().right <= 1);
            await page.evaluate(() => window.scrollTo(0, 0));
            const layout = await page.evaluate(() => ({ width: innerWidth, height: innerHeight, zoom: visualViewport.scale,
                font: getComputedStyle(document.querySelector('.so-page')).fontSize,
                contentWidth: document.querySelector('.so-page').getBoundingClientRect().width,
                overflow: document.documentElement.scrollWidth > innerWidth + 1 }));
            assert.equal(layout.overflow, false, name + ' overflow');
            observations.push({ name, theme, ...layout });
            await page.screenshot({ path: path.join(out, `${name}-${theme === 'Koyu' ? 'dark' : 'light'}-${width}.png`), fullPage: true, animations: 'disabled' });
        }
        await page.setViewportSize({ width: 1440, height: 900 });
        }
    }
    try {
        await signIn(page, ui);
        await navigate(page, ui, 'access/users?q=ops&page=2');
        await ready();
        await page.getByText('140 eşleşen kullanıcı', { exact: true }).waitFor();
        await page.locator('.so-access-table-row').first().waitFor();
        assert.equal(await page.locator('.so-access-table-row').count(), 25);
        await capture('users-page-two');
        await page.locator('.so-access-table-row').first().click();
        await ready();
        await page.getByRole('link', { name: 'Listeye dön', exact: true }).click();
        await page.waitForURL(url => url.pathname === '/access/users' && url.searchParams.get('page') === '2');
        await ready();
        assert.ok(page.url().includes('page=2'));
        await navigate(page, ui, 'access/users?q=ops082&status=Approved');
        await ready();
        await page.locator('.so-access-table-row').first().click();
        await ready();
        const targetId = new URL(page.url()).pathname.split('/').pop();
        await capture('user-detail');
        await navigate(page, ui, 'access/requests?q=ops&status=Rejected');
        await ready();
        assert.equal(await page.locator('.so-request-row').count(), 20);
        await page.locator('.so-request-row').first().click();
        await ready();
        await capture('requests-rejected');
        assert.equal(await page.getByRole('button', { name: 'Onayla', exact: true }).count(), 0);
        await navigate(page, ui, 'access/roles');
        await page.getByRole('button', { name: 'Yeni rol', exact: true }).click();
        const roleName = 'Yerel inceleme ' + Date.now();
        await page.getByLabel('Rol adı', { exact: true }).fill(roleName);
        await page.getByLabel('Amaç', { exact: true }).fill('Sentetik rol ve yetki kabulü');
        const inUse = page.locator('fieldset').filter({ has: page.locator('legend', { hasText: 'In Use' }) });
        await inUse.getByRole('checkbox').nth(0).check();
        await inUse.getByRole('checkbox').nth(1).check();
        await page.getByRole('button', { name: 'Etkiyi incele', exact: true }).click();
        await page.locator('.role-impact').waitFor();
        assert.ok(await page.locator('.role-impact').evaluate(element => element === document.activeElement));
        await capture('role-impact-new');
        await page.getByRole('button', { name: 'Değişikliği uygula', exact: true }).click();
        await page.getByText('Rol tanımı kaydedildi.', { exact: true }).waitFor();
        const role = (await json(admin, 'api/v1/access/roles')).find(value => value.name === roleName);
        assert.ok(role);
        await navigate(page, ui, `access/users/${targetId}`);
        await ready();
        await page.getByRole('button', { name: 'Rolleri değiştir', exact: true }).click();
        const dialog = page.getByRole('dialog');
        await dialog.getByText(roleName, { exact: true }).waitFor();
        assert.equal(await dialog.locator('textarea').count(), 0);
        await dialog.locator('label').filter({ hasText: roleName }).getByRole('checkbox').check();
        await dialog.getByRole('button', { name: 'Rolleri uygula', exact: true }).click();
        await page.getByText('Roller güncellendi', { exact: true }).waitFor();
        const updated = await json(admin, `api/v1/access/users/${targetId}`);
        assert.ok(updated.roles.includes(role.code));
        assert.ok(updated.capabilities.includes('InUse.Review'));
        await navigate(page, ui, 'access/roles');
        await page.locator('aside button').filter({ hasText: roleName }).click();
        await inUse.getByRole('checkbox').nth(1).uncheck();
        await page.getByRole('button', { name: 'Etkiyi incele', exact: true }).click();
        await page.getByText('1 kullanıcı · 0 yetki kazanacak · 1 yetki kaybedecek', { exact: true }).waitFor();
        const concurrent = { code: role.code, name: roleName + ' newer', purpose: role.purpose, expectedVersion: role.version, capabilities: role.capabilities };
        const impact = await json(admin, '/api/v1/access/roles/preview', { method: 'POST', data: concurrent });
        await json(admin, '/api/v1/access/roles', { method: 'PUT', data: { ...concurrent, previewToken: impact.previewToken } });
        await page.getByRole('button', { name: 'Değişikliği uygula', exact: true }).click();
        await page.getByRole('heading', { name: 'Rol başka bir işlemde değişti', exact: true }).waitFor();
        assert.equal(await inUse.getByRole('checkbox').nth(1).isChecked(), false, 'local edit survives conflict');
        assert.equal(await page.getByLabel('Rol adı', { exact: true }).inputValue(), roleName);
        await page.getByRole('button', { name: 'Bu sürümle farkları yeniden incele', exact: true }).click();
        await page.getByRole('button', { name: 'Etkiyi incele', exact: true }).click();
        await capture('role-impact-revocation');
        await page.getByRole('button', { name: 'Değişikliği uygula', exact: true }).click();
        await page.getByText('Rol tanımı kaydedildi.', { exact: true }).waitFor();
        assert.equal((await json(admin, `api/v1/access/users/${targetId}`)).capabilities.includes('InUse.Review'), false);
        await json(denied, 'api/v1/access/roles/preview', { method: 'POST', data: { code: 'NotAllowed', name: 'Denied', purpose: 'Denied', expectedVersion: 0, capabilities: [] }, expectedStatus: 403 });
        assert.deepEqual(errors, []);
        fs.writeFileSync(path.join(out, 'result.json'), JSON.stringify({ passed: true, seconds: (Date.now() - start) / 1000, roleCode: role.code, targetId, observations }, null, 2));
    } catch (error) {
        fs.writeFileSync(path.join(out, 'failure.html'), await page.content());
        await page.screenshot({ path: path.join(out, 'failure.png'), fullPage: true });
        throw error;
    } finally { await admin.dispose(); await denied.dispose(); await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
