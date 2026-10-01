// The operator stops only the owned loopback API after READY; no request interception.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require(process.argv[2]);
const { loopback, signIn, navigate, capture } = require('./journey-support.cjs');
const ui = loopback(process.argv[3]), out = path.resolve(process.argv[4]);
(async () => {
    const browser = await chromium.launch({ executablePath: 'C:/Program Files/Google/Chrome/Application/chrome.exe', headless: true });
    let page;
    try {
        page = await browser.newPage({ ignoreHTTPSErrors: true, viewport: { width: 1440, height: 900 } });
        await signIn(page, ui); await navigate(page, ui, 'resources');
        await page.waitForTimeout(700);
        await page.getByLabel('Bağlantı ara', { exact: true }).fill('korunan sentetik arama');
        await page.getByText('Eşleşen bağlantı yok', { exact: true }).waitFor();
        console.log('READY: stop owned loopback API; read failure check begins in 20 seconds');
        await page.waitForTimeout(20000);
        await page.getByRole('button', { name: 'Yenile', exact: true }).click();
        await page.locator('.so-problem').first().waitFor({ timeout: 120000 });
        assert.equal(await page.getByLabel('Bağlantı ara', { exact: true }).inputValue(), 'korunan sentetik arama');
        const text = await page.locator('.so-page').innerText();
        assert.match(text, /ulaşılam|bağlantı|Bağlantı/);
        assert.doesNotMatch(text, /StackTrace|SqlException|System\.Net\./);
        assert.ok(await page.locator('.so-problem').count() > 0, 'Actionable problem panel');
        await capture(page, out, 'after-links-api-unavailable');
        fs.writeFileSync(path.join(out, 'workspace-error-results.json'), JSON.stringify({ transport: 'Owned loopback API stopped; no proxy', preservedSearch: true, safeProblemPanel: true }, null, 2));
        console.log('PASS: published UI outage state, preserved search, no raw exception');
    } finally {
        if (page && !page.isClosed()) await page.screenshot({ path: path.join(out, 'error-last-state.png'), fullPage: true });
        await browser.close();
    }
})().catch(e => { console.error(e); process.exitCode = 1; });
