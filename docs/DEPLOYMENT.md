# Ücretsiz demo yayını

Bu rehber MeetingApp'i MonsterASP.NET Free üzerinde incelemeye açık bir demo olarak hazırlamak içindir.
ASP.NET Core API ve MSSQL korunur. Angular'ın production çıktısı API paketinin `wwwroot` klasörüne eklenir;
tarayıcı hem ekranları hem `/api/` isteklerini aynı site adresinden alır. Yerel Docker kurulumu ayrı kalır.

Demo adresi: [https://meetingapp-nida.runasp.net/login](https://meetingapp-nida.runasp.net/login).

## Mevcut yayında doğrulananlar

| Kontrol | Sonuç |
|---|---|
| HTTPS | Standart TLS sertifika doğrulaması başarılı; HTTP isteği HTTPS'e 307 ile yönleniyor |
| SQL bağlantısı | `/health/ready`: HTTP 200, JSON `ready` |
| Angular | `/login` ve `/meetings/42/edit`: HTTP 200, Angular uygulama kabuğu |
| Girişsiz API erişimi | `/api/meetings` ve `/api/auth/me`: HTTP 401 |
| Ayrılmış yollar | Bilinmeyen API yolu, `/Storage` ve kapalı OpenAPI belgesi: HTTP 404 |
| Kullanıcı denemesi | Yeni hesapla giriş, belgeli toplantı oluşturma, belge indirme, mevcut belgeyi koruyarak düzenleme ve iptal kullanıcı tarafından denendi |
| Canlı e-posta | Gmail SMTP gönderimi açık; outbox gönderim tarihleri ve yeni kayıt/toplantı mesajlarının alıcının gelen kutusuna ulaştığı doğrulandı |

Canlı ortamda doğrudan silme, worker ile kalıcı silme/trigger ve yeniden başlatma sonrası dosya kalıcılığı henüz
ayrıca doğrulanmadı. Yukarıdaki HTTP kontrolleri salt okunur isteklerdir; tam otomatik canlı uçtan uca
testin yerine geçmez. Canlı yayında Gmail SMTP ayarları tanımlı ve `EmailDelivery__Enabled=true`.
02.11.2026 tarihinde bekleyen 8 bildirimin tamamında `SentAtUtc` dolduğu ve `LastError` temizlendiği
outbox üzerinden doğrulandı. Sonrasında yeni hesap kaydı ve toplantı oluşturma denemesinde her iki
e-postanın alıcının gelen kutusunda göründüğü kullanıcı tarafından doğrulandı.

## Ücretsiz planın sınırı

Sağlayıcıya göre istek almayan uygulama üç saat sonra durdurulabilir; Free planda AlwaysRunning yoktur.
Bu nedenle e-posta ve toplantı temizleme worker'ları site uykudayken çalışmaz. Sonraki ziyaret API'yi
yeniden açınca işleri işlemeye devam eder. Bu yayın, arka plan işlerinin kesintisiz çalıştığı bir canlı
ortam taahhüdü değildir. [Sağlayıcının açıklaması](https://help.monsterasp.net/websites/configuration/always-running).

## 1. Hosting ve boş MSSQL veritabanı

1. Kendi MonsterASP.NET hesabınızı oluşturun ve kontrol panelinden Free bir website açın.
2. Website için .NET sürümünü **.NET 10** seçin.
3. Bu demoya ait yeni ve boş bir **MSSQL** veritabanı oluşturun. Bilgisayarınızdaki mevcut
   `MeetingAppDb` veritabanını kopyalamak veya silmek gerekmez.
4. Veritabanı ayrıntılarından website içinde kullanılacak bağlantı cümlesini alın.
   Sağlayıcının yerel/iç bağlantısı ile dışarıdan SSMS için verdiği bağlantı farklı olabilir.

Bağlantı ve hesap parolalarını kaynak dosyalara veya GitHub'a koymayın. Ortam değişkenlerini hosting
panelinde saklayacağız. Veritabanı ayarları için [resmî rehber](https://help.monsterasp.net/websites/frameworks/aspnet-core-with-mssql).

## 2. Site ayarları

Website panelinde **Scripting → Environment variables** bölümünü açın. Aşağıdaki değişkenleri
bu website'e ekleyin; büyük harfli yer tutucular gerçek değerlerle değiştirilmelidir.

Mevcut yayında Production, bağlantı cümlesi, JWT anahtarı, frontend hosting, açılış migration'ı,
e-posta gönderimi, Swagger ve HTTPS yönlendirme ayarları panelde tanımlıdır. Tablodaki issuer/audience,
token süresi, migration tekrarları ve temizlik süreleri uygulamanın mevcut varsayılanlarıyla da çalışır.

| Değişken | Değer |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `ConnectionStrings__DefaultConnection` | Yeni hosting MSSQL veritabanının bağlantı cümlesi |
| `Jwt__SigningKey` | En az 32 UTF-8 bayt uzunluğunda rastgele ve özel bir anahtar |
| `Jwt__Issuer` | `MeetingApp.Api` |
| `Jwt__Audience` | `MeetingApp.Web` |
| `Jwt__ExpirationMinutes` | `30` |
| `FrontendHosting__Enabled` | `true` |
| `DatabaseStartup__ApplyMigrations` | `true` |
| `DatabaseStartup__MaxAttempts` | `6` |
| `DatabaseStartup__RetryDelaySeconds` | `5` |
| `Swagger__Enabled` | `false` |
| `HttpsRedirection__Enabled` | `false`; HTTPS yönlendirmesi hosting panelinden açılacak |
| `EmailDelivery__Enabled` | İlk kurulum ve doğrulamada `false` |
| `MeetingCleanup__Enabled` | `true` |
| `MeetingCleanup__PollIntervalSeconds` | `60` |
| `MeetingCleanup__RetentionHours` | `24` |

Örnek bağlantı şekli aşağıdadır; panelin verdiği sunucu, kullanıcı, veritabanı ve sertifika ayarlarını kullanın:

```text
Server=SQL_SUNUCUSU;Database=VERITABANI_ADI;User Id=DB_KULLANICISI;Password=DB_PAROLASI;Encrypt=True;TrustServerCertificate=False
```

User Secrets ve yerel `.env` dosyası hosting'e aktarılmaz. Ayar değişiklikleri uygulama yeniden
başlatıldığında yüklenir; panelde **Restart application** kullanılır.
[Ortam değişkenleri rehberi](https://help.monsterasp.net/websites/configuration/environment-variables).

## 3. Yayın paketini hazırlama

Bilgisayarda .NET 10 SDK, Node.js 24 ve npm kurulu olmalıdır.
Paketlemeden önce Visual Studio'da çalışan projeleri **Shift+F5** ile ve ayrı açılmış Angular
geliştirme sunucusunu kendi terminalinde **Ctrl+C** ile durdurun. `npm ci` bağımlılıkları yeniden
kurar; çalışan geliştirme sunucusu Windows'ta `esbuild.exe` dosyasını kilitleyebilir.

### Visual Studio'dan klasöre publish

1. Çözümde **MeetingApp.Api → sağ tık → Yayımla** seçeneğini açın.
2. Yerel **FolderDemo** profilini seçin. Profil bir hosting hesabına bağlanmaz ve parola içermez.
3. Release yapılandırmasıyla **Yayımla** çalıştırın.
4. Profil, repo kökünün `artifacts/demo/folder-<benzersiz-kimlik>/site` klasöründe çıktı oluşturur.

Bu profilin `PublishFrontend=true` ayarı API publish sırasında Angular için `npm ci` ve production build
çalıştırır. Arayüzü ayrıca elle kopyalamak gerekmez. Sadece `site` klasörünün **içeriği** yayımlanır.

### ZIP hazırlama alternatifi

Repo kökünde:

```powershell
.\scripts\Publish-Demo.ps1
```

İsteğe bağlı başka bir çıktı ana klasörü:

```powershell
.\scripts\Publish-Demo.ps1 -OutputFolder "C:\DemoPaketleri"
```

Script her çalıştırmada yeni bir klasör oluşturur; önceki çıktıları silmez ve hosting'e yükleme yapmaz.
Sonuçta `SiteDirectory` ve `ArchivePath` gösterilir. `MeetingApp-demo.zip` içinde doğrudan
`MeetingApp.Api.dll`, `web.config` ve Angular dosyalarını içeren `wwwroot` bulunur; fazladan `site` üst klasörü yoktur.
Build başarısızsa ZIP oluşturulmaz. API publish hedefi npm komutlarını bir kez çalıştırır.

Çıktı klasörleri Git'e gönderilmez. Gerçek `.publishSettings` dosyası veya parolalı hosting publish profili
bu repo içindeki örnek FolderDemo profilinin yerine kaydedilmemelidir.

## 4. Paketi website'e yükleme

Website panelinde **Files** bölümünden ZIP'i yükleyin, **Unzip** seçeneğiyle website'in uygulama köküne
(sağlayıcıda genellikle `/wwwroot`) çıkarın. DLL ve `web.config` doğrudan bu kökte olmalıdır.
Yayın paketinin içindeki Angular `wwwroot` alt klasörü yerinde kalır.
[ZIP yükleme rehberi](https://help.monsterasp.net/websites/deploy/zip-file).

Sunucuda `Storage` klasörü kullanıcıların yüklediği dosyaları tutar; yeniden dağıtımda silinmemelidir.
Yerel paket bilgisayardaki fotoğraf/doküman kayıtlarını içermez. İlk dosya yüklemesinde uygulamanın
`Storage/ProfileImages` ve `Storage/MeetingDocuments` oluşturup yazabildiği doğrulanır.

Visual Studio'dan doğrudan Web Deploy da kullanılabilir. Panelde Deploy altında WebDeploy'u açıp
sağlayıcının profilini kendi bilgisayarınıza indirin; API publish ayarına `PublishFrontend=true`
eklenmiş olduğundan emin olun. Bu özel profil/kimlik bilgileri Git'e eklenmez.
[Web Deploy rehberi](https://help.monsterasp.net/websites/deploy/visual-studio).

Uygulamayı geçici olarak `app_offline.htm` ile durdurduysanız dosyalar, ortam ayarları ve HTTPS
hazırlığı tamamlandıktan sonra bu dosyayı kaldırın. Dosya yerinde kaldığında hazırlık sayfasıyla
HTTP 503 alınabilir; bu durum tek başına uygulama hatası değildir. Ardından HTTPS üzerinden
`/health/ready` isteğinin HTTP 200 döndürdüğünü kontrol edin.

## 5. HTTPS etkinleştirme

Site dosyalarını yükledikten sonra website'in **HTTPS** sayfasını açın ve alan adı satırındaki
**Enable HTTPS → Let's Encrypt** seçeneğini deneyin. Bu demo için kullanılan Free hesapta bu işlem
panelden doğrudan başarıyla tamamlandı; destek talebi gerekmedi.

Seçenek hesabınızda görünmüyorsa veya aktivasyon yapılamıyorsa kontrol panelinden destek talebi açın.
Sağlayıcının Free plan rehberi bu alternatifi de anlatır.
Aktivasyondan sonra `https://` adresinin geçerli sertifikayla çalıştığını doğrulayın ve panelde **HTTPS Redirect** açın.
Kayıt/giriş ve diğer kişisel veri denemeleri HTTPS hazır olduktan sonra yapılır.
[Free plan HTTPS adımları](https://help.monsterasp.net/domains-https/https/lets-encrypt).

## 6. Veritabanı ve uygulama doğrulaması

API ilk açılışta yalnızca ayarladığınız yeni hosting veritabanına migration uygular. Bağlantı/DDL izni
başarısızsa uygulama sağlıklı başlamaz; bu durumda hosting logları ve bağlantı cümlesi kontrol edilir.

HTTPS üzerinden şu akışı kontrol edin:

1. `/health/ready` HTTP 200 döndürmeli; yeni DB'de migration tabloları ve silme trigger'ı oluşmalı.
2. Ana sayfa ve `/login` açılmalı; `/meetings` sayfasını yenilemek 404 vermemeli.
3. Sahte/test bilgileriyle profil resimli kayıt, giriş ve dokümanlı toplantı çalışmalı.
4. Ekrandaki yerel saatin SQL'deki UTC karşılığı doğru olmalı.
5. Güncelleme, dosya indirme, iptal ve süresi dolan deneme kaydının silme logu kontrol edilmeli.
6. Uygulama restart'ından sonra aynı kullanıcı/toplantı ve yüklenen dosyalar korunmalı.

`EmailDelivery__Enabled=false` iken hoş geldiniz/toplantı mesajı outbox'a yazılır fakat SMTP'ye gönderilmez.
İlk kontrollerde gerçek kişilere e-posta gönderilmez; mesaj gövdesi hosting SQL outbox tablosundan okunur.

## 7. Gerçek SMTP yapılandırması ve teslim kontrolü

Gerçek e-posta teslimi isteniyorsa bir SMTP hizmeti ve bu hizmetin kabul ettiği gönderici adresi gerekir.
Hosting panelinde `Smtp__Host`, `Smtp__Port`, `Smtp__Security` (`StartTls` veya `SslOnConnect`),
`Smtp__Username`, `Smtp__Password`, `Smtp__FromAddress` ve `Smtp__FromName` tanımlanır.
Kullanıcı/parola TLS ile birlikte kullanılır; `Smtp__AllowUnencryptedRelay=false` kalır.
Kurulum doğrulanıp test alıcısı belirlendikten sonra `EmailDelivery__Enabled=true` yapılabilir.

Mevcut canlı yayında Gmail için `465` / `SslOnConnect` kullanılıyor; `587` / `StartTls` diğer
desteklenen eşleşmedir. Gönderici adresi ve SMTP kullanıcı adı aynı Gmail hesabına ait; parola olarak
bu hesaptan oluşturulan uygulama şifresi kullanılıyor. Bu özel değerler hosting panelinde tutulur.
Önceki `5.7.8 BadCredentials` hatası hesap ve uygulama şifresi eşleştirilerek giderildi. Ayarları
kaydettikten sonra uygulama yeniden başlatıldı; outbox gönderim durumu ve yeni hoş geldiniz/toplantı
mesajlarının alıcıda görünmesi doğrulandı. Gelecekteki SMTP ayar değişikliklerinde de hem `SentAtUtc`
hem alıcının gelen kutusu kontrol edilmelidir; SMTP kabulü tek başına gelen kutusu teslimini kanıtlamaz.

Mailpit bir test kutusudur; bu hosting yayınında alıcıya teslim eden SMTP yerine geçmez.
SMTP açılmadan önce outbox'taki eski bekleyen mesajların da worker tarafından işleneceğini göz önünde bulundurun.

Henüz denenmemiş canlı işlemler doğrulandıkça yukarıdaki durum tablosunu ve README'deki canlı demo
bölümünü güncelleyin; ücretsiz planın worker sınırını belirtmeye devam edin.
