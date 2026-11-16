# Violet Studio (.NET)

Visual Studio Community ile açılıp F5 üzerinden çalıştırılabilecek ASP.NET Core web projesi.

## Visual Studio'da çalıştırma

1. `VioletStudio.sln` dosyasını Visual Studio Community 2026 ile aç.
2. Gerekirse ASP.NET ve web geliştirme iş yükünü Visual Studio Installer'dan ekle.
3. Başlangıç profili olarak `VioletStudio.Web` seçip F5'e bas. Tarayıcı otomatik açılır.

Proje .NET 10 hedefler; bu makinede .NET 10 SDK ve Visual Studio 2026 Community kurulu. .NET 10 hedefi Visual Studio 2026 18.0 veya üzerini gerektirir.

## SQL Server

Geliştirme bağlantısı `.\\SQLEXPRESS` örneğini ve Windows kimlik doğrulamasını kullanır. İlk başarılı çalıştırmada uygulama `VioletStudio` veritabanını ve `Users`, `Orders`, `LicenseKeys` tablolarını oluşturur. Hesap şifreleri ayrı salt ile PBKDF2-SHA256 hash olarak saklanır.

Bu makinede SQL Server Express örneği kurulu, ancak geliştirme sırasında SQL hizmetine erişim alınamadı. Visual Studio'yu F5 ile çalıştırmadan önce **SQL Server Configuration Manager**'ı yönetici olarak açıp **SQL Server (SQLEXPRESS)** hizmetini başlat. Servis başlatma izni verilmezse hesap ve sipariş uçları veritabanına bağlanamayacağı için çalışmaz; arayüz açılır ve SQL bağlantı uyarısı gösterir.

Pro planı sipariş oluşturma akışı kullanıcı hesabına bağlı siparişi `PendingPayment` olarak kaydeder. Ödeme sağlayıcısı henüz entegre olmadığı için bu kayıt ödeme alınmış anlamına gelmez.

## Yönetici hesabı

- Kullanıcı adı: `Apoaxp`
- Parola: kullanıcı tarafından belirlenen yerel admin parolası.
- Yönetici paneli sol menüdeki **Yönetici paneli** bağlantısından açılır.

Kimlik bilgisi ham parola olarak kaydedilmez; yerel `admin.local.json` içinde PBKDF2-SHA256 hash ve salt tutulur. Bu dosya `.gitignore` kapsamındadır. Admin oturumu uygulama belleğinde tutulur; uygulama yeniden başlatılınca sona erer. Lisanslar SQL'de saklanır. Bu prototip yerel geliştirme içindir; genel internete açma.

## Prototip kapsamı

Video bağlantısı çözümlendiğinde kalite seçenekleri o videonun mevcut formatlarından oluşturulur. İndirilen dosyalar Windows'un varsayılan İndirilenler klasörüne kaydedilir; video birleştirme ve MP3 dönüştürme için FFmpeg, YouTube JavaScript çözümlemesi için Node.js kurulu olmalıdır. `VioletStudio.Web/tools/yt-dlp.exe` video formatlarını çözümlemek ve indirmek için kullanılır; [yt-dlp'nin resmi Windows sürümünü](https://github.com/yt-dlp/yt-dlp/releases/latest) ve üçüncü taraf lisans dökümünü aynı klasöre ekledim. YouTube desteği için uygulama yt-dlp'nin resmi `ejs:github` bileşenini internetten alır. Özel veya giriş isteyen içerikler yine çözümlenemeyebilir. Ödeme sağlayıcısı ve gerçek ödüllü reklam bağlantısı henüz yoktur.
