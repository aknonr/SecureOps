# RFC ve ilgili talebi bildiren

Yalnız bu YENİ paketin tamamını kullanın. Eski 6e05b45 EXE yeni seçenekleri
desteklemez. Eski paketleri/kanıtları koruyun. Agent kurumsal çağrı çalıştırmaz.
Bu araç uygulamaya veri yazmaz; başarılı toplama uçtan uca kabul değildir.

ZIP SHA256 değerini teslim kaydıyla, açılan dosyaları payload-manifest.json ile
karşılaştırın. delivery-metadata.json kesin kaynak SHA'sını ve runtime sürümlerini
içerir. Windows x64: Microsoft.NETCore.App 10.0 ve Microsoft.AspNetCore.App 10.0
gerekir; `dotnet --list-runtimes` ile kontrol edin. SDK/IIS/SQL değişikliği yoktur.

## A. Aday alan incelemesi

Mevcut korumalı server-config.json dosyasını kullanın; parola/token istemiyoruz.
Dosyayı pakete koymayın veya paylaşmayın. web.config JSON değildir.
candidate-dictionary.json sadece DOM kaynaklı c_rfc_record ve c_virtual_pc_user
adaylarını içerir. `_i_` öneki API seçicisi değildir. Sözlük onay anlamına gelmez.

Paketi açtığınız klasörde onaylı PowerShell oturumunda aşağıdaki bloğu çalıştırın.
İstenenler yalnız yerel yollar ve tek seçili In Use OR'un sayısal kaynak kimliğidir;
OR kodu veya WASAS GUID'i değildir. Çıktı klasörü önceden mevcut, web dışı ve erişimi
sınırlı olmalıdır. Değişkenleri ve tam çıktıyı paylaşmayın.

```powershell
$toolRoot = (Get-Location).Path
$configPath = Read-Host 'Mevcut korumali server-config.json tam yolu'
$sourceId = Read-Host 'Tek secili In Use OR sayisal kaynak kimligi'
$privateOutput = Read-Host 'Mevcut ozel cikti klasorunun tam yolu'
$outputA = Join-Path $privateOutput ('rfc-A-' + [guid]::NewGuid().ToString('N') + '.json')
& (Join-Path $toolRoot 'tool\InUseEvidence.exe') $configPath $sourceId (Join-Path $toolRoot 'candidate-dictionary.json') $outputA --inspect-candidates
$LASTEXITCODE
```

Beklenen: exit 0, yerel JSON'da Status=CollectedNotMapped, Evidence altında
Root, ServiceItems, CandidateFields; ReferencedRequests=null. A RFC hedefine gitmez.
Yalnız bir aktif 4241/68 OR ve doğrudan Service Items ilişkisi okunur: mevcut 15
alan + bu iki aday. Virtual PC User boş olabilir.

Hücreler Key/Type/ValueState/Shape/Alias taşır. Null, Empty ve Returned ayrıdır;
CandidateFields.Rows.Cells boşsa ilgili SET/KEY hücreleri Omitted durumundadır.
Alias gerçek değer değildir. PositiveInteger/OrCode yalnız biçim bilgisidir;
referansın iş anlamını ve doğru SET/KEY hücresini tek başına onaylamaz.
Karmaşık aday Value için yalnız Array/Object türü ve Cardinality verilir; iç
değerler paylaşılmaz veya düzleştirilmez. Bu durum B sözleşmesini doğrulamaz.
Beklenmeyen key, ret, timeout veya sıfır olmayan exit durumunda durun.
Yeni seçici denemeyin, kapsamı genişletmeyin, tam response dökmeyin.

Özel dosyanın LocalComparison bölümü sadece servis öğesi kimliği/adayı ve exact
hedefin id/kod/Bildiren hücrelerini gerçek Value ve aynı Alias ile içerir. Tam
response içermez. Bu bölümü yalnız sunucuda tarayıcıyla karşılaştırın; paylaşmayın.
Yalnız Evidence nesnesini ve
yerel karşılaştırma sonucunu anonim aliaslarla geri verin: hangi servis öğesi,
hangi SET/KEY hücresi, tür/boşluk durumu, RFC'nin OR kodu mu kaynak kimliği mi
olduğu ve tarayıcıdaki aynı hedefle eşleşip eşleşmediği. Gerçek kişi adı/kimliği,
URL, ActorSid, At, sözlük hash'leri, oturum ve konfigürasyon paylaşılmaz.

## B. Temsil doğrulandıktan sonra exact RFC adımı

A sözlüğü reddedildiyse B'yi çalıştırmayın. rfc-contract.template.json dosyasının
özel klasörde yeni bir kopyasını oluşturun. RfcProperty ancak A destekliyorsa
c_rfc_record kalır. ReferenceCellKind: kanıtlanan SET veya KEY;
ReferenceKind: kanıtlanan OrCode veya SourceId. Boş şablon ağ çağrısından önce
reddedilir. A'da sayısal görünen her değer otomatik SourceId değildir.
ReporterProperty eklemeyin: p_rel_requester zaten bilinen Bildiren seçicisidir.
İstem Sahibi, kurulum yapan kişi, sahip ve WASAS inceleyicisi ayrı kavramlardır.

Temsil doğrulandıktan sonra aynı oturumda:

```powershell
$contractPath = Read-Host 'Kanita gore doldurulmus ozel RFC sozlesmesinin tam yolu'
$outputB = Join-Path $privateOutput ('rfc-B-' + [guid]::NewGuid().ToString('N') + '.json')
& (Join-Path $toolRoot 'tool\InUseEvidence.exe') $configPath $sourceId (Join-Path $toolRoot 'candidate-dictionary.json') $outputB --collect --rfc-contract $contractPath
$LASTEXITCODE
```

Beklenen Evidence.ReferencedRequests.Links içindeki her çözülen bağlantıda
State=ExactMatchNotBusinessOwnership, ReporterLabel=Bildiren;
ReporterDisplayKey=KEY.p_rel_requester, ReporterReferenceKey=SET.p_rel_requester.
Display/referans durumlarını ayrı kontrol edin. RequesterState eski uyumluluk
adıdır; bağımsız İstem Sahibi kanıtı değildir. Başarısız/eksik/belirsiz bağlantı
toplama exit 0 olsa bile doğrulanmış sayılmaz. Aliaslar yalnız aynı koşuda eşit
değerleri gösterir. ReferencedRequests.DistinctLookups ortak RFC'leri sayar.

Hedef sadece id/p_code/p_rel_requester okur; kapanmış veya farklı katalog/gruptaki
hedefleri parent filtresiyle dışlamaz. Kaynak erişim kontrolü korunur. Tek eşleşme
ve dönen id/kod doğrulanır; ilk sonuç seçilmez, yeniden RFC takip edilmez.
En fazla 10 servis öğesi/10 farklı hedef, toplam 45 saniye, yanıt başına 64 KiB,
satır başına 64 hücre, üç dizi seviyesi. Mevcut transport sorgu başına en fazla
bir oturum yenileme/tekrar yapabilir. Kullanıcı/envanter taraması yoktur.

A/B kanıtından sonra uygulamanın açık refresh yoluna üretim bağlantısı
tamamlanacaktır. Bu paket deploy/upload/Jira/BPM/source update/yazma aktivasyonu
yapmaz; yerel testler kurumsal eşlemeyi veya VDI kabulünü kanıtlamaz.
