# In Use ve OCO Kaynak Karşılaştırması

## Referans Kimliği

`in_use_v2_sifresiz.txt`: 741 satır,
SHA256 `9DDBE8383606EC8006118B376E520109C9ED236C001817EB0AD1132CD93A3104`.
Önceki `in_use_sifresiz.txt`: `BB07673BCC19FABFB07005E24897810FE9AD91FA6936049E7EB271CABAC0D704`.
Karşılaştırılan farklar 42/44/77 satırlarında kimlik bilgisi yer tutucularıdır;
işlem blokları aynıdır. İkisi de okunmuş, çalıştırılmamıştır.
`mail_sscm` 457 satır: `4A0F86D26C729CCEADFE7F7CE1D8715CD8F78E14196662DD5BD28F1376B0C3EA`.
Kaynak fonksiyonları 67-325 önceki referansla aynıdır. 327-363 dağıtım isteği,
HTML ekidir; nihai duyuru veya OCO kapatma kanıtı değildir.

## İşlemler

| Referans satırları | Uygulama ve düzeltme | Kalan dış kanıt / kabul |
| --- | --- | --- |
| 323-333 | Aktif SMSS_oRFF, DCC 4241, grup 68; mevcut ayrı In Use keşfi korunur | Kaynak tamlık sözleşmesi henüz yok; kısmi/eski durum korunur |
| 348-370 | Sunucular, NMS, boş CheckList_TEKNIK/THY; ek kaynak/inceleme kanıtı sayfaları | Boş sekmeler tamamlanmış kontrol sayılmaz |
| 377-466 | Her sunucunun 100049 ilişkisi ve semantik KEY/SET alanları ayrı modelde | [Genel] aspect yanıtı için aşağıdaki salt okunur toplama |
| 441 | Döngü dışı eski `$si` kullanılmaz; sunucuya özgü servis ID'si korunur | Bir servisin aspect'i başka sunucuya taşınmaz |
| 499-520 | Üç açık cevap, eksik taslak, seçili sunuculara önce fark sonra uygulama | Diyalog kapanması cevap değildir; yerel tarayıcı kabulü |
| 524-555 | PROD Evet; DEV/TEST/NonProd/POC/PreProd Hayır önerisi. Bilinmeyen ortam çözümsüz. UP_DOWN Evet önerisi | Kabul edilen sürümlü öneri, alarm kurulumu kanıtı değildir |
| 531-608 | Kaynak, açık operatör cevabı ve sürümlü politika ayrı kökenler | Kişisel sahip, OS release, KONTROL uydurulmaz |
| 637-657 | 4463/4464 kaynak yazıları kapalı; yerel kayıt bunları çalıştırmaz | Ayrı komut/niyet/hedef/son durum sözleşmesi gerekli |
| 662-695 | Özel değişmez JSON zarfı içinde XLSX; aynı arşivi tekrar indirme | Kaynağa yükleme yok; tarayıcı indirme yeri sunucu tarafından bilinmez |
| 701-730 | İlk BPM sonucunu seçme veya kapatma yok | Tekil faaliyet, koşullu güncelleme ve yetkili son durum eksik |

## Sunucular: 29 Satır

İlişki alanlarının ön eki `(LCSIMS_ServiceInstance)m_rid.`. Referansın sayısal
indisleri kullanılmaz. Aşağıdaki KEY gösterim, SET kesin değer/kimlik demektir.

| Satır | Alan | Uygulamadaki kaynak |
| --- | --- | --- |
| 1 | ALAN ADI | Yerel sütun başlığı; kaynak gözlemi değil |
| 2 | ENVANTER_ID | SET.id, XLSX metin hücresi |
| 3 | HOSTNAME | SET.p_name |
| 4 | SERVER_TYPE | KEY.p_SI_def_server_type |
| 5 | SI_ENVIRONMENT | KEY.p_SI_def_environment; sınıflandırma önerisinin girdisi |
| 6 | CONSUMER_COMPANY | KEY.p_rel_company_owner; bireysel sahip değil |
| 7 | SERVICE OWNER DIRECTORATE | KEY.c_new_SI_major_project.p_rel_obs; kişi değil |
| 8 | SERVICE NAME | KEY.c_new_SI_major_project |
| 9 | SERVICE ASPECT | Doğrulanmış yanıt bekleniyor; [Genel] otomatik gözlem sayılmaz |
| 10 | NETWORK SEGMENT | KEY.p_SI_def_network_segment |
| 11 | IP ADDRESS | SET.p_SI_ip_SI_address_1 |
| 12 | OS NAME | KEY.p_SI_def_os_name |
| 13 | OS_VERSION | KEY.p_def_os_version |
| 14 | OS RELEASE | Kaynak yok; Datacenter Edition varsayılmaz |
| 15-17 | InternetOut / InternetIn / Microsegmented | Üç açık operatör cevabı |
| 18 | NMS dahil etme | Kabul edilmiş ortam politikası önerisi |
| 19 | COUNTRY | Kaynak varsa kaynak, yoksa onaylı sürümlü yapılandırma önerisi |
| 20 | CITY | KEY.p_rel_asset_item.p_rel_lbs.m_parent |
| 21 | BUILDING | KEY.p_rel_asset_item.p_rel_lbs |
| 22-24 | Department / Sub_Department / Contact_email | Kaynak varsa kaynak, yoksa sürümlü öneri; varsayılan boş |
| 25 | UY_Owner Mail Address | Doğrulanmış bireysel sahip sözleşmesi bekleniyor |
| 26-29 | MEMORY / CPU / UP_DOWN / Disk | Kabul edilmiş izleme önerileri; uygulanmış alarm değil |

## NMS: 22 Sütun

Sıra değişmez: KONTROL; IP Address; ENV_ID; ITMC_Service_ID;
ITMC_Turuncu_Sunucu_Listesi_Karsilik; ITMC_Servis_Unsuru_ID;
ITMC_Servis_Unsuru; Hardware_Type; Device_Type; Server_Type; Country; City;
Building; Department; Sub_Department; Contact_email; UY_Owner Mail Address;
ITMC_Event_Owner_Group; ITMC_MEMORY_Alarm; ITMC_CPU_Alarm; ITMC_UP_DOWN_Alarm;
ITMC_Disk_Alarm.

ENV_ID Sunucular'ın aynı envanter kimliğidir. Servis ID, SET.c_new_SI_major_project.id;
ilişkinin SET.c_new_SI_major_project değeriyle çelişirse yanıt reddedilir.
Servis unsurunun ID/adı gerçek yanıt olmadan boş kalır. Hardware_Type sunucu türü,
Server_Type ortam, Device_Type KEY.p_def_category veya açık yapılandırma önerisidir.
KONTROL doğrulanmadı kalır. Diğer ortak alanlar aynı per-server modelden gelir.
Uzun/başında sıfır olan kimlikler sayıya çevrilmez; XLSX inlineStr testleri bilimsel
gösterim, kesilme ve formül yürütmesini engeller. `1E+06` örneğinden ID türetilmez.

`InUseTests.Policy` PROD/DEV/TEST/bilinmeyen, ayrı servis/aspect kimlikleri, kaynak
önceliği, sürüm değişimi ve tam metin XLSX round-trip senaryolarını doğrular.
Gerçek kaynak parser'ının tarihsel kesin semantik temsil sınırları korunur.

## Eksik Aspect ve Sahip Kanıtı

Yalnız ayrı onaylı salt okunur toplamada, mevcut yetkili transport kullanılır:

1. Tek OR için `rel`, filtre `m_tid=100049 and m_lid=<kesin OR ID>` ve mevcut
   semantik servis projection alınır. Her sunucunun envanter, servis SET kimliği,
   servis KEY gösterimi ayrı kaydedilir; ham kişisel içerik paylaşılmaz.
2. Her **farklı, o sunucuya ait doğrulanmış servis ID** için
   `LCMCMS_Service_FunctionalAspect`, filtre
   `#%p_name%#='[Genel]' and #%p_rel_service_id%#=<servis ID>`, Selects `id,p_name`.
   Sıfır/bir/birden çok satır, QueryResult başarı/error metaverisi, KEY/SET adları,
   scalar/null/nested temsil ve dönen kesin ID-ad eşleşmesi gerekir. ID=112/2915
   genel varsayılan değildir. Birden çok sonuçta ilk satır seçilmez.
3. Bireysel uygulama sahibi ve kurulum aktörü için kaynak sahibinden gerçek
   selector adı, nesne/ilişki yönü, cardinality, KEY gösterim ile SET kişi kimliği
   ve mailin hangi doğrulanmış kişi nesnesinden geldiği istenir. Mevcut referans
   bu selector'ı kanıtlamaz; yeni alan adı tahmin edilmez. p_rel_obs,
   p_rel_company_owner, RFC Bildiren veya entegrasyon hesabı yerine konmaz.
4. Toplama zamanını, endpoint işlem adını, koşulları ve yalnız gerekli maskelenmiş
   şekil örneğini kontrollü kayıtta tutun. Login/Authorization/session/config
   çıktısı alınmaz. Bu belge scripti çalıştırma veya yazma yetkisi vermez.

## OCO ve Görsel Düzeltmesi

Koleksiyon -> sunucu -> c_new_SI_major_project akışı korunur; OCO'nun doğrudan
servis kapsamı olduğu iddia edilmez. CMSite sürücüsüne geçmeden Get-CMDevice
çağıran adapter düzeltildi. Tekil OCO tarihleri, açık UTC farkı veya operatörün
onayladığı kaynak farkıyla önerilir. Restart saati bitişten türetilmez.
Profil içerik özeti API/Worker arasında ve inceleme/uygulama sırasında eşleşir.

Eski v2 renderer yatay header/main hücreleri içerir. Yeni v3 ayrı table satırları,
600px MSO koşullu çerçeve, açık boyut/aspect ratio ve duyarlı tarayıcı genişliği
kullanır. Bu kod nedeni yerelde yeniden üretilmiştir; gerçek Outlook %120 kabulü
ve kurumsal kaynak bağlantısı ayrıca açık kalır. Eski hazırlanmış baytlar değişmez.
