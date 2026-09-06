// Local UI evidence only. No proxy, provider call, role change or publication.
// Usage: node tests/browser/sdm-milestone.cjs <playwright-core path> <UI URL> <evidence directory>
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { pathToFileURL } = require('node:url');
const { chromium } = require(process.argv[2]);
const ui = new URL(process.argv[3]);
const out = path.resolve(process.argv[4]);
assert.ok(['localhost', '127.0.0.1', '[::1]'].includes(ui.hostname), 'Loopback only');
fs.mkdirSync(out, { recursive: true });
const checks = [];

async function navigate(page, route) {
    let listener;
    const connected = new Promise((resolve, reject) => {
        const deadline = setTimeout(() => reject(new Error('No interactive circuit')), 12000);
        listener = socket => socket.on('framereceived', ({ payload }) => {
            if (payload.toString().includes('JS.BeginInvokeJS')) { clearTimeout(deadline); resolve(); }
        });
        page.on('websocket', listener);
    });
    try { await page.goto(new URL(route, ui).href); await connected; }
    finally { page.off('websocket', listener); }
}

async function capture(page, name) {
    for (const width of [1440, 390]) {
        await page.setViewportSize({ width, height: width === 390 ? 844 : 900 });
        await page.waitForTimeout(700);
        await page.screenshot({ path: path.join(out, `${name}-${width}.png`), fullPage: true, animations: 'disabled' });
        assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1), false, name + ': overflow');
    }
    checks.push(name + ': desktop/mobile render and no horizontal overflow');
}

(async () => {
    const browser = await chromium.launch({ executablePath: process.env.WASAS_CHROME || 'C:/Program Files/Google/Chrome/Application/chrome.exe', headless: true });
    try {
        const context = await browser.newContext({ ignoreHTTPSErrors: true });
        const page = await context.newPage();
        page.setDefaultTimeout(12000);
        await page.goto(new URL('login', ui).href);
        await page.locator('button[type=submit]').click();
        await page.waitForURL(url => !url.pathname.includes('login'));
        await navigate(page, 'dashboard');
        await page.getByText('Yönetim raporlaması henüz etkin değil', { exact: true }).waitFor();
        assert.equal(await page.locator('.so-problem').getByRole('button').count(), 0);
        assert.ok(!(await page.locator('.so-problem').innerText()).includes('SQL'));
        await capture(page, 'dashboard-unavailable');
        await page.getByRole('tab', { name: 'Son 30 gün', exact: true }).click();
        await page.locator('.so-tab--active').filter({ hasText: 'Son 30 gün' }).waitFor();
        assert.equal(await page.getByRole('tab', { name: 'Son 30 gün', exact: true }).getAttribute('aria-selected'), 'true');
        checks.push('Management period selection remains interactive in unavailable state');
        for (const [route, name, ready] of [
            ['resources', 'resources', 'Uygulama Bağlantıları'],
            ['resources/sets', 'groups', 'Bağlantı Gruplarım'],
            ['admin/resources', 'resource-management', 'Bağlantı Yönetimi']
        ]) {
            await navigate(page, route);
            await page.getByRole('heading', { name: ready, exact: true }).waitFor();
            await capture(page, name);
        }
        await navigate(page, 'operational-records');
        await page.locator('.so-problem-title').waitFor();
        await capture(page, 'sdm-source-unavailable');
        const component = path.join(out, 'synthetic-sdm-component.html');
        if (fs.existsSync(component)) {
            const theme = (await page.locator('style').allTextContents()).join('\n');
            await page.goto(pathToFileURL(component).href);
            await page.addStyleTag({ content: theme });
            await page.getByRole('heading', { name: 'SDM değerlendirmesi', exact: true }).waitFor();
            await capture(page, 'sdm-synthetic-component');
        }
        fs.writeFileSync(path.join(out, 'browser-summary.json'), JSON.stringify({ synthetic: true, checks, limits: ['Populated SDM is a static rendering of the real component, not an interactive publication journey.', 'Corporate TEST, successful SQL-backed dashboard browser journey and injected proxy failures were not run.'] }, null, 2));
        checks.forEach(check => console.log('PASS: ' + check));
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
