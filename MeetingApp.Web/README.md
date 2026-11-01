# MeetingApp.Web

MeetingApp'in Angular 20.3 arayüzü. Kayıt, giriş, hesap ve toplantı ekranlarını içerir.
ASP.NET Core API, SQL Server, Docker, User Secrets ve migration kurulumu için
[ana proje rehberini](../README.md) izleyin.

## Çalıştırma

Yerel geliştirme için Node.js 24 ve npm gerekir. Komutları bu klasörde çalıştırın:

```sh
npm ci
npm start
```

Arayüz [http://localhost:4200](http://localhost:4200) adresinde açılır.
API'nin aynı anda `https://localhost:7085` üzerinde çalışması gerekir. Windows PowerShell'de
yürütme ilkesi hatası alırsanız `npm` yerine `npm.cmd` kullanabilirsiniz.

Visual Studio'da repo kökündeki `MeetingApp.slnx` çözümünü açıp **MeetingApp - API ve Web** profilini
seçerek F5'e basmak API ve Angular'ı birlikte başlatır. `.esproj` içindeki `StartupCommand`,
`npm.cmd start` komutunu çalıştırır; Angular CLI ayrıca global olarak kurulmaz.

## API bağlantısı

HTTP servisleri göreli `/api/` adreslerini kullanır. Tarayıcıya veritabanı bağlantısı,
SMTP parolası veya JWT imzalama anahtarı verilmez.

| Ortam | API'ye yönlendirme |
|---|---|
| `npm start` / Visual Studio | [src/proxy.conf.json](src/proxy.conf.json) → `https://localhost:7085` |
| Docker | [nginx.conf](nginx.conf) → Docker ağı içinde `http://api:8080` |

Geliştirme proxy'si [angular.json](angular.json) içinde tanımlıdır. Production build bu proxy'yi
pakete eklemez; Docker'da nginx `/api/` yolunu koruyarak API'ye iletir ve alt sayfa yenilemelerini
`index.html` dosyasına yönlendirir.

## Ekranlar

| Yol | İşlev |
|---|---|
| `/register` | Kullanıcı bilgileri ve isteğe bağlı JPG/PNG ile kayıt |
| `/login` | E-posta ve parola ile giriş |
| `/meetings` | Kullanıcının toplantı listesi; belge indirme/kaldırma, iptal ve silme |
| `/meetings/new` | Toplantı ve isteğe bağlı PDF/DOCX oluşturma |
| `/meetings/:id/edit` | Mevcut toplantıyı ve dokümanı düzenleme |
| `/account` | Hesap bilgilerini görüntüleme ve e-posta saat dilimini değiştirme |

Hesap ve toplantı rotaları `authGuard` ile korunur. Erişim denetimi API'de JWT ve kayıt
sahipliği kontrolüyle ayrıca yapılır. Oturum `sessionStorage` içinde tutulur; interceptor,
korunan `/api/` isteklerine Bearer token ekler. Süresi dolan veya 401 alan oturum giriş ekranına yönlendirilir.

Kayıt ve giriş sırasında tarayıcının IANA saat dilimi API'ye gönderilir. Toplantı formuna yerel saat
girilir, API'ye UTC gönderilir. Liste ve form tarayıcının saat dilimini kullanır; hesap ekranındaki
saat dilimi tercihi ise e-postada gösterilecek saati belirler.

## Dosya düzeni

```text
src/app/
  core/auth/          Oturum servisi, guard, interceptor ve tarayıcı saat dilimi
  core/meetings/      HTTP servisi, veri tipleri ve tarih dönüşümleri
  pages/login/       Giriş formu
  pages/register/    Kayıt formu ve profil resmi seçimi
  pages/account/     Hesap bilgileri ve saat dilimi tercihi
  pages/meetings/    Toplantı listesi ve işlemler
  pages/meeting-form/ Toplantı oluşturma/düzenleme formu
  app.routes.ts      Rotalar ve erişim guard'ları
  app.config.ts      Router ve HTTP sağlayıcıları
```

Component'ler standalone yapıdadır. Formlarda Reactive Forms ve kullanıcıya gösterilen doğrulama
mesajları kullanılır. Sunucudan gelen doğrulama hataları da formda gösterilir.

## Test ve build

Jasmine/Karma testleri; form doğrulama, oturum, HTTP istekleri, tarih dönüşümleri ve toplantı işlemlerini kapsar.
ChromeHeadless için Google Chrome kurulu olmalıdır:

```sh
npm test -- --watch=false --browsers=ChromeHeadless
```

Etkileşimli test çalıştırma:

```sh
npm test
```

Production build:

```sh
npm run build
```

Çıktı `dist/meeting-app/browser` klasöründedir; Dockerfile bu dosyaları nginx imajına kopyalar.
Repoda yapılandırılmış bir e2e test builder'ı yoktur. Gerçek API, SQL ve Mailpit ile elle kontrol akışı
[ana README'nin test bölümünde](../README.md#testler-ve-kontrol) anlatılır.

## Docker

Ana rehberdeki `.env` kurulumunu tamamlayıp repo kökünde `docker compose up --build` çalıştırın.
Arayüz varsayılan olarak [http://localhost:8080](http://localhost:8080) adresindedir.
Yalnızca bu klasördeki Dockerfile'ı çalıştırmak SQL Server ve API'yi başlatmaz.
