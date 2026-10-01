const assert = require('node:assert/strict'), fs = require('node:fs'), path = require('node:path'), crypto = require('node:crypto');
const { chromium, request } = require(process.argv[2]);
const { loopback, navigate, signIn, apiContext, json } = require('./journey-support.cjs');
const ui = loopback(process.argv[3]), api = loopback(process.argv[4]), out = path.resolve(process.argv[5]);
(async () => {
    fs.mkdirSync(out, { recursive: false });
    const browser = await chromium.launch({ executablePath: 'C:/Program Files/Google/Chrome/Application/chrome.exe', headless: true });
    const context = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1366, height: 768 }, reducedMotion: 'reduce' });
    await context.route('**/*', r => ['localhost', '127.0.0.1'].includes(new URL(r.request().url()).hostname) ? r.continue() : r.abort());
    const page = await context.newPage(), client = await apiContext(request, api), measurements = [];
    const ready = () => page.locator('.so-announcements[aria-busy=false]').waitFor();
    const click = async name => { await page.getByRole('button', { name, exact: true }).click(); await ready(); };
    async function shots(name) {
        for (const theme of ['light', 'dark']) {
            await page.getByRole('button', { name:'Hesap menüsü',exact:true }).click();
            await page.locator('.so-user-menu-popover .mud-list-item').filter({hasText:theme==='dark'?/^Koyu/:/^Aydınlık/}).click();
            await page.waitForFunction(t=>document.documentElement.classList.contains('so-dark')===(t==='dark'),theme);
            for (const [width, height, zoom] of [[1366,768,1], [1440,900,1], [683,384,2], [390,844,1]]) {
                await page.setViewportSize({ width, height });
                if (width < 960) await page.waitForFunction(() => document.querySelector('.so-drawer')?.getBoundingClientRect().right <= 1);
                await page.evaluate(() => document.fonts.ready);
                await page.evaluate(() => window.scrollTo(0, 0));
                const metric = await page.evaluate(() => {
                    const surface = document.querySelector('.so-page'), style = getComputedStyle(surface);
                    const rgb = color => { const probe=document.createElement('span');probe.style.color=color;surface.append(probe);const result=getComputedStyle(probe).color;probe.remove();return (result.match(/[\d.]+/g)||[]).slice(0,3).map(Number); };
                    const luminance = color => rgb(color).map(v => { v /= 255; return v <= .04045 ? v / 12.92 : ((v + .055)/1.055)**2.4; }).reduce((a,v,i)=>a+v*[.2126,.7152,.0722][i],0);
                    const foreground = style.color, background = style.getPropertyValue('--mud-palette-background').trim();
                    const f=luminance(foreground), b=luminance(background);
                    return { overflow: document.documentElement.scrollWidth > innerWidth + 1, font: style.fontSize, foreground, background,
                        contrast: Math.max(f+.05,b+.05)/Math.min(f+.05,b+.05), contentWidth:surface.getBoundingClientRect().width };
                });
                assert.equal(metric.overflow, false, name + ' overflow at ' + width);
                measurements.push({ name, theme, width, height, zoom, zoomMethod:zoom===2?'half CSS viewport, equivalent reflow; not managed VDI zoom':'normal', ...metric });
                await page.screenshot({ path:path.join(out, `${name}-${theme}-${width}.png`), fullPage:true });
            }
        }
        await page.setViewportSize({ width:1366,height:768 });
    }
    try {
        const previousDrafts = new Set((await json(client, '/api/v1/announcements')).items.map(i => i.id));
        await signIn(page, ui); await navigate(page, ui, 'announcements'); await ready();
        if (await page.getByRole('button',{name:'Şimdi değil',exact:true}).count()) await click('Şimdi değil');
        await click('Yeni duyuru');
        await page.locator('#announcement-OcoReference').fill('OCO-SYNTHETIC');
        await page.locator('#source-profile').selectOption('NonProd');
        await click('Kaydet ve kaynağı sorgula');
        await page.locator('[data-source-state=Succeeded]').waitFor({timeout:45000});
        for (const label of ['Konu', 'Çalışma başlangıcı', 'Çalışma bitişi', 'Duyuru tarihi', 'Çalışma yapılacak sistem/uygulama', 'Etki', 'Açıklama', 'Kontroller']) {
            const box = page.getByRole('checkbox',{name:label+' uygula',exact:true});
            if (await box.count()) await box.check();
        }
        // Select every available source field; the application, not this fixture, chooses the values.
        await page.locator('section[aria-label="Kaynak incelemesi"] input[type=checkbox]').evaluateAll(boxes => boxes.forEach(b => { if(!b.checked) b.click(); }));
        await click('Seçili değişiklikleri uygula');
        await page.getByText('İncelenen değişiklikler yeni sürüme kaydedildi.',{exact:true}).waitFor();
        const list=await json(client,'/api/v1/announcements'), item=list.items.find(i=>!previousDrafts.has(i.id) && i.subject==='OCO-SYNTHETIC - Planlı Çalışma Duyurusu');
        assert.ok(item); const draft='/api/v1/announcements/'+item.id, stored=await json(client,draft);
        assert.equal(stored.templateRevision,'oco-table-v3'); assert.equal(stored.affectedServices.length,155);
        assert.equal(stored.workStart,'2026-09-15T01:00:00.123+03:00'); assert.equal(stored.workEnd,'2026-09-15T02:00:00.456+03:00');
        await page.locator('#announcement-BannerRevision').selectOption('bundle-v1');
        await click('Kaydet'); await click('Elle düzenlemeye geç'); await click('Önizle');
        const frame=page.frameLocator('iframe[title="Duyuru önizlemesi"]:visible');
        await page.waitForFunction(()=>document.querySelector('iframe.so-preview-active') && !document.querySelector('iframe.so-preview-staging'));
        await frame.getByText('Service 155 <b>', { exact: false }).waitFor();
        await frame.locator('img').first().waitFor();
        await frame.locator('img').evaluateAll(images => Promise.all(images.map(i => i.decode())));
        assert.equal(await frame.locator('img').evaluateAll(a=>a.length===6&&a.every(i=>i.complete&&i.naturalWidth>0)),true);
        const boxes=await frame.locator('img').evaluateAll(a=>a.slice(0,2).map(i=>({top:i.getBoundingClientRect().top,bottom:i.getBoundingClientRect().bottom,width:i.width})));
        assert.ok(boxes[1].top>=boxes[0].bottom); assert.ok(boxes.every(b=>b.width<=600));
        await shots('oco-reviewed');
        await click('İncelemeye hazırla'); await page.waitForURL(/announcements\/preparations\/[a-f0-9-]+$/);
        const prepared=await json(client,'/api/v1/announcements/preparations/'+new URL(page.url()).pathname.split('/').at(-1));
        const download=page.waitForEvent('download'); await page.getByRole('button',{name:'Hazırlanan maili indir',exact:true}).click();
        await (await download).saveAs(path.join(out,'representative.eml'));
        assert.deepEqual(fs.readFileSync(path.join(out,'representative.eml')),Buffer.from(prepared.email,'base64'));
        fs.writeFileSync(path.join(out,'saved-preview.html'),prepared.html); fs.writeFileSync(path.join(out,'saved-draft.json'),JSON.stringify(prepared.draft.content));
        await json(client,'/api/v1/in-use/refresh',{method:'POST',data:{commandId:crypto.randomUUID()}});
        await navigate(page,ui,'in-use'); await page.getByText('OR-DEMO-INUSE-01',{exact:true}).waitFor(); await shots('inuse-list');
        const record=(await json(client,'/api/v1/in-use?search=OR-DEMO-INUSE-01')).items[0];
        await navigate(page,ui,'in-use/'+record.id);
        await page.getByRole('button',{name:'İnceleyici ata/değiştir',exact:true}).waitFor();
        await page.getByRole('checkbox',{name:'Tüm sunucular için gösterilen önerileri inceledim; bu sürümü taslağa ekle',exact:true}).check();
        await page.getByRole('button',{name:'Taslağı kaydet',exact:true}).click();
        await page.getByText(/Kaydedilmiş öneri:/).waitFor();
        const saved=await json(client,'/api/v1/in-use/'+record.id); assert.ok(saved.draft.policy.fingerprint);
        await shots('inuse-detail');
        await page.getByRole('button',{name:'Excel önizleme',exact:true}).click();
        await page.getByRole('button',{name:"WASAS'a arşivle ve indir",exact:true}).waitFor();
        await shots('inuse-workbook');
        for (const route of ['access/users','access/requests','access/roles','access/me']) {
            await navigate(page,ui,route); await page.locator('.so-page').waitFor();
            assert.equal(await page.getByText('Bu yetki için açıklama tanımlı değil.',{exact:true}).count(),0);
            await shots(route.replace('/','-'));
        }
        await navigate(page,ui,'admin/system-status');
        await page.getByRole('button',{name:'Salt okunur denetimi çalıştır',exact:true}).click();
        await page.getByRole('button',{name:'Tanılama kaydını indir',exact:true}).waitFor();
        const diagnosticDownload=page.waitForEvent('download');
        await page.getByRole('button',{name:'Tanılama kaydını indir',exact:true}).click();
        await (await diagnosticDownload).saveAs(path.join(out,'api-operations.json'));
        const diagnostic=JSON.parse(fs.readFileSync(path.join(out,'api-operations.json'),'utf8'));
        assert.equal(diagnostic.Source.State,'Ready'); assert.equal(diagnostic.Assets.filter(a=>a.State==='Validated').length,7);
        fs.writeFileSync(path.join(out,'measurements.json'),JSON.stringify(measurements,null,2));
        fs.writeFileSync(path.join(out,'result.json'),JSON.stringify({passed:true,draft:item.id,record:record.id,prepared:prepared.id,corporateCalls:false,send:false},null,2));
    } catch(error) { await page.screenshot({path:path.join(out,'failure.png'),fullPage:true}); throw error; }
    finally { await client.dispose(); await browser.close(); }
})().catch(error=>{console.error(error);process.exitCode=1;});
