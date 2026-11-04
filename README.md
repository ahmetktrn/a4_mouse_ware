# MouseWare

**MouseWare**, Windows 11 için fare tuşlarına işlev atama programıdır. A4Tech FB35 için yazıldı ama Windows'un tanıdığı her farede çalışır. A4Tech'in *8-in-One (8 Gestures)* yazılımının atayamadığı tuşların **hepsine** işlev atayabilmek amacıyla geliştirildi.

> MouseWare bağımsız bir projedir; A4Tech ile bir bağlantısı yoktur.

## Atanabilen tuşlar

| Tuş | Atanabilir mi? |
|---|---|
| Sol tuş, sağ tuş | Evet (sol tuş için ek onay istenir) |
| Tekerlek tıklama | Evet |
| Tekerlek yukarı / aşağı | Evet |
| Sol yandaki 2 tuş (geri / ileri) | Evet |
| Tekerlek sola / sağa eğme | Evet (farede varsa) |
| Tekerleğin üstündeki küçük gri tuş | **Tuş testi** ile kontrol edilmeli (aşağıya bakın) |
| Klavye tuşu gönderen ek tuşlar | Evet ("Son klavye tuşunu tetikleyici yap") |

## Her tuşa atanabilecek eylemler

- **Hazır eylemler:** Geri, ileri, kopyala, yapıştır, geri al, oynat/duraklat, ses aç/kıs/sessiz, masaüstünü göster, görev görünümü, sanal masaüstü değiştir, ekran alıntısı, sekme aç/kapat/değiştir, yakınlaştır… (45+ eylem)
- **Klavye kısayolu:** Tuşlara basarak kaydedilir; fare tuşu basılı kaldıkça kısayol da basılı kalır.
- **Başka bir fare tuşu:** Basma ve bırakma birebir aktarıldığı için sürükleme de çalışır.
- **Diğer eylemler:**
  - Çift tıklama
  - Kaydırma (dikey veya yatay, miktar ayarlı)
  - Program, dosya veya klasör açma
  - Web sitesi açma
  - Metin yazma
- **Pencere işlemleri** (imlecin altındaki pencereye uygulanır): küçült, büyüt/eski boyut, kapat, her zaman üstte.
- **Makro:** Tuşlar, tıklamalar, metin ve beklemelerden oluşan kısa bir betik.
- **8 yönlü jest:** Tuşa basılı tutup fareyi bir yöne çektiğinizde, 8 yönün her biri için ayrı eylem çalışır. Hareket etmeden bırakırsanız tuş normal tıklar. Bu, 8-in-One'daki jest özelliğinin karşılığıdır.
- **Devre dışı** veya **MouseWare'i aç/kapat**.

## Ek özellikler

- **Program profilleri:** Örneğin Chrome'da yan tuşlar sekme değiştirsin, diğer programlarda geri ve ileri olsun. Profilde değiştirmediğiniz tuşlar Varsayılan profilden gelir.
- **Bildirim alanı simgesi:** Pencereyi kapatınca program arka planda çalışmaya devam eder.
- **Windows ile başlatma** ve **Türkçe/İngilizce** arayüz.
- **Acil durum kısayolu:** `Ctrl+Alt+Shift+M` tüm atamaları anında kapatır veya açar. Sol veya sağ tuşa yanlış bir atama yapsanız da fareyi her zaman geri kazanırsınız.

## Kurulum

1. [Releases](https://github.com/ahmetktrn/a4_mouse_ware/releases) sayfasından `MouseWare-Setup-x.y.z.exe` dosyasını indirin. Henüz sürüm yayınlanmadıysa [Actions](https://github.com/ahmetktrn/a4_mouse_ware/actions) sekmesinde son başarılı derlemeyi açıp **Artifacts** bölümünden indirin.
2. Kurulumu çalıştırın. Yönetici izni gerekmez.
3. **Windows SmartScreen uyarısı:** Program dijital olarak imzalanmadığı için "Windows bilgisayarınızı korudu" uyarısı çıkabilir. *Ek bilgi → Yine de çalıştır* ile devam edin.

Kurulum istemiyorsanız `MouseWare-Portable-x.y.z.zip` içindeki `MouseWare.exe` doğrudan çalışır. .NET kurulumu gerekmez.

## İlk kullanım

1. **A4Tech 8-in-One'ı kapatın veya kaldırın.** Bu yazılım yan tuşların gönderdiği sinyali kendisi değiştirir (ekranınızda 4 ve 5 numaralı tuşlar "Double Click" görünüyordu). İki program aynı anda çalışırsa birbirini bozar.
2. MouseWare'de **Tuş testi** sekmesini açın ve faredeki her tuşa sırayla basın:
   - `[hook]` satırı çıkan tuşlar MouseWare ile atanabilir.
   - Bir tuş yalnızca `[raw HID]` satırı üretiyorsa sinyal var ama standart bir tuş değil. Bu satırları kopyalayıp (**Kopyala** düğmesi) paylaşın; desteği eklenebilir.
   - Hiç satır çıkmıyorsa o tuş farenin içinde çalışıyordur (çoğunlukla DPI tuşu) ve hiçbir yazılım onu atayamaz.
3. **Tuş atamaları** sekmesinde bir tuşun yanındaki **Değiştir** düğmesiyle eylem seçin. Değişiklikler hemen geçerli olur ve otomatik kaydedilir.

## Bilinen sınırlar

- **Yönetici pencereleri:** Yönetici olarak açılmış pencerelerde (Görev Yöneticisi gibi) atamalar çalışmaz, çünkü Windows buna izin vermez. Gerekirse *Ayarlar → Yönetici olarak yeniden başlat* kullanın.
- **Oyunlar:** Hile önleme sistemi olan bazı oyunlar yazılımla üretilen tuş basışlarını yok sayabilir.
- **`Win+L`** gibi bazı güvenlik kısayolları Windows tarafından yazılımla tetiklenemez.

## Ayar dosyası

Ayarlar `%APPDATA%\MouseWare\config.json` dosyasında, günlük kayıtları `mouseware.log` dosyasında durur. Ayarları *Ayarlar* sekmesinden dışa ve içe aktarabilirsiniz. Program kaldırılsa da ayar dosyası silinmez.

### Makro dili

Her satıra bir komut yazılır. Komutlar İngilizce veya Türkçe olabilir. `#` ile başlayan satırlar açıklamadır.

```
key Ctrl+C        # tuş      — bas ve bırak
down Shift        # bas      — basılı tut
up Shift          # bırak    — bırak
wait 200          # bekle    — milisaniye
text Merhaba      # yaz      — metin yaz
click left 2      # tıkla    — left/right/middle/x1/x2 (sol/sağ/orta), adet
scroll down 3     # kaydır   — up/down/left/right (yukarı/aşağı/sol/sağ), miktar
```

## Geliştirme

| Klasör | İçerik |
|---|---|
| `src/MouseWare.Core` | Platformdan bağımsız çekirdek: atama motoru, jest hesabı, kısayol ve makro ayrıştırıcı, ayar dosyası |
| `src/MouseWare.App` | Windows uygulaması (WPF): düşük seviyeli fare/klavye hook'u, eylem çalıştırıcı, arayüz, tepsi simgesi |
| `tests/MouseWare.Core.Tests` | Çekirdek için birim testleri |
| `installer/MouseWare.iss` | Inno Setup kurulum betiği |
| `.github/workflows/build.yml` | Windows'ta test, paketleme ve kurulum dosyası üretimi; `v*` etiketiyle otomatik sürüm yayını |

```bash
dotnet test tests/MouseWare.Core.Tests
```

```bash
dotnet publish src/MouseWare.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

```bash
iscc /DAppVersion=0.1.0 /DPublishDir=..\publish installer\MouseWare.iss
```

Yeni bir sürüm yayınlamak için bir etiket gönderin, örneğin `git tag v0.1.0` ve ardından `git push origin v0.1.0`. GitHub Actions kurulum dosyasını Releases sayfasına ekler.

### Nasıl çalışır?

- MouseWare, ayrı bir iş parçacığında Windows'un düşük seviyeli fare hook'unu (`WH_MOUSE_LL`) kurar. Klavye hook'u yalnızca bir klavye tuşu tetikleyici olarak kullanıldığında kurulur.
- Atanmış bir tuşun basılmasını yakalar, Windows'a iletilmesini engeller ve seçilen eylemi `SendInput` ile ayrı bir iş parçacığında çalıştırır. Hook bu sayede hiç beklemez; yavaş hook'ları Windows sessizce kaldırır.
- Her basış kendi bırakmasıyla eşleştirilir. Bu sayede ayar veya program değişse bile hiçbir tuş "basılı" kalmaz.
