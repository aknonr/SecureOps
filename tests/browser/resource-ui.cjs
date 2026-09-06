// Usage: node tests/browser/resource-ui.cjs <playwright-core path> <UI URL> <API URL>
// Requires the existing local development hosts with synthetic InMemory data.
const assert = require('node:assert/strict');
const { randomUUID } = require('node:crypto');
const { chromium } = require(process.argv[2]);
const ui = new URL(process.argv[3]);
const apiUrl = new URL(process.argv[4]);
for (const url of [ui, apiUrl]) {
    assert.ok(['localhost', '127.0.0.1', '[::1]'].includes(url.hostname), 'Loopback hosts only');
}

async function api(route, method = 'GET', body) {
    const response = await fetch(new URL('api/v1/' + route, apiUrl), {
        method,
        headers: { 'Content-Type': 'application/json', 'X-SecureOps-Demo-Actor': 'platform-admin' },
        body: body ? JSON.stringify(body) : undefined
    });
    assert.equal(response.status, 200, `${method} ${route}`);
    return response.json();
}

async function navigate(page, route) {
    // Wait for circuit JS traffic, not merely the prerendered HTTP response.
    const connected = new Promise((resolve, reject) => {
        const deadline = setTimeout(() => reject(new Error('Blazor circuit did not become interactive')), 10000);
        page.once('websocket', socket => {
        socket.on('framereceived', ({ payload }) => {
            if (payload.toString().includes('JS.BeginInvokeJS')) {
                clearTimeout(deadline);
                resolve();
            }
        });
        });
    });
    await page.goto(new URL(route, ui).href);
    await connected;
}

(async () => {
    const label = 'UI review ' + randomUUID().slice(0, 8);
    const category = await api('resources/categories', 'POST', { name: label, expectedVersion: 0 });
    const links = [];
    for (let i = 0; i < 2; i++) {
        links.push(await api('resources/links', 'POST', {
            categoryId: category.id, name: `${label} ${i}`, purpose: 'Synthetic browser fixture',
            url: new URL(`harmless/${i}`, ui).href, expectedVersion: 0
        }));
    }
    let preferences = await api('resources/me');
    const setName = 'S'.repeat(70) + randomUUID().slice(0, 10);
    preferences = await api('resources/me/sets', 'POST', {
        name: setName, linkIds: links.map(link => link.id), expectedVersion: preferences.version
    });
    const set = preferences.sets.find(item => item.name === setName);
    const browser = await chromium.launch({ executablePath: process.env.WASAS_CHROME || 'C:/Program Files/Google/Chrome/Application/chrome.exe', headless: true,
        ignoreDefaultArgs: ['--disable-popup-blocking'] });
    try {
        const context = await browser.newContext({ ignoreHTTPSErrors: true });
        await context.route('**/harmless/**', route => route.fulfill({
            contentType: 'text/html', body: '<title>Local synthetic target</title><h1>Harmless target</h1>'
        }));
        const page = await context.newPage();
        page.setDefaultTimeout(10000);
        await page.goto(new URL('login', ui).href);
        await page.locator('button[type=submit]').click();
        await page.waitForURL(url => !url.pathname.includes('login'));
        console.log('Connected to local UI');
        await navigate(page, 'resources');
        await page.getByRole('button', { name: 'Sayfa boyutu', exact: true }).waitFor();
        console.log('PASS: visible select keyboard target has an accessible name');
        await navigate(page, 'resources/sets');
        assert.equal(await page.locator('a[href="resources"].active').count(), 0);
        const section = page.locator('section').filter({ has: page.locator('h2').filter({ hasText: setName }) });
        await page.setViewportSize({ width: 390, height: 844 });
        assert.ok(await section.locator('h2').evaluate(element => element.getBoundingClientRect().width < innerWidth));
        await page.setViewportSize({ width: 1440, height: 900 });
        console.log('PASS: maximum-length set title fits the narrow layout');
        await section.getByRole('button', { name: 'Vardiyayı Başlat' }).click();
        await section.locator('[data-so-open-links]').waitFor();
        await page.evaluate(() => {
            window.openCalls = [];
            const open = window.open;
            window.open = function (...args) {
                window.openCalls.push({ args, active: navigator.userActivation.isActive });
                return open.apply(this, args);
            };
        });
        await section.locator('[data-so-open-links]').press('Enter');
        await section.locator('[data-so-open-status]:visible').waitFor();
        const calls = await page.evaluate(() => window.openCalls);
        assert.deepEqual(calls.map(call => call.args[0]), links.map(link => link.url));
        assert.ok(calls[0].active, 'Opening starts within browser activation');
        assert.ok(calls.every(call => call.args[2] === 'noopener,noreferrer'));
        console.log('PASS: batch dispatch, order, activation and isolation flags');

        await api('resources/links/' + links[1].id, 'PUT', {
            ...links[1], archived: true, expectedVersion: links[1].version
        });
        await page.getByRole('button', { name: 'Yenile', exact: true }).click();
        await section.locator('.so-set-resolved').waitFor({ state: 'hidden' });
        assert.equal(await section.locator('.so-set-item').count(), 1);
        console.log('PASS: refresh invalidates archived opening candidates');

        await section.getByRole('button', { name: 'Yeniden Adlandır' }).click();
        await page.getByLabel('Set adı', { exact: true }).fill(label + ' draft');
        preferences = await api('resources/me');
        await api('resources/me/favourites/' + links[0].id, 'PUT', {
            favourite: true, expectedVersion: preferences.version
        });
        await page.getByRole('button', { name: 'Kaydet', exact: true }).click();
        const dialog = page.locator('.mud-dialog');
        await dialog.getByText('Kayıt siz düzenlerken değişti', { exact: true }).waitFor();
        assert.equal(await page.getByLabel('Set adı', { exact: true }).inputValue(), label + ' draft');
        assert.ok(await dialog.getByRole('button', { name: 'Kaydet', exact: true }).isDisabled());
        await dialog.getByRole('button', { name: 'Vazgeç', exact: true }).click();
        console.log('PASS: conflict survives refresh, retains draft and blocks stale resubmission');

        await navigate(page, 'admin/resources');
        await page.getByRole('button', { name: 'Yeni Kategori' }).click();
        await page.getByLabel('Ad', { exact: true }).fill('Invalid <fixture>');
        await dialog.getByRole('button', { name: 'Kaydet', exact: true }).click();
        await dialog.locator('.so-problem').waitFor();
        assert.equal(await page.getByLabel('Ad', { exact: true }).inputValue(), 'Invalid <fixture>');
        await dialog.getByRole('button', { name: 'Vazgeç', exact: true }).click();
        console.log('PASS: rejected save retains editable form; cancel dismisses it');
    } finally {
        await browser.close();
        preferences = await api('resources/me');
        await api(`resources/me/sets/${set.id}?expectedVersion=${preferences.version}`, 'DELETE');
        await api('resources/categories/' + category.id, 'PUT', {
            ...category, archived: true, expectedVersion: category.version
        });
    }
})().catch(error => { console.error(error.message); process.exitCode = 1; });
