const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

function loopback(value) {
    const url = new URL(value);
    assert.ok(['localhost', '127.0.0.1', '[::1]'].includes(url.hostname), 'Loopback only');
    return url;
}

async function navigate(page, ui, route) {
    let listener;
    let deadline;
    const connected = new Promise((resolve, reject) => {
        deadline = setTimeout(() => reject(new Error('No interactive Blazor circuit')), 15000);
        listener = socket => socket.on('framereceived', ({ payload }) => {
            if (payload.toString().includes('JS.BeginInvokeJS')) resolve();
        });
        page.on('websocket', listener);
    });
    try { await page.goto(new URL(route, ui).href); await connected; }
    finally { clearTimeout(deadline); page.off('websocket', listener); }
}

async function signIn(page, ui) {
    await page.goto(new URL('login', ui).href);
    await page.locator('button[type=submit]').click();
    await page.waitForURL(url => !url.pathname.includes('login'));
}

async function capture(page, out, name) {
    fs.mkdirSync(out, { recursive: true });
    for (const width of [1440, 390]) {
        await page.setViewportSize({ width, height: width === 390 ? 844 : 900 });
        const modal = await page.getByRole('dialog').count() > 0;
        if (!modal) {
            if (width === 390) {
                await page.waitForFunction(() => {
                    const drawer = document.querySelector('.so-drawer');
                    return !drawer || drawer.getBoundingClientRect().right <= 1;
                });
            }
            await page.evaluate(() => window.scrollTo(0, 0));
        }
        await page.screenshot({ path: path.join(out, `${name}-${width}.png`), fullPage: !modal, animations: 'disabled' });
        assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1), false, name + ': overflow');
    }
    await page.setViewportSize({ width: 1440, height: 900 });
}

async function apiContext(request, api, actor = 'platform-admin') {
    return request.newContext({ baseURL: api.href, extraHTTPHeaders: { 'X-SecureOps-Demo-Actor': actor } });
}

async function json(client, route, options = {}) {
    const response = await client.fetch(route, options);
    assert.equal(response.status(), options.expectedStatus || 200, route + ': ' + await response.text());
    return response.json();
}

module.exports = { loopback, navigate, signIn, capture, apiContext, json };
