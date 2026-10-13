// Loopback Demo UI/API only: node api-csrf-ui.cjs <playwright-core> <UI URL> <API URL> <evidence dir>
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium, request } = require(process.argv[2]);
const { loopback, navigate, signIn, apiContext, json, capture } = require('./journey-support.cjs');
const ui = loopback(process.argv[3]), api = loopback(process.argv[4]), out = path.resolve(process.argv[5]);

(async () => {
    fs.mkdirSync(out, { recursive: true });
    const admin = await apiContext(request, api);
    const name = `Synthetic CSRF UI ${Date.now().toString(36)}`;
    const evidence = { checks: [] };
    let browser, page;
    try {
        const category = await json(admin, '/api/v1/resources/categories', { method: 'POST', data: { name } });
        const link = await json(admin, '/api/v1/resources/links', { method: 'POST', data: {
            categoryId: category.id, name, purpose: 'Synthetic CSRF UI acceptance', url: 'https://example.invalid/'
        } });
        browser = await chromium.launch({ executablePath: process.env.WASAS_CHROME || undefined, headless: true });
        const context = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1440, height: 900 } });
        page = await context.newPage();
        const errors = [];
        page.on('pageerror', error => errors.push(error.message));
        await signIn(page, ui);
        await navigate(page, ui, 'resources');
        await page.getByRole('button', { name: `${name}: Favorilere ekle`, exact: true }).click();
        await page.getByRole('button', { name: `${name}: Favorilerden çıkar`, exact: true }).waitFor();
        const preferences = await json(admin, '/api/v1/resources/me');
        assert.ok(preferences.favourites.some(item => item.id === link.id), 'UI favourite must be persisted by the API');
        evidence.checks.push({ name: 'UI favourite write persisted by API', passed: true });

        await page.getByRole('button', { name: `${name}: Grubuma kaydet`, exact: true }).click();
        const dialog = page.getByRole('dialog');
        if (await dialog.getByRole('textbox', { name: 'Grup adı', exact: true }).count() === 0)
            await dialog.getByRole('button', { name: 'Yeni kişisel grup oluştur', exact: true }).click();
        const input = dialog.getByRole('textbox', { name: 'Grup adı', exact: true });
        await input.pressSequentially(name);
        await input.press('Tab');
        await dialog.getByRole('button', { name: 'Oluştur ve kaydet', exact: true }).click();
        await dialog.waitFor({ state: 'detached' });
        const stored = await json(admin, '/api/v1/resources/me');
        const group = stored.sets.find(item => item.name === name);
        assert.ok(group && group.links.some(item => item.id === link.id), 'UI group write must be persisted by the API');
        evidence.checks.push({ name: 'UI group create and link save persisted by API', passed: true });
        await capture(page, out, 'ui-write-confirmed');
        assert.deepEqual(errors, []);
        evidence.passed = true;
    } finally {
        if (!evidence.passed && page) await page.screenshot({ path: path.join(out, 'failure.png'), fullPage: true }).catch(() => {});
        fs.writeFileSync(path.join(out, 'ui-csrf-results.json'), JSON.stringify(evidence, null, 2));
        if (browser) await browser.close();
        await admin.dispose();
    }
})().catch(error => { console.error(error); process.exitCode = 1; });
