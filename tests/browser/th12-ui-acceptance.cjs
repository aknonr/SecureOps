// TH-12 UI acceptance of the OR -> Jira (TH-8/9) and In Use completion-gate states that the paired
// InMemory/Simulation hosts cannot reach on their own. A test-only loopback proxy sits between the UI host
// and the API host and rewrites a few response fields, always with synthetic values (SIM-* keys,
// *.example.invalid addresses, the server's own readiness texts). It adds no product route or setting and
// never contacts a corporate provider. Start the API (Demo, Simulation, InMemory) on <API URL>, then start the
// UI with IdentityLookupApi__BaseAddress=http://localhost:<proxy port>/ and fresh in-memory state.
// node th12-ui-acceptance.cjs <playwright path> <UI URL> <API URL> <proxy port> <evidence directory>
const assert = require('node:assert/strict');
const fs = require('node:fs');
const http = require('node:http');
const path = require('node:path');
const { chromium, request } = require(process.argv[2]);
const { loopback, navigate, signIn, capture, apiContext, json } = require('./journey-support.cjs');
const ui = loopback(process.argv[3]);
const api = loopback(process.argv[4]);
const proxyPort = Number(process.argv[5]);
const out = path.resolve(process.argv[6]);
const chrome = process.env.WASAS_CHROME || 'C:/Program Files/Google/Chrome/Application/chrome.exe';

// ---- rewriting proxy -------------------------------------------------------------------------------
const fake = { replay: false, issueUrlFor: null, sent: null, retryMatches: null, unknownDates: 0, readiness: null };
function rewriteRecord(r) {
    if (!r || typeof r !== 'object' || !('orCode' in r)) return r;
    if (fake.issueUrlFor === r.orCode && r.jiraIssueKey) r.jiraIssueUrl = 'https://jira.example.invalid/browse/' + r.jiraIssueKey;
    if (fake.sent?.orCode === r.orCode) {
        Object.assign(r, { workflowState: 6, jiraIssueKey: null, jiraExists: false, reconciliationRequired: false, retryEligible: false,
            claimed: fake.sent.claimed, claimExpiresAt: fake.sent.claimed ? new Date(Date.now() + 10 * 60000).toISOString() : null });
    }
    return r;
}
function rewrite(method, route, body) {
    if (route.startsWith('/api/v1/operational-records')) {
        if (Array.isArray(body)) body.forEach(rewriteRecord);
        else if (Array.isArray(body?.items)) { body.items.forEach(rewriteRecord); if (fake.unknownDates) body.unknownSourceCreatedAtCount = fake.unknownDates; }
        else rewriteRecord(body);
        if (method === 'POST' && /\/jira$/.test(route) && fake.replay && body) body.replayed = true;
        if (/\/retry-preview$/.test(route) && fake.retryMatches !== null && body) body.matchesPersistedDraft = fake.retryMatches;
    }
    if (method === 'GET' && /^\/api\/v1\/in-use\/[^/]+\/execution$/.test(route) && fake.readiness && body) body.readiness = fake.readiness;
    return body;
}
const proxy = http.createServer((req, res) => {
    const headers = { ...req.headers };
    delete headers['accept-encoding'];
    const upstream = http.request({ host: api.hostname === 'localhost' ? '127.0.0.1' : api.hostname, port: api.port, method: req.method, path: req.url, headers }, response => {
        const type = response.headers['content-type'] || '';
        if (!type.includes('json')) { res.writeHead(response.statusCode, response.headers); response.pipe(res); return; }
        const chunks = [];
        response.on('data', c => chunks.push(c));
        response.on('end', () => {
            let text = Buffer.concat(chunks).toString('utf8');
            if (response.statusCode < 300 && text) text = JSON.stringify(rewrite(req.method, req.url.split('?')[0], JSON.parse(text)));
            const outHeaders = { ...response.headers, 'content-length': Buffer.byteLength(text) };
            delete outHeaders['transfer-encoding'];
            res.writeHead(response.statusCode, outHeaders);
            res.end(text);
        });
    });
    upstream.on('error', () => res.destroy());
    req.pipe(upstream);
});

// ---- helpers ---------------------------------------------------------------------------------------
const checks = [];
async function focusVisible(page) {
    return page.evaluate(() => {
        const e = document.activeElement;
        if (!e || e === document.body) return false;
        const s = getComputedStyle(e), r = e.getBoundingClientRect();
        const ring = (s.outlineStyle !== 'none' && parseFloat(s.outlineWidth) > 0) || s.boxShadow !== 'none';
        return ring && r.width > 0 && r.height > 0;
    });
}
async function setTheme(page, theme) {
    await page.evaluate(t => localStorage.setItem('wasas.appearance', t), theme);
}
async function themeApplied(page, theme) {
    await page.waitForFunction(dark => document.documentElement.classList.contains('so-night') === dark, theme === 'dark');
}

(async () => {
    await new Promise(resolve => proxy.listen(proxyPort, '127.0.0.1', resolve));
    const admin = await apiContext(request, api);
    const browser = await chromium.launch({ executablePath: chrome, headless: true });
    let page;
    try {
        const records = await json(admin, '/api/v1/operational-records');
        const fixture = code => records.find(r => r.orCode === code) ?? assert.fail(code + ' missing');
        const [happy, sent, failing] = ['SIM-OR-100', 'SIM-OR-200', 'SIM-OR-300'].map(fixture);
        assert.ok(!happy.jiraIssueKey && !failing.jiraIssueKey, 'Fresh in-memory fixtures required');
        await json(admin, '/api/v1/in-use/refresh', { method: 'POST', data: { commandId: crypto.randomUUID() } });
        const inUse = (await json(admin, '/api/v1/in-use?search=OR-DEMO-INUSE-01')).items[0] ?? assert.fail('In Use fixture missing');

        const context = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1440, height: 900 } });
        page = await context.newPage();
        const panel = () => page.locator('[data-jira-submission]');
        const heading = () => panel().locator('h3');
        const button = name => page.getByRole('button', { name, exact: true });
        // Keyboard modality first, so a programmatic focus is shown the way a Tab would show it.
        const keyboard = async name => { await page.keyboard.press('Shift'); await button(name).focus(); assert.ok(await focusVisible(page), name + ': focus ring'); await page.keyboard.press('Enter'); };
        const confirmCreate = async () => {
            await keyboard('Jira Kaydı Oluştur');
            const dialog = page.getByRole('dialog', { name: 'Jira Kaydı Oluştur', exact: true });
            await dialog.waitFor();
            await page.waitForFunction(() => document.activeElement?.matches('[role="dialog"] input[type="checkbox"]'));
            await page.keyboard.press('Space');
            await dialog.getByText('Onay verildi. Oluştur düğmesi etkin.', { exact: true }).waitFor();
            await page.keyboard.press('Tab');
            await page.keyboard.press('Tab');
            assert.equal(await page.evaluate(() => document.activeElement?.textContent.trim()), 'Oluştur');
            assert.ok(await focusVisible(page), 'Oluştur: focus ring');
            await page.keyboard.press('Enter');
            await dialog.waitFor({ state: 'hidden' });
        };
        // FocusOnNavigate moves focus to the page h1 once the record renders; act only after that.
        const open = async record => {
            await navigate(page, ui, 'operational-records/' + record.id);
            await page.getByRole('heading', { name: record.orCode, exact: true }).waitFor();
            await panel().waitFor();
            await page.waitForFunction(() => document.activeElement?.tagName === 'H1');
        };
        await signIn(page, ui);

        // ---- created with a server link, and a replayed answer
        fake.replay = true;
        fake.issueUrlFor = happy.orCode;
        await open(happy);
        await keyboard('Jira Taslağını Önizle');
        await heading().filter({ hasText: /^Gönderime hazır$/ }).waitFor();
        await confirmCreate();
        await heading().filter({ hasText: /^Jira kaydı oluşturuldu · SIM-/ }).waitFor();
        await panel().locator('[data-replayed]').waitFor();
        const link = panel().locator('a[data-jira-link]');
        assert.equal(await link.getAttribute('href'), 'https://jira.example.invalid/browse/' + (await json(admin, '/api/v1/operational-records/' + happy.id)).jiraIssueKey);
        assert.equal(await link.getAttribute('target'), '_blank');
        assert.match(await link.getAttribute('rel'), /noopener/);
        await page.keyboard.press('Shift');
        await link.focus();
        assert.ok(await focusVisible(page), 'Jira link: focus ring');
        await capture(page, out, 'or-created-link-replayed');
        checks.push('Created: key and server link (only with jiraIssueUrl, new tab, noopener), replay note, visible focus on the link');
        fake.replay = false;
        await page.reload();
        await heading().filter({ hasText: /^Jira kaydı oluşturuldu · SIM-/ }).waitFor();
        assert.equal(await panel().locator('[data-replayed]').count(), 0, 'Replay note belongs to the attempt, not to the record');
        fake.issueUrlFor = null;
        await page.reload();
        await heading().filter({ hasText: /^Jira kaydı oluşturuldu · SIM-/ }).waitFor();
        assert.equal(await panel().locator('a[href]').count(), 0, 'No link without jiraIssueUrl');
        checks.push('After F5: replay note gone (page-only), and without jiraIssueUrl the key stays plain text');

        // ---- retry preview: matching fields shown; changed draft hides the content and closes the button
        await open(failing);
        await keyboard('Jira Taslağını Önizle');
        await heading().filter({ hasText: /^Gönderime hazır$/ }).waitFor();
        await confirmCreate();
        await heading().filter({ hasText: 'Gönderim reddedildi' }).waitFor();
        const retryDialog = () => page.getByRole('dialog', { name: 'Aktarımı Yeniden Dene', exact: true });
        fake.retryMatches = false;
        await keyboard('Yeniden Dene');
        await retryDialog().locator('[data-retry-mismatch]').waitFor();
        assert.equal(await retryDialog().locator('[data-retry-fields]').count(), 0);
        assert.equal(await retryDialog().getByText('Açıklama', { exact: true }).count(), 0, 'Changed draft content stays hidden');
        assert.equal(await retryDialog().getByRole('button', { name: 'Yeniden Dene', exact: true }).isDisabled(), true);
        await capture(page, out, 'or-retry-preview-changed');
        await page.keyboard.press('Escape');
        await retryDialog().waitFor({ state: 'hidden' });
        fake.retryMatches = null;
        await keyboard('Yeniden Dene');
        await retryDialog().locator('[data-retry-fields]').waitFor();
        await capture(page, out, 'or-retry-preview-matching');
        await page.keyboard.press('Escape');
        await retryDialog().waitFor({ state: 'hidden' });
        checks.push('Retry preview: matching draft lists fields; changed draft hides content, explains, keeps Yeniden Dene closed; Escape cancels');

        // ---- F5 on a request that was sent and never answered
        for (const claimed of [true, false]) {
            fake.sent = { orCode: sent.orCode, claimed };
            await navigate(page, ui, 'operational-records/' + sent.id);
            await page.reload();
            await heading().filter({ hasText: claimed ? 'Gönderildi; yanıt henüz kaydedilmedi' : 'Gönderildi, yanıt kaydedilmedi; mutabakat gerekli' }).waitFor();
            assert.equal(await button('Jira Kaydı Oluştur').isDisabled(), true);
            await capture(page, out, claimed ? 'or-sent-awaiting-answer' : 'or-sent-no-answer');
        }
        fake.sent = null;
        checks.push('F5: "gönderildi, yanıt yok" (claim active and lapsed) read from durable state; create closed');

        // ---- unknown source dates: date sort closed with an explanation, reachable by keyboard
        fake.unknownDates = 2;
        await navigate(page, ui, 'operational-records');
        const sort = page.getByRole('combobox', { name: 'Sıralama', exact: true });
        await page.locator('[data-date-order-note]').waitFor();
        await page.waitForFunction(() => document.querySelector('select[aria-label="Sıralama"] option[value=oldest]')?.disabled === true);
        assert.equal(await sort.getAttribute('aria-describedby'), 'or-date-order-note');
        await page.keyboard.press('Shift');
        await sort.focus();
        assert.ok(await focusVisible(page), 'Sort select: focus ring');
        await capture(page, out, 'or-list-unknown-dates');
        fake.unknownDates = 0;
        checks.push('List: unknown source date disables date sort, explanation linked with aria-describedby');

        // ---- In Use completion gate: why the button is closed
        const gates = [
            ['Disabled', null],
            ['WriteFence', 'Kaynak yazma kontrolleri kapalı. Arşivleme ve indirme kullanılabilir.'],
            ['SourceContractsMissing', 'Dinamik vaka hedefi ve koşullu güncelleme, tekil görev eşzamanlılığı ve ek kimliği/içerik doğrulaması için kaynak sözleşmesi eksik. Son OR durumu manuel kontrol edilebilir; arşivleme kullanılabilir.']
        ];
        for (const [code, explanation] of gates) {
            fake.readiness = explanation ? { available: false, code, explanation } : null;
            await navigate(page, ui, 'in-use/' + inUse.id);
            const gate = page.locator('section[aria-label="WASAS adımını onayla"]');
            await gate.waitFor();
            const expected = explanation ?? (await json(admin, `/api/v1/in-use/${inUse.id}/execution`)).readiness.explanation;
            const reason = gate.locator(`[data-readiness="${code}"]`);
            await reason.getByText('Onay düğmesi kapalı.', { exact: true }).waitFor();
            assert.ok((await reason.textContent()).includes(expected), code + ': server explanation');
            const start = gate.getByRole('button', { name: 'Raporu ekle ve WASAS adımını onayla', exact: true });
            assert.equal(await start.isDisabled(), true);
            assert.equal(await start.getAttribute('aria-describedby'), 'inuse-gate-reason');
            await gate.scrollIntoViewIfNeeded();
            await capture(page, out, 'inuse-gate-' + code.toLowerCase());
        }
        fake.readiness = null;
        checks.push('In Use: Disabled (server), WriteFence and SourceContractsMissing texts shown as "Onay düğmesi kapalı." next to the closed button and linked by aria-describedby');

        // ---- dark theme and 200 % native zoom revisit the durable states
        const revisit = async (p, tag) => {
            fake.issueUrlFor = happy.orCode;
            await navigate(p, ui, 'operational-records/' + happy.id);
            await p.locator('[data-jira-submission] a[data-jira-link]').waitFor();
            await capture(p, out, tag + '-or-created-link');
            fake.sent = { orCode: sent.orCode, claimed: false };
            await navigate(p, ui, 'operational-records/' + sent.id);
            await p.locator('[data-jira-submission] h3').filter({ hasText: 'Gönderildi, yanıt kaydedilmedi' }).waitFor();
            await capture(p, out, tag + '-or-sent-no-answer');
            fake.unknownDates = 2;
            await navigate(p, ui, 'operational-records');
            await p.locator('[data-date-order-note]').waitFor();
            await capture(p, out, tag + '-or-list-unknown-dates');
            fake.readiness = { available: false, code: 'WriteFence', explanation: gates[1][1] };
            await navigate(p, ui, 'in-use/' + inUse.id);
            await p.locator('[data-readiness="WriteFence"]').waitFor();
            await capture(p, out, tag + '-inuse-gate-writefence');
            Object.assign(fake, { issueUrlFor: null, sent: null, unknownDates: 0, readiness: null });
        };
        await setTheme(page, 'dark');
        await page.reload();
        await themeApplied(page, 'dark');
        await revisit(page, 'dark');
        await setTheme(page, 'light');
        checks.push('Dark theme: created/link, sent-no-answer, unknown dates, In Use gate at 1440 and 390 px without horizontal scroll');
        await context.close();

        for (const theme of ['light', 'dark']) {
            const profile = path.join(out, 'zoom-profile-' + theme);
            fs.rmSync(profile, { recursive: true, force: true });
            fs.mkdirSync(path.join(profile, 'Default'), { recursive: true });
            // Native browser zoom (Chromium default zoom level 200 %), not CSS zoom.
            fs.writeFileSync(path.join(profile, 'Default', 'Preferences'), JSON.stringify({ partition: { default_zoom_level: { x: Math.log(2) / Math.log(1.2) } } }));
            const zoomed = await chromium.launchPersistentContext(profile, { executablePath: chrome, headless: true, ignoreHTTPSErrors: true, viewport: null, args: ['--window-size=1440,900'] });
            try {
                const p = zoomed.pages()[0] || await zoomed.newPage();
                await signIn(p, ui);
                await setTheme(p, theme);
                await p.reload();
                await themeApplied(p, theme);
                assert.equal(await p.evaluate(() => devicePixelRatio), 2, 'Native 200 % zoom');
                for (const [route, name] of [['operational-records/' + happy.id, 'or-created'], ['operational-records', 'or-list'], ['in-use/' + inUse.id, 'inuse']]) {
                    await navigate(p, ui, route);
                    await p.waitForLoadState('networkidle');
                    assert.equal(await p.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1), false, name + ' 200 %: overflow');
                    await p.screenshot({ path: path.join(out, `zoom200-${theme}-${name}.png`), animations: 'disabled' });
                }
            } finally { await zoomed.close(); fs.rmSync(profile, { recursive: true, force: true }); }
        }
        checks.push('Native 200 % zoom, light and dark: OR detail, OR list and In Use review without horizontal scroll');

        fs.writeFileSync(path.join(out, 'th12-results.json'), JSON.stringify({ checks }, null, 2));
        console.log(JSON.stringify({ checks }, null, 2));
    } catch (error) {
        fs.mkdirSync(out, { recursive: true });
        if (page && !page.isClosed()) {
            fs.writeFileSync(path.join(out, 'failure.html'), await page.content());
            await page.screenshot({ path: path.join(out, 'failure.png'), fullPage: true });
        }
        throw error;
    } finally {
        await browser.close();
        await admin.dispose();
        proxy.close();
    }
})().catch(error => { console.error(error); process.exitCode = 1; });
