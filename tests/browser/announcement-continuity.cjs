// Published local Demo only. Args: playwright-core, UI, API, fresh output, before|after.
const assert = require('node:assert/strict'), fs = require('node:fs'), path = require('node:path'), crypto = require('node:crypto');
const { chromium, request } = require(process.argv[2]);
const { loopback, navigate, signIn, apiContext, json } = require('./journey-support.cjs');
const ui = loopback(process.argv[3]), api = loopback(process.argv[4]), out = path.resolve(process.argv[5]), after = process.argv[6] === 'after';
(async () => {
    fs.mkdirSync(out, { recursive: false });
    const browser = await chromium.launch({ executablePath: 'C:/Program Files/Google/Chrome/Application/chrome.exe', headless: true });
    const context = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1440, height: 900 } });
    await context.tracing.start({screenshots:true,snapshots:true,sources:false}); // Local synthetic trace; do not publish session state.
    await context.route('**/*', r => { const u = new URL(r.request().url()); return ['localhost','127.0.0.1','[::1]'].includes(u.hostname) || u.protocol === 'data:' ? r.continue() : r.abort(); });
    const page = await context.newPage(), client = await apiContext(request, api), evidence = [];
    const route = '/api/v1/announcements/' + crypto.randomUUID();
    const content = { ocoReference: 'OCO-SYNTHETIC', scope: 'Yerel test sistemi', subject: 'Süreklilik kabulü '+crypto.randomUUID().slice(0,8), announcementDate: '2026-09-13',
        workStart: '2026-09-13T01:00:37+03:00', workEnd: '2026-09-13T02:00:59+03:00', description: 'Türkçe & <b> düz metin', impact: 'Kısa kesinti', checks: 'Sağlık kontrolü', notes: '',
        to: ['reader@example.invalid'], cc: ['copy@example.invalid'], bannerRevision: 'bundle-v1', templateRevision: 'oco-table-v2', dateTextRevision: 'tr-v1',
        affectedServices: Array.from({length:155}, (_,i) => `Sentetik servis ${String(i+1).padStart(3,'0')} - Türkçe & inceleme <b> uygulama hizmeti`) };
    const visible = () => page.locator('iframe[title="Duyuru önizlemesi"]:visible');
    const body = () => visible().contentFrame().locator('body');
    async function settled() { await body().waitFor(); await page.getByText(after ? 'Kaydedilmiş önizleme' : 'Kaydedilmemiş önizleme', {exact:true}).waitFor(); }
    async function response(name, change) {
        const c = {...content,...change}, r = await client.post('/api/v1/announcements/preview', { data: c });
        const b = r.status() === 200 ? null : await r.json();
        evidence.push({name, request:{workStart:c.workStart,workEnd:c.workEnd}, status:r.status(),code:b?.code,fields:b?.fields});
    }
    try {
        await json(client, route+'?version=0', {method:'PUT',data:content});
        await signIn(page,ui); await navigate(page,ui,'announcements');
        if (await page.getByRole('button',{name:'Başlat',exact:true}).count()) {
            const beforeTour = await json(client,'/api/v1/announcements');
            await page.getByRole('button',{name:'Başlat',exact:true}).click();
            const guide = page.getByRole('dialog',{name:'Kullanım Rehberi'}); await guide.waitFor();
            await guide.getByRole('button',{name:'İleri',exact:true}).click(); await guide.getByRole('button',{name:'Geri',exact:true}).click();
            await guide.getByRole('button',{name:'Çıkış',exact:true}).click();
            assert.deepEqual(await json(client,'/api/v1/announcements'),beforeTour);
        }
        await page.getByRole('button',{name:'Düzenle: '+content.subject,exact:true}).click(); await settled();
        await response('start-first-invalid',{workStart:'2026-09-14T01:00:37+03:00'});
        await response('corrected',{workStart:'2026-09-14T01:00:37+03:00',workEnd:'2026-09-14T02:00:59+03:00'});
        await response('partial-date',{workStart:'T01:00:37+03:00'});
        await response('blank-start',{workStart:''});
        await response('offset-order',{workStart:'2026-09-13T01:00:37+00:00',workEnd:'2026-09-13T02:00:59+03:00'});
        await page.locator('#announcement-WorkStart').scrollIntoViewIfNeeded();
        await page.evaluate(() => {
            window.continuity = {stop:false,samples:[]};
            const sample = () => { const f = [...document.querySelectorAll('iframe')].find(e=>e.getBoundingClientRect().width>0 && getComputedStyle(e).visibility!=='hidden');
                window.continuity.samples.push({t:performance.now(),frame:!!f,height:document.querySelector('.so-announcement-preview')?.getBoundingClientRect().height,scroll:scrollY,focus:document.activeElement?.id});
                if (!window.continuity.stop) requestAnimationFrame(sample); }; sample();
        });
        await page.locator('#announcement-WorkStart').fill('2026-09-14');
        await page.getByText(after ? 'Bitiş, başlangıçtan sonra olmalı; tarih ve saat dilimini kontrol edin.' : 'Önizleme güncellenemedi',{exact:true}).waitFor();
        await page.screenshot({path:path.join(out,'temporary-invalid.png')});
        assert.equal(await page.locator('#announcement-WorkStart').inputValue(),'2026-09-14');
        if (after) assert.equal(await visible().count(),1);
        await page.locator('#announcement-WorkEnd').fill('2026-09-14');
        await page.waitForFunction(()=>[...document.querySelectorAll('iframe')].some(f=>getComputedStyle(f).visibility==='visible' && f.srcdoc.includes('14.09.2026 02:00:59 UTC +03:00')));
        await body().getByText('14.09.2026 02:00:59 UTC +03:00',{exact:true}).waitFor();
        const samples = await page.evaluate(()=>{window.continuity.stop=true;return window.continuity.samples});
        fs.writeFileSync(path.join(out,'continuity.json'),JSON.stringify(samples,null,2));
        if (after) assert.ok(samples.every(s=>s.frame),'Last preview never disappears during invalid/valid transition');
        else assert.ok(samples.some(s=>!s.frame),'Baseline reproduces iframe removal');
        await page.screenshot({path:path.join(out,'recovered.png')});
        const current = await client.get(route); assert.equal(current.headers().etag,'"1"');
        assert.deepEqual(await current.json(),{...content,restartStart:null,restartEnd:null});
        if (after) {
            assert.ok(Math.max(...samples.map(s=>s.height))-Math.min(...samples.map(s=>s.height)) < 2,'Preview pane height stays stable');
            for (const theme of ['Koyu','Aydınlık']) {
                await page.getByRole('button',{name:'Hesap menüsü',exact:true}).click();
                await page.locator('.so-user-menu-popover.mud-popover-open .mud-list-item').filter({hasText:new RegExp('^'+theme)}).click();
                const date = page.locator('#announcement-WorkEnd'); await date.scrollIntoViewIfNeeded(); const box = await date.boundingBox();
                await date.click({position:{x:box.width-15,y:box.height/2}}); await page.keyboard.press('ArrowRight'); await page.keyboard.press('Enter');
                await date.fill('2026-10-01'); // End-first remains valid, then cross the month at start.
                await page.locator('#announcement-WorkStart').fill('2026-09-30');
                await page.getByLabel('Çalışma başlangıcı saat',{exact:true}).selectOption('23');
                await page.getByLabel('Çalışma başlangıcı dakika',{exact:true}).focus(); await page.keyboard.press('End'); await page.keyboard.press('Enter');
                await body().getByText('30.09.2026 23:59:37 UTC +03:00',{exact:true}).waitFor();
                const details = page.locator('.so-announcement-field').filter({has:page.locator('#announcement-WorkStart')});
                if (!await details.locator('details').evaluate(e=>e.open)) await details.locator('summary').click();
                assert.equal(await page.getByLabel('Çalışma başlangıcı saniye',{exact:true}).inputValue(),'37');
                await page.getByLabel('Çalışma başlangıcı gösterim saat dilimi',{exact:true}).selectOption('+05:45');
                await body().getByText('01.10.2026 02:44:37 UTC +05:45',{exact:true}).waitFor();
                await page.getByLabel('Çalışma başlangıcı gösterim saat dilimi',{exact:true}).selectOption('+03:00');
                await date.fill(''); await page.getByText('Güncel alanlar eksik veya geçersiz',{exact:true}).waitFor(); assert.equal(await visible().count(),1);
                await date.fill('2026-10-01'); await page.getByLabel('Çalışma bitişi dakika',{exact:true}).selectOption('');
                await page.getByText('Güncel alanlar eksik veya geçersiz',{exact:true}).waitFor(); assert.equal(await visible().count(),1);
                await page.getByLabel('Çalışma bitişi dakika',{exact:true}).selectOption('01');
                await body().getByText('01.10.2026 02:01:59 UTC +03:00',{exact:true}).waitFor();
                await page.screenshot({path:path.join(out,'dates-'+theme+'.png')});
            }
            const input = page.locator('#announcement-Description'); await input.fill('');
            await page.getByText('Güncel alanlar eksik veya geçersiz',{exact:true}).waitFor();
            assert.equal(await visible().count(),1); await input.pressSequentially('Türkçe & <b> sürekli yazı',{delay:65});
            await body().getByText('Türkçe & <b> sürekli yazı',{exact:true}).waitFor(); assert.equal(await input.evaluate(e=>e===document.activeElement),true);
            await page.getByRole('button',{name:'Kaydet',exact:true}).click(); await page.getByText('Taslak kaydedildi.',{exact:true}).waitFor();
            await page.getByText('Kaydedilmiş önizleme',{exact:true}).waitFor(); await page.getByText('Güncel içerik kaydedildi',{exact:true}).waitFor();
            const saved = await json(client,route); assert.equal(saved.workStart,'2026-09-30T23:59:37+03:00'); assert.equal(saved.workEnd,'2026-10-01T02:01:59+03:00');
            await input.fill('geçici'); await input.fill(saved.description); await page.getByText('Güncel içerik kaydedildi',{exact:true}).waitFor();
            await page.getByRole('button',{name:'Önizle',exact:true}).click(); await page.getByText('Kaydedilmiş önizleme',{exact:true}).waitFor();
            const frame = visible(); assert.equal(await frame.getAttribute('sandbox'),'');
            assert.equal(await frame.contentFrame().locator('script').count(),0);
            assert.equal(await frame.contentFrame().locator('img').evaluateAll(a=>a.length===6&&a.every(i=>i.complete&&i.naturalWidth>0)),true);
            for (const width of [1440,390]) {
                await page.setViewportSize({width,height:844}); if (width===390) await page.getByRole('button',{name:'Önizleme',exact:true}).click();
                await frame.scrollIntoViewIfNeeded(); await frame.focus(); await page.keyboard.press('Control+End');
                assert.ok((await frame.contentFrame().locator('body').innerText()).includes(content.affectedServices[154]));
                let footerVisible = false;
                for (let i=0;i<50&&!footerVisible;i++) {
                    footerVisible = await frame.contentFrame().locator('footer').evaluate(e=>e.getBoundingClientRect().bottom<=innerHeight+1);
                    if (!footerVisible) await new Promise(resolve=>setTimeout(resolve,100));
                }
                assert.ok(footerVisible,'Keyboard reaches the complete visible footer');
                await page.screenshot({path:path.join(out,'footer-'+width+'.png')});
            }
            await page.setViewportSize({width:1440,height:900}); await page.getByRole('button',{name:'Önizle',exact:true}).click();
            const html = await (await client.get(route+'?version=2&format=html')).text(); fs.writeFileSync(path.join(out,'saved-preview.html'),html);
            const downloadEvent = page.waitForEvent('download'); await page.getByRole('button',{name:'Maili indir',exact:true}).click();
            await (await downloadEvent).saveAs(path.join(out,'representative.eml')); fs.writeFileSync(path.join(out,'saved-draft.json'),JSON.stringify(saved));
            assert.equal((await client.get(route)).headers().etag,'"2"');
            if (process.argv[7] === 'network') {
                const waitSignal = async name => { for(let i=0;i<600;i++) { if(fs.existsSync(path.join(out,name))) return; await new Promise(r=>setTimeout(r,100)); } throw Error('Local controller did not signal '+name); };
                fs.writeFileSync(path.join(out,'stop-api.request'),'local API only'); await waitSignal('api-stopped.signal');
                await input.fill('Ağ kesintisinde korunan düzenleme');
                await page.getByText('Önizleme güncellenemedi',{exact:true}).waitFor(); assert.equal(await visible().count(),1);
                await page.getByText('Önceki önizleme; güncel alanları yansıtmıyor',{exact:true}).waitFor();
                await page.screenshot({path:path.join(out,'network-retained.png')});
                fs.writeFileSync(path.join(out,'resume-api.request'),'same payload/config'); await waitSignal('api-resumed.signal');
                await input.fill('Ağ düzeldi; düzenlemeyle otomatik toparlandı');
                await body().getByText('Ağ düzeldi; düzenlemeyle otomatik toparlandı',{exact:true}).waitFor();
                assert.equal((await client.get(route)).headers().etag,'"2"');
                await input.fill(saved.description); await page.getByText('Güncel içerik kaydedildi',{exact:true}).waitFor();
            }
            await page.getByRole('button',{name:'Taslaklar',exact:true}).click(); assert.equal(await page.locator('iframe').count(),0);
            await page.getByRole('button',{name:'Yeni duyuru',exact:true}).click();
            const marker = content.subject.split(' ').at(-1);
            assert.ok(!await page.locator('iframe').evaluateAll((a,m)=>a.some(f=>f.srcdoc.includes(m)),marker),'Switching drafts clears old private preview');
            const sessions = await json(client,'/api/v1/sessions/active');
            for (const session of sessions.items.filter(s=>!s.isCurrent))
                await json(client,'/api/v1/sessions/revoke',{method:'POST',data:{sessionId:session.sessionId,reason:'Local announcement access-loss acceptance'}});
            await page.locator('#announcement-Subject').fill('Session revoked');
            await page.locator('.so-announcement-workspace').waitFor({state:'hidden'}); assert.equal(await page.locator('iframe').count(),0);
        }
        fs.writeFileSync(path.join(out,'results.json'),JSON.stringify({after,evidence,frameMissingSamples:samples.filter(s=>!s.frame).length},null,2));
    } catch (e) {
        await page.screenshot({path:path.join(out,'failed.png')});
        console.error(await page.locator('iframe').evaluateAll(frames=>frames.map(f=>({cls:f.className,html:f.srcdoc.slice(-500),visibility:getComputedStyle(f).visibility}))));
        throw e;
    } finally { await context.tracing.stop({path:path.join(out,'private-browser-trace.zip')}); await client.dispose(); await context.close(); await browser.close(); }
})().catch(e=>{console.error(e);process.exitCode=1;});
