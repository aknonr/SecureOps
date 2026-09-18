# In Use: Kısa Operatör Rehberi

Bu rehber teslimin `evidence` klasörüne ekran görüntüleriyle birlikte aktarılır.
Görüntüler sentetik yerel kayıtlardır; gerçek Turuncu Hat kapanış kanıtı değildir.
Kurulum ve geri dönüş için teslimdeki `operator-runbook-tr.md` belgesini kullanın.

## 1. Sunucuları İncele

OR, bildiren, inceleyici ve kaynak tarihini kontrol edin. İnceleyici atamak,
bildireni veya servis sahibini değiştirmez. Listeye dönüş işlem başlatmaz.
Servis sahibi eksikse bildirenin adını yazmayın; doğrulanmış sahip ilişkisini
yöneticinizden isteyin. Servis unsuru eksikse ilgili servis kimliği için doğru
eşleme gerekir; başka sunucunun `[Genel]` değeri kullanılamaz.

![Cevaplar girilmeden önce aynı sentetik kayıt](screens/before-answers.png)

## 2. Eksikleri Tamamla

Her satırda internet çıkışı, internetten erişim ve mikrosegmentasyon için Evet,
Hayır veya Bilinmiyor seçin. Bilinmiyor bir olumsuz cevap değildir. İlk eksik
cevaba git ile ilerleyin. Seçili sunuculara uygularken değişiklikleri inceleyip
onaylayın; farklı cevabı tek tek düzeltebilirsiniz. Taslağı kaydetmeden ilerleme
kalıcı değildir. Çatışmada cevapları koruyup güncel farkları karşılaştırın.

İnceleme geçmişinde önceki OR, kişi ve tarihi görün. Yalnız uygun cevapları seçip
açıkça kabul edin; değişmiş kaynak bağlamı eski onayı devralmaz. Ortam/NMS
önerileri kurulum kanıtı değildir; tüm sunucu gruplarını ve istisnaları inceleyin.

![İkinci OR'da önceki cevapların açıkça kullanılması](screens/reuse-light-1366.png)
![Yüzde 200 yakınlaştırmada klavye odağı](screens/light-native-200-viewport.png)

## 3. Excel'i Kontrol Et

Sunucular ve NMS sayfalarını inceleyin. Eksik sahip, OS sürümü veya kontrol
kanıtı otomatik uydurulmaz. WASAS'a arşivle ve indir işleminden sonra indirme
başarısız olursa Arşivi indir ile aynı dosyayı alın; yeniden rapor oluşturmayın.
Özel WASAS arşivinde değişmez JSON zarfı bulunur; tarayıcı dosyayı sizin indirme
ayarınızdaki konuma indirir. Yetkili yönetici etkin fiziksel arşiv yolunu tanılama
ekranından kontrol eder; bu yol sıradan operatöre gösterilmez.

## 4. Talebe Ekle ve Tamamla

Bu kurumsal teslimde kaynak tamamlama kapalıdır. Yazma bayraklarını açmayın;
yerel inceleme ve arşivleme kullanılabilir. Yetkilendirilmiş gelecekteki akışta
son onay aynı arşiv baytlarını sunucudan kullanır; PC'ye indirip geri yükleme yoktur.

Ek doğrulanmış ama görev başarısızsa eki yeniden yüklemeyin. Başarılı ek kanıtı
korunur; görev sonucuyla yöneticinize başvurun.

![Ek doğrulandı, BPM başarısız; tamamlama kapalı](screens/attachment-confirmed-bpm-failed.png)

Yükleme yanıtı belirsizse tekrar göndermeyin. Yetkili kaynak mutabakatı gerekir.

![Yükleme sonucu belirsiz](screens/upload-unknown.png)

Görev yanıtı alınmış olsa bile OR son durumu okunamadıysa kayıt kapatıldı sayılmaz.
Yetkili son-durum okumasını bekleyin; yeniden yazma ile çözmeye çalışmayın.

![OR son durumu doğrulanmadı](screens/or-state-unconfirmed.png)
