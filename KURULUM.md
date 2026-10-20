# RUSTED FAIR — Kurulum ve Yayın Rehberi

Birinci şahıs, yatay ekranda oynanan bir mobil korku oyunu (iOS + Android). Unity 6 ve URP ile yapıldı.
Dünya, robotlar, arayüz, sesler ve dokular koddan ve üretilmiş dosyalardan kuruluyor. Hazır model, prefab ya da sahne kurmanız gerekmiyor.

---

## 1. İlk açılış (yaklaşık 10 dakika)

1. **Unity Hub'ı açın** ve **Unity 6 LTS** (6000.0.x) sürümünü kurun. Kurarken **Android Build Support** ve **iOS Build Support** modüllerini de seçin.
2. **Yeni Proje → "Universal 3D"** şablonunu seçin. URP bu şablonla birlikte geliyor.
3. Bu klasördeki **`Assets/RustedFair`** klasörünü projenizin `Assets/` klasörüne kopyalayın.
   - `Tools/` klasörü isteğe bağlıdır. Ses ve dokuları yeniden üreten Python scriptleri orada duruyor.
4. Unity'de **Window → Package Manager → "+" → Add package by name** yolunu izleyin ve şunu yazın:
   - `com.unity.purchasing` (sürüm: **4.12.2**). Bu, reklam kaldırma satın alımları içindir.
5. Üst menüden **Rusted Fair → Setup Project (run once)** komutunu çalıştırın. Bu komut:
   - katmanları, materyalleri ve sahneyi oluşturur,
   - yatay ekran, paket kimliği, IL2CPP/ARM64, 120 Hz ve URP Forward+ ayarlarını yapar,
   - uygulama ikonunu atar.
6. Açılan **RustedFair** sahnesinde **Play**'e basın.

**Editörde test kontrolleri:**

| Tuş | İşlev |
|---|---|
| WASD | Yürü |
| Shift | Koş |
| C | Eğil |
| E | Etkileşim (basılı tutma gereken işlerde basılı tutun) |
| F | El feneri |
| Q | Maske |
| G | Eşya fırlat |
| Tab | Eşya değiştir |
| J | Günlük |
| Esc | Duraklat |
| Sağ tık | Fare ile bakışı aç/kapat |

Her gecenin kasa şifresi Console'a yazılıyor. Test ederken kolaylık sağlar.

> Projeyi "Input System" açık olarak kurduysanız da sorun yok. Kod hem eski hem yeni giriş sistemini destekliyor.

---

## 2. Reklamlar (Google AdMob)

Reklam sistemi kodda hazır. AdMob eklentisini kurduğunuzda kendiliğinden devreye giriyor:

1. **Google Mobile Ads Unity Plugin**'i kurun. GitHub'daki `googleads/googleads-mobile-unity` deposunun Releases sayfasından `.unitypackage` dosyasını indirip içe aktarın.
2. **Assets → Google Mobile Ads → Settings** ekranını bir kez açın (ayar dosyası oluşsun). App ID'leri oraya elle yazmanıza gerek yok: v1.0.0'dan beri tüm kimlikler `Scripts/Services/StoreConfig.cs` içinde (bkz. bölüm 18) ve derleme sırasında oraya kopyalanıyor.
3. Reklam birimi ID'leri de `StoreConfig.cs` içinde. Editörde ve Development Build'de her zaman Google'ın test ID'leri kullanılır.
4. **Rusted Fair → Refresh SDK Defines** komutunu çalıştırın. Bu, `RF_ADMOB` tanımını ekler.

**Reklam kuralı:** İlk ölümde reklam çıkmaz. Reklam en az **2 ölümde bir** ve **en az 150 saniye arayla**, ölüm ekranında "Devam"a basınca gösterilir.
Bu değerler `AdsManager.cs` içindeki `DeathsPerAd` ve `MinSecondsBetweenAds` sabitlerinden değiştirilebilir. Avrupa için gereken onay formu (UMP) da hazır.

## 3. Reklam kaldırma aboneliği (IAP)

Mağaza panellerinde iki ürünü **aynı kimliklerle** oluşturun:

| Ürün | Kimlik | App Store türü | Google Play türü |
|---|---|---|---|
| Aylık | `com.lyfestudios.rustedfair.noads.monthly` | Auto-Renewable Subscription (1 ay) | Subscription (1 ay, otomatik yenilenen) |
| Ömür boyu | `com.lyfestudios.rustedfair.noads.lifetime` | Non-Consumable | Uygulama içi ürün (tek seferlik) |

- Fiyatlar mağazadan otomatik çekilir.
- "Satın Alımları Geri Yükle" butonu Ayarlar'da ve Mağaza panelinde var. Apple bu butonu zorunlu tutuyor.
- Gizlilik politikası ve kullanım şartları adresleri `Scripts/Services/StoreConfig.cs` içinde (`PrivacyUrl`, `TermsUrl`). **Abonelik içeren uygulamalarda ikisi de zorunlu.**

## 4. Build

- **Android:** File → Build Profiles → Android → Switch Platform. Keystore ve AAB için bölüm 18'e bakın.
- **iOS:** iOS profiline geçip Build alın. Xcode'da imzalamayı yapın. 120 Hz ProMotion ayarı (`CADisableMinimumFrameDurationOnPhone`) otomatik ekleniyor.
- Paket kimliği `com.lyfestudios.rustedfair` (Android ve iOS); **Rusted Fair → Setup Project** bunu ve tüm mağaza ayarlarını uygular (bölüm 18).

---

## 5. Oyunun içeriği

**Hikâye:** Kapalı, devasa bir fuarda gece kalıyorsunuz. Depodaki robotlar bir elektrik kaçağıyla uyanıyor. Çıkış kapısı yalnızca güvenlik odasındaki kasada duran acil durum kartıyla açılıyor. Kasanın 7 haneli şifresi her oyunda rastgele belirleniyor ve 7 nota bölünmüş durumda.

**Harita:** Yaklaşık 144 × 112 m büyüklüğünde, 18 bölüm.

**7 görev:** Malzemelerin yerleri her oyunda değişiyor.

| # | Görev | Ne yapılıyor |
|---|---|---|
| 1 | **Elektrik** | 3 sigortayı bul (yemek alanı, hayalet treni, çarpışan arabalar gibi uzak yerler) → sigorta kutusuna tak → jeneratör kolunu çek. Kol gürültülüdür. |
| 2 | **Atlıkarınca** | Bilet kulübesindeki çekmeceden dolap anahtarını al → personel odasındaki 13 numaralı dolabı aç → kolu al → atlıkarıncayı çevir → gizli bölme açılır. |
| 3 | **Falcı Zelda** | 3 altın jeton bul (biri ayna labirentinin çıkmaz sokağında olabilir) → makineye at. |
| 4 | **Hayalet Treni** | Depo ofisinin çekmecesinden tren anahtarını al → kontrol paneline tak → karanlık ve kıvrımlı tüneli geç (yolda korkutma tuzakları var) → tabutun kapağını it. |
| 5 | **Anons** | Kaseti anons sistemine tak. Anons bütün robotları kabine çağırır, saklanman gerekir. Anons bitince çekmece açılır. |
| 6 | **Soğuk Oda** | Mutfak anahtarını bul → soğuk odayı aç → buzun içindeki notu çekiçle kır. |
| 7 | **Büyük Saat** | 3 pirinç dişliyi saate tak → saat gece yarısını vurur → otomat bebek notu uzatır. |

**Görevlerden sonra:** 2. görev biter bitmez maske verilir. Notlar tamamlanınca kasa açılır, kart alınır ve kapıya gidilir. Kapı alarmı bütün robotları uyandırır; kapı açılana kadar hayatta kalmanız gerekir. Kaçışla oyun biter.

**Robotlar:**

| Robot | Tarif | Özelliği |
|---|---|---|
| **Mister Grin** | Uzun boylu sunucu, yüz plakası kırık | Hızlı ve görüşü iyi. Gecenin başında uyanır. |
| **Rattle** | Yarısı kopmuş tavşan, kollarıyla sürünüyor | Gözü kötü ama kulağı çok iyi. Koşarken atılarak ilerler. 1. görevden sonra uyanır. |
| **Brutus** | Çürümüş dev ayı | Yavaş ama durmaz. Kapıları kırar ve dolapları kontrol eder. Yağda kaymaz. 3. görevden sonra uyanır. |

**Hayatta kalma araçları:**
- **Maske:** Yakındaki robotları uzaklaştırır. 3,5 saniye etkili, 15 saniye bekleme süresi var.
- **Yağ kutusu:** Robot üstüne basınca kayıp düşer. Brutus sadece yavaşlar.
- **Su balonu:** Robotu kısa devre yaptırıp sersemletir.
- **Kurmalı oyuncak:** Ses çıkarır, robotları kendine çeker.
- **El feneri:** Pili bitebilir, haritada yedek piller var.
- **Saklanma yerleri:** Dolaplar, fotoğraf kabini, tabutlar, büyük sandık, dolap kabinleri, saat kasası.
  - Bir robot sizi saklanırken görürse içeriden çeker.

**Can ve zorluk:** Kolay 7, Normal 5, Kâbus 3 can. Yakalanınca ilerlemeniz korunur ve girişte uyanırsınız.

**Atmosfer:**
- Uyanırken gözler bulanık açılıp kapanıyor.
- Ambiyans çoğunlukla sessiz ve tekinsiz; arada uzaktan metal sesleri ve fısıltılar geliyor.
- Robot sizi fark edince ani bir vurgu sesi çalıyor, kovalamaca başlayınca kovalamaca müziği giriyor.
- Robot yaklaştıkça kalp atışı duyuluyor ve ışıklar titriyor.
- Jumpscare'de ekran kırmızıya dönüyor, kan lekeleri beliriyor ve telefon titriyor.

**Başarımlar:** Bronz, gümüş ve altın rozetli 23 başarım var.

**Diller:** İngilizce (ana dil), Türkçe, Almanca, Fransızca, İspanyolca, İtalyanca, Portekizce, Rusça, Lehçe, Endonezce, Vietnamca, Japonca, Korece, Çince (v1.10'dan beri 14 dil).

---

## 6. Ayar noktaları (kod)

| Ne | Nerede |
|---|---|
| Oyuncu hızları, sprint süresi, maske süresi/bekleme | `Scripts/Player/PlayerController.cs` (en üstteki sabitler) |
| Robot hızları, görüş, duyma mesafesi | `Scripts/AI/RobotAI.cs` → `Configure()` |
| Robotların hangi görevden sonra uyandığı | `Scripts/Core/GameController.cs` → `OnTaskDone()` |
| Can sayısı | `GameController.Awake()` |
| Reklam sıklığı | `Scripts/Services/AdsManager.cs` |
| Harita ve oda yerleşimi | `Scripts/World/FairBuilder.cs` → `DefineLayout()` |
| Oda dekorları | `Scripts/World/FairProps.cs` |
| Görevler ve eşya konumları | `Scripts/Gameplay/Quest.cs` |
| Metinler | `Resources/RF/Localization/*.txt` |

## 7. Performans

- **Grafik seviyesi:** Düşük, Orta ve Yüksek seviyeler var; ilk açılışta cihaza göre otomatik seçiliyor. Seviye değişince render ölçeği, MSAA, gölgeler ve efektler değişiyor.
- **FPS hedefi:** Ayarlardan 30, 60 ya da 120 seçilebiliyor. Varsayılan 120.
- **Işıklar:** Sadece oyuncuya en yakın gerçek ışıklar açık (v1.5'ten beri 3 / 5 / 8 adet, kaliteye göre); diğer lambalar ucuz bir hale ile görünüyor. Gölgeyi yalnızca el feneri düşürüyor.
- **Geometri:** Statik geometri 72 metrelik parçalara bölünüp malzemeye göre birleştiriliyor (v1.4). Bu, az sayıda çizim çağrısı ve iyi bir görüş alanı kırpması sağlıyor.

## 8. Bilinmesi gerekenler

- **Test durumu:** Kod, Unity olmayan bir ortamda yazıldı ve derleme tutarlılığı ayrı bir derleyiciyle kontrol edildi. Ancak **Unity içinde henüz çalıştırılmadı**. İlk açılışta Console'da hata çıkarsa hata metnini bana gönderin, hemen düzeltirim.
- **Modeller:** Robotlar ve dekorlar Unity'nin temel şekillerinden koddan üretiliyor. Görünüm stilize ama atmosferik. Daha gerçekçi model isterseniz (örneğin Asset Store'dan) robotların görselleri `RobotRig.cs` üzerinden değiştirilebilir.
- **Ses ve doku kaynağı:** Sesler ve dokular Python ile sentezlendi ve telif sorunu yok. `Tools/gen_audio.py` ve `Tools/gen_textures.py` ile yeniden üretilebilirler.
- **Fontlar:** Cinzel, Oswald (SIL OFL) ve Special Elite (Apache 2.0). (Creepster v1.6'da kaldırıldı.) Lisansları `Assets/RustedFair/Licenses` klasöründe.

---

## 9. v1.1 değişiklikleri

- **Arayüz sadeleşti:** Butonların ve yazıların arkasındaki kutular kaldırıldı. Butonlar artık ortalanmış, büyük harfli, hafif gölgeli düz yazı. Ana butonlar (Oyna, Devam, Tamam) kırmızı Creepster fontuyla ve daha büyük; ikincil butonlar daha soluk.
- **Ana menü ortada:** Logo, slogan, Oyna, "Zorluk · Dil" satırı, Ayarlar ve Nasıl oynanır alt alta ortalı. Başarımlar, Reklamları kaldır ve Çıkış küçük bir satırda.
- **Paneller:** Kart/kutu arka planı yok; tam ekran koyu perde üstünde ortalı içerik. Paneller, menü sütunu ve brifing kısa yatay telefonlara (ör. 1920x864, çentikli ekranlar) sığacak şekilde otomatik küçülüyor (`UIFit`). Uzun listeler kaydırılabiliyor.
- **Görev brifingi:** Oyun başında gözler açıldıktan sonra, oyuncu kontrolü almadan önce "Gecenin Görevi" ekranı çıkıyor (amaç, 3 adım, ilk görevin ayrıntılı açıklaması). Her görev tamamlanıp not kapatıldığında yeni görevin ayrıntısı ve açık diğer görevler gösteriliyor. 7 görev bitince kaçış adımı anlatılıyor. Brifing açıkken oyun duraklıyor.
- **GÖREVLER butonu:** Sol üstteki eski günlük butonu, simgeli ve yazılı GÖREVLER butonu oldu (J tuşu da çalışıyor). Açılan ekranda 7 görev ve kaçış adımı durumlarıyla (✓ tamam / ▸ şu an / ○ açık), ilerlemeleri ve ayrıntılı açıklamalarıyla listeleniyor. Bulunan şifre haneleri de burada. Yeni bir brifing okunmamışsa butonda kırmızı nokta yanıp sönüyor.
- **Nasıl oynanır:** Menüdeki "Günlük" butonunun yerine geldi; duraklatma menüsünden ve Görevler ekranından da açılıyor.
- **Ses:** Ağır nefes sesi kısıldı (0.75 → 0.22). Saklanma ve uyanma nefesleri yaklaşık %58'e indi.
- **Robotlar yavaşladı:** Grin 1.9 / 4.3, Rattle 1.7 / 4.6 (atılma çarpanı yumuşadı), Brutus 1.5 / 3.7 (yürüme / kovalama). Görev başına hızlanma 0.025 → 0.015.
- **Dokunmatik kontroller:** Daha şeffaf ve hafif. İpuçları yine sağ altta, ama artık kutusuz ve gölgeli yazı olarak.
- **Diller:** Yeni metinler 11 dilin hepsine eklendi (her dosyada 271 anahtar var).
- **Fontlar:** Durum simgeleri (✓ ▸ ○) paketteki fontlarda yok. `RFImport` font içe aktarılırken sistemdeki sembol fontlarını yedek olarak ekliyor. Simgeler kare görünürse `Assets/RustedFair/Resources/RF/Fonts` klasörüne sağ tıklayıp **Reimport** yapın.
- **Sürüm:** Mağazaya yüklemeden önce *Project Settings → Player → Version* değerini `1.1.0` yapın ve Bundle Version Code'u artırın.

## 10. v1.2 değişiklikleri

- **Robotlar duvardan geçmiyor:** Rattle'ın modeli %82'ye küçültüldü. Robotların gövdesine ek "yoklama" daireleri eklendi (`RobotAI.BodyResolve`); kafa, kollar ve uzun gövde artık duvarların ve ana kapının içine girmiyor. Kapılardan geçiş etkilenmedi.
- **İçinden geçilen eşyalar düzeltildi:** Sandalyeler (devrik olan dahil), soğuk odadaki asılı çarşaflar, depo ofisindeki masa, turnike kolları, sahne perdesi, kadife ip, çocuk roketi, çöp kutuları ve kova artık katı. Çarpışan araba ofisindeki TV metal bir dolabın üstünde duruyor. Hayalet trenindeki bez hayaletler yükseltildi, artık kafa içinden geçmiyor.
- **Daha dolu odalar:** `Scripts/World/FairDressing.cs` her odaya duvar dibine eşyalar (kutu yığınları, çöp kutuları, istif sandalyeler, animatronik hurdası, dosya dolapları, ödül rafları, kostüm askıları, dede saatleri, mezar taşları, tabutlar, pençe makineleri, paletler, ocak, buzdolabı, kanepe vb.), açık zemine serbest eşyalar ve duvarlara kan izleri ile loş aplikler ekliyor. Atlıkarınca salonuna patlamış mısır arabaları, güç ölçer, halka atma, ördek havuzu, palyaço fotoğraf panosu, çocuk arabaları ve titrek lamba direkleri geldi; giriş holüne lamba direkleri ve bilgi panosu. Eşyalar kapıların, görev noktalarının, saklanma yerlerinin ve etkileşimli nesnelerin yakınına konmuyor; zemindeki eşyalar hiçbir yolu kapatamıyor.
- **Ulaşılabilirlik kontrolü:** *Rusted Fair ▸ Check Fair Reachability (8 seeds)* menüsü fuarı 8 farklı rastgele tohumla kurup her görev noktasının, çapanın ve odanın yeniden doğma noktasından A* ile ulaşılabildiğini kontrol ediyor. Editörde ve geliştirme sürümlerinde aynı kontrol her oyun başında Console'a yazılıyor.
- **İpuçları:** Oyuncu bir süre ilerleme kaydetmezse (zorluğa göre 22 / 32 / 45 sn, ilk 40 sn hariç) sağ altta bir sonraki görev eşyasının ya da yapılacak işin hangi odada olduğunu söyleyen bir ipucu çıkıyor (8 sn ekranda kalıyor). İpucu verilen eşya 45 sn parlıyor ve ekranın üstünde ona doğru dönen altın renkli bir ok ve mesafe görünüyor. Görevler ekranında **Bana ipucu ver** butonu var (12 sn bekleme). Görev eşyaları artık altın renkli, daha belirgin bir parıltıyla duruyor. İpuçları ayarlardaki "İpuçları" seçeneği kapatılırsa otomatik olarak çıkmıyor.
- **Dokunmatik kontroller:** Her parmak ayrı takip ediliyor. Bir butona basılı tutarken başka bir parmakla kamera çevrilebiliyor; aksiyon butonuna basan parmak kaydırılarak da etrafa bakılabiliyor (duraklat ve Görevler butonları hariç).
- **Diller:** Oda adları ve ipucu metinleri 11 dilin hepsine eklendi (her dosyada 309 anahtar var).
- **Sürüm:** Mağazaya yüklemeden önce *Version* değerini `1.2.0` yapın ve Bundle Version Code'u artırın.

## 11. v1.4 değişiklikleri

- **İpuçları azaldı:** Otomatik ipucu ancak 40 / 55 / 80 sn (zorluğa göre) ilerleme olmazsa çıkıyor, ilk 60 sn hiç çıkmıyor; aynı ipucu tekrarlanınca bekleme ×1.3 uzuyor (en fazla ×2,5). Üstteki ok ipucundan sonra sadece 10 sn görünüyor; eşyanın parlaması 45 sn sürüyor.
- **Canlar:** Her zorlukta 3 can. Ekrandaki kalp/can çubuğu kaldırıldı; kalan can sadece ölüm ekranında görünüyor ("x / 3").
- **Görev eşyaları:** Sigortalar, jetonlar ve dişliler uzaktan görünen altın renkli dikey bir ışık huzmesi ve büyük bir parıltıyla duruyor; diğer görev eşyalarında daha küçük bir huzme var. Eşyaların çevresindeki küçük dekor parçaları (kutular, kafalar, döküntü) oyun başında kaldırılıyor. Raf üstündeki noktalar rafın ön kenarına alındı ve raf çarpıştırıcıları inceltildi (eşya artık rafın içinde kalıp alınamaz hale gelmiyor). Bir çarpıştırıcının içinde başlayan eşya yukarı kaldırılıyor. Editörde ve geliştirme sürümünde her görev eşyasının en az 6 noktadan görünür olduğu kontrol edilip Console'a yazılıyor.
- **Robotlar takılmıyor:** Her 0,9 sn'de bir kontrol ediliyor; robot hareket etmek isteyip ilerleyemiyorsa 2,2 sn boyunca gövde yoklamalarını bırakıp yeniden yol arıyor. Üçüncü takılmada, oyuncu onu görmüyorsa bir sonraki yol noktasına (ya da hücresinin ortasına) ışınlanıyor.
- **FPS:** Robot gözleri için tek ortak kırmızı ışık; gerçek nokta ışık sayısı 4 / 6 / 8; statik birleştirme parçaları 72 m; gölge kapalıyken gölgeye göre ayrı paket yok; tavan ampulleri ve acil durum lambaları statik (ortak malzeme elektrikle birlikte sönüp yanıyor); atlıkarınca, saklanma yerleri ve sahnedeki küçük parçalar birleştirildi; kamera uzak düzlemi 62 m. **Dinamik çözünürlük:** 48 FPS'in altında render ölçeği 0,1 (35'in altında 0,18) düşüyor, en az 0,55; 57,5 FPS'in üstünde 0,05 artıyor (kalite ayarının ölçeğine kadar).
- **Işıklandırma:** Sis 0,026 (#05050B), ortam ışığı mavimsi gökyüzü / sıcak zemin. Her lambanın etrafında yumuşak bir hale, tavan lambalarında hafif bir ışık konisi (düşük kalitede yok) ve zeminde parıltı var. Hepsi gerçek ışıklarla aynı titreme / karartma / elektrik durumunu izliyor ve toplam 3 çizim çağrısı.
- **Sürüm:** Mağazaya yüklemeden önce *Version* değerini `1.4.0` yapın ve Bundle Version Code'u artırın.

## 12. v1.5 değişiklikleri

- **Işık efekti kaldırıldı:** Lambaların altındaki ışık konileri ve zemin parıltıları tamamen kaldırıldı. Sadece lambaların etrafındaki hale kaldı, o da daha küçük ve daha soluk.
- **Daha karanlık ortam:** Sis 0,03 (#030307), ortam ışığı v1.4'ün yaklaşık yarısı, tavan lambaları %20 daha zayıf. Gerçek nokta ışık sayısı 3 / 5 / 8 (kaliteye göre).
- **El feneri güçlendi:** Parlaklık ×1,375, menzil 24 → 30 m, ışık konisi biraz daha geniş. Dolu pil artık 20 dakika gidiyor (eskiden 9).
- **"Saklanacak yer bul!" uyarısı:** Bir robot oyuncuyu fark edip kovalamaya başlayınca ekranın üst kısmında büyük, kırmızı, parlayan bir yazı 2,6 sn görünüyor (6 sn bekleme, saklanırken çıkmıyor). 11 dile çevrildi.
- **El feneri hatırlatması:** Oyuncu 4 sn boyunca karanlıkta ve el feneri kapalıysa sağ altta "el fenerini yak" ipucu çıkıyor (dokunmatikte sağ üstteki 🔦 butonu, klavyede F). En fazla 5 kez, en az 70 sn arayla. Not: 🔦 emojisi bazı cihaz fontlarında kare görünebilir; öyle olursa metinden çıkarılabilir.
- **FPS (telefonlar):**
  - Orta kalitede render çözünürlüğü cihazın "dp" çözünürlüğüne (1,0×), düşük kalitede 0,7×'e sınırlandı; yüksek kalitede en fazla 1,75×.
  - Dinamik çözünürlük daha hızlı tepki veriyor (0,8 sn ölçüm, 50 FPS altında düşüyor, en az 0,5).
  - Düşük ve orta kalitede tüm malzemeler URP **Simple Lit** (Blinn-Phong) ile ve normal haritasız çiziliyor; dokusuz opak malzemeler her parçada tek bir köşe-renkli malzemede birleşiyor (ışıklı / ışıksız ayrı).
  - Saat otomatı, falcı ve ana kapının parçaları da birleştirildi (hareket eden ve koddan erişilen parçalar ayrı kalıyor).
  - Dışarıdaki yağmur sadece oyuncu ana kapıya yakınken hesaplanıyor.
- **Önemli:** Bu sürümden sonra Unity'de bir kez **Rusted Fair ▸ Rebuild Materials** çalıştırın. Yeni `vc_lit`, `vc_unlit` ve `simple_lit_ref` malzemeleri böylece build'e giriyor; yoksa telefonda Simple Lit ve köşe-renk shader'ları bulunamayabilir.
- **Sürüm:** Mağazaya yüklemeden önce *Version* değerini `1.5.0` yapın ve Bundle Version Code'u artırın.

## 13. v1.6 değişiklikleri

- **Yeni başlık fontu:** Creepster tamamen kaldırıldı; yerine **Cinzel** (Google Fonts, SIL OFL) geldi. Büyük başlıklar (Yakalandın / Oyun bitti / Kaçtın: Cinzel Black), panel başlıkları ve ana butonlar (Oyna / Devam / Tamam: Cinzel Bold, geniş harf aralığı, biraz daha küçük) bu fontla. Ana menüdeki damlayan logo resmi kaldırıldı; yerine Cinzel Black ile yazılmış, üstten aydınlanan kırmızı degrade ve geniş harf aralıklı "RUSTED FAIR" yazısı var. Cinzel'de Kiril harfleri olmadığı için Rusçada başlıklar Oswald SemiBold. Harf aralığı ve degrade için `Scripts/UI/TextFx.cs` (LetterSpacing, VerticalGradient) eklendi. Unity'nin eski Text bileşeni değişken fontların kalınlığını seçemediği için Cinzel'in Bold ve Black ağırlıkları ayrı TTF dosyası olarak üretildi.
- **"Saklanacak yer bul!" uyarısı:** Artık Oswald SemiBold, büyük harf, çok daha küçük, geniş harf aralıklı (#FF3B2A, hafif parlama), ekranın %24 yüksekliğinde.
- **Robotlar daha az fark ediyor:** Görüş mesafesi Grin 22 → 15, Rattle 13 → 9, Brutus 18 → 12 m; görüş açısı 115 → 100, 150 → 130, 95 → 85°; duyma 1 → 0,7, 1,7 → 1,15, 0,8 → 0,6; gözden kaybetme süresi 4,5 → 3, 3 → 2,2, 8 → 5 sn. Fark etme hızı düştü, unutma hızı arttı (0,35 → 0,6/sn); oyuncuyu kaybedince arama 10 → 7 sn. Robotların oyuncunun odasına doğru devriye gezme olasılığı en fazla %40 (eskiden %60). Oyuncunun ayak sesi yarıçapı yürürken 6 → 4,5, koşarken 15 → 11, çömelirken 1,8 → 1,5 m.
- **Koşu daha uzun:** Dolu dayanıklılık 10 sn koşturuyor (eskiden 6,5); 5 sn'de doluyor (dururken ×1,5).
- **Toplanabilir eşyalar:** Pil, yağ, su balonu ve oyuncak artık beyaz-mavi bir parıltı ve küçük mavi bir ışık huzmesiyle görünüyor. Başlangıçta 8 pil, 6 yağ, 6 su, 5 oyuncak var. Duvar dibindeki dekorların %45'i önlerine yeni bir eşya noktası bırakıyor. Her 70 sn'de bir haritada 12'den az eşya kaldıysa oyuncudan 18 m'den uzak boş noktalara en fazla 3 yeni eşya çıkıyor (pil iki kat sık).
- **Sürüm:** Mağazaya yüklemeden önce *Version* değerini `1.6.0` yapın ve Bundle Version Code'u artırın.

## 14. v1.7 değişiklikleri

- **Fark edilme biraz arttı, çömelmek işe yarıyor:** Görüş Grin 15 → 18, Rattle 9 → 11, Brutus 12 → 15 m; görüş açısı 100 → 105, 130 → 140, 85 → 90°; duyma 0,7 → 0,85, 1,15 → 1,4, 0,6 → 0,7; gözden kaybetme 3 → 3,6, 2,2 → 2,6, 5 → 6 sn; fark etme hızı biraz arttı. Çömelirken görünürlük 0,6 → 0,45 (görünürlük, robotun seni görebildiği mesafeyi de kısaltıyor).
- **13 numaralı dolap:** Unity'de dolap kapısı kendi `Door` bileşeniyle her karede güncelleniyor (web'deki "kapı dönmüyor" hatası burada yok). Akış kontrol edildi: dolap anahtarıyla etkileşim kilidi açıp kapıyı oyuncuya doğru (dışarı) açıyor, içindeki kurbalı kol alınabiliyor.
- **Duvarlarda titreşen dikdörtgenler (z-fighting) giderildi:** Her odanın duvar yüzeyi (kapı ve açıklıkların üstündeki duvar parçası, süpürgelik ve korkuluk dahil) kendi odasının 2 cm içine alındı. Böylece komşu odada duvara dayalı eşyalar (ör. mutfak tezgâhları ve davlumbaz – Atari Salonu duvarı) artık diğer odanın duvarından görünmüyor. Duvar kan izleri ve aplikler de yeni yüzeye göre yerleştiriliyor.
- **Giriş holündeki çubuk kaldırıldı:** Bilgi masasının önünde havada duran ince camgöbeği neon çubuk silindi.
- **Sahneye çıkılabiliyor:** Oyuncunun basamak yüksekliği 0,35 → 0,5 m (robotlar 0,25 m). Sahneye öndeki 0,44 m'lik basamaktan çıkılıyor ve sahnedeki eşyalar alınabiliyor. Kenardan inince yerçekimiyle (18 m/sn²) düşülüyor. Sahne arkasındaki perdenin çarpıştırıcısı kalınlaştırıldı, arkadan düşülemiyor.
- **v1.7.1 – açık kapının arkasındaki eşya alınabiliyor:** Etkileşim ışını önce AÇIK bir kapıya çarparsa ve kapının en fazla 0,9 m arkasında başka bir etkileşimli nesne varsa (ör. 13 numaralı dolaptaki kurbalı kol) o nesne seçiliyor; yoksa kapı seçiliyor ("Kapat"). Kapı çarpıştırıcısı zaten menteşeyle birlikte dönüyor.
- **Sürüm:** Mağazaya yüklemeden önce *Version* değerini `1.7.1` yapın ve Bundle Version Code'u artırın.

## 15. v1.8 değişiklikleri

- **Çanta (envanter) ekranı:** Unity'de daha önce hiç envanter ekranı yoktu (açılmayan bir panel değil, panelin kendisi eksikti). Sol üstte GÖREVLER düğmesinin yanına çanta simgeli **ÇANTA** düğmesi eklendi (klavyede **I**, duraklatma menüsünde de "Çanta"). Ekran oyunu duraklatır. "Görev eşyaları" bölümünde elindeki görev eşyaları altın renkte ve adetleriyle görünür; maske de "Hazır" ya da "Şarj oluyor: N sn" durumuyla burada. "Kullanılabilir" bölümünde yağ kutusu, su balonu ve kurmalı oyuncak ×adet ile listelenir: birine dokunmak onu fırlatma düğmesinin eşyası yapar, seçili olan mavi çerçeveyle vurgulanır. Pil satırı fener şarjını (%) gösterir; yedek pilin varsa ve şarj %98'in altındaysa dokununca yeni pil takılır. Kapatınca oyuna (duraklatma menüsünden açıldıysa menüye) dönülür. Yeni simge: `Resources/RF/UI/icon_bag.png` (`Tools/gen_textures.py icon_bag`).
- **Not otomatik alınıyor:** Görev bitince yere not düşmüyor. 1,4 sn sonra not kendiliğinden alınır: önce not, ardından "sıradaki görev" ekranı açılır. Yakalanırken, ölüyken, saklanma yerine girerken, oyun duraklatılmışken veya bittikten sonra beklenir; bir robot seni kovalıyorsa da beklenir (not 15 sn'den uzun süredir bekliyorsa artık beklenmez). Maske, 2. görev bitince yine görevin yanında çıkar.
- **Maskeyi aldıktan sonra robotlar biraz daha sinirli (hafif):** Hareket hızı ×1,07, görüş mesafesi ×1,12, fark etme hızı ×1,12. Oyuncunun odasına doğru devriye eğilimi +0,08 (en fazla 0,48).
- **Metinler:** Yeni `inv_*` anahtarları ve değişen `task_done_body`, `brief_step2` 11 dilde.
- **v1.8.1 – üst sıra ve nefes:** Duraklat / GÖREVLER / ÇANTA / 0/7 artık ekranın en üstünde ortalanmış, daha küçük ve %85 opak tek bir sırada (güvenli alan içinde); görev satırı ve ipucu oku hemen altına taşındı. Koşudan sonraki ağır nefes yalnızca gerçekten tükenmişken çalıyor, daha sessiz (0,10) ve yavaş (pitch 0,9), her nefesten sonra 2,2 sn ara veriliyor.
- **Sürüm:** Mağazaya yüklemeden önce *Version* değerini `1.8.1` yapın ve Bundle Version Code'u artırın.

## 16. v1.9 değişiklikleri

- **Başarım bildirimleri:** Her başarım (görevler, yakalanma, 7 not, kaçış…) ekranın üst ortasında sırayla ve kayarak beliren bir şeritle gösteriliyor (her biri ~4,8 sn): kupa simgesi, küçük altın "ROZET KAZANILDI", Cinzel başlık ve açıklama. Kaçınca "Kaçış" (+ hiç yakalanmadıysan "Dokunulmaz", 25 dk altıysa hız ödülü, hikâyede Kâbus zorlukta "zor kaçış", Özel Gece'de "Mesai Sonrası", üç robot da 20 iken "Yirmi Yirmi Yirmi") açılıyor. İlk kaçışta ayrıca kırmızı vurgulu "YENİ MOD – Özel Gece açıldı" bildirimi (kafatası simgesi) çıkıyor. Açılan başarımlar kayıtta (PlayerPrefs) saklanıyor; iki yeni rozet başarımlar ekranında da var.
- **Ana menü:** Zorluk · Dil satırının altına **Özel Gece** düğmesi eklendi. İlk kaçıştan önce kilit simgeli ve soluk görünür, dokununca "Özel Gece'yi açmak için fuardan bir kez kaç." mesajı çıkar. Ayarlar · Başarımlar · Nasıl oynanır artık tek küçük satırda; Reklamları kaldır · Çıkış altta kalıyor.
- **Özel Gece paneli:** Mister Grin / Rattle / Brutus için birer kart (kısa açıklama, büyük seviye numarası ya da KAPALI, 0–20 kaydırıcı); "Uyanma:" (tek tek – hikâye / ilk görevden sonra hepsi / baştan hepsi) ve "Şans:" (1 / 3 / 5) düğmeleri; hazır ayarlar Kolay 5, Normal 10, Zor 15 (ilk görevden sonra hepsi), 20/20/20 (baştan hepsi); "Geceyi başlat". Ayarlar kaydediliyor.
- **Özel Gece oyunu:** Hikâye zorluğu yok sayılır. Robot seviyesi L için: hız ×(0,82 + 0,018·L), görüş ve duyma ×(0,6 + 0,04·L), gözden kaybetme süresi ×(0,7 + 0,03·L), fark etme hızı ×(0,6 + 0,04·L). L = 0 olan robot hiç uyanmaz (kapı alarmında da) ve onun ipuçları gösterilmez. "Baştan hepsi"nde Grin girişte, Rattle 6 sn, Brutus 8 sn sonra uyanır; "İlk görevden sonra hepsi"nde ikisi birden ilk görevden sonra uyanır. Can sayısı seçilen şans kadar. Bitiş ekranında ilk satır "Özel Gece: Grin L · Rattle L · Brutus L". Bitiş ekranındaki "Tekrar oyna" modu korur; ana menüdeki Oyna her zaman hikâyeyi başlatır.
- **Logo:** Ana menü başlığı sabit "RUSTED FAIR" metni; Türkçede de büyük harfe çevrilmediği için "FAİR" olmuyor.
- **Metinler:** Yeni `ach_custom`, `ach_nightmare`, `mode_*`, `custom_*`, `cn_*`, `stats_custom`, `tip_all_awake` anahtarları 11 dilde.
- **Sürüm:** Mağazaya yüklemeden önce *Version* değerini `1.9.0` yapın ve Bundle Version Code'u artırın.

## 17. v1.10 değişiklikleri

- **14 dil:** Lehçe (Polski), Endonezce (Bahasa Indonesia) ve Vietnamca (Tiếng Việt) eklendi (`Resources/RF/Localization/pl.txt`, `id.txt`, `vi.txt`; her dosyada 349 anahtar). Sıra: English, Türkçe, Deutsch, Français, Español, Italiano, Português, Русский, Polski, Bahasa Indonesia, Tiếng Việt, 日本語, 한국어, 简体中文. İlk açılışta cihaz dili Lehçe / Endonezce / Vietnamca ise o dil seçilir, aksi hâlde İngilizce.
- **Fontlar:** Oswald, Special Elite ve Cinzel Lehçe ve Endonezce harflerin hepsini içeriyor. Vietnamca harfleri (ơ ư ạ ả ấ …) yalnızca Oswald içeriyor; bu yüzden Vietnamcada başlıklar Oswald SemiBold, notlar Oswald Regular ile gösteriliyor (Rusçadaki gibi). Logo her dilde Cinzel Black "RUSTED FAIR".
- **Dil seçici:** Ana menüdeki dil düğmesi ve Ayarlar'daki Dil satırı artık tek tek dönmek yerine 14 dilin ızgarasını açıyor; her dil kendi adıyla (kendi yazısını destekleyen fontla) yazılı, seçili olanın altı kırmızı çizgili. Birine dokununca dil değişip ızgara kapanıyor.
- **Küçük harf:** İpuçlarındaki eşya adları Lehçe / Endonezce / Vietnamcada normal küçük harfe çevriliyor (Vietnamca aksanlar korunuyor).
- **Sürüm:** Mağazaya yüklemeden önce *Version* değerini `1.10.0` yapın ve Bundle Version Code'u artırın.

## 18. Mağaza sürümü (Google Play / App Store)

Sürüm **1.0.0** (Android sürüm kodu 1, iOS build 1), geliştirici **LYFE STUDIOS**, paket kimliği **com.lyfestudios.rustedfair** (Android ve iOS).
Kodda yapılanlar aşağıda; elle yapmanız gerekenler numaralı adımlarda.

### 18.1 Koddaki hazırlık (hazır)

- **Tek ayar dosyası:** `Assets/RustedFair/Scripts/Services/StoreConfig.cs`. Şirket/ürün adı, paket kimliği, sürüm numaraları, AdMob App ID'leri ve reklam birimi ID'leri, satın alma ürün kimlikleri, gizlilik politikası ve kullanım şartları adresleri burada. `REPLACE_WITH_...` ile başlayan her değeri değiştirmeniz gerekiyor.
- **Rusted Fair → Apply Store Settings:** Kimliği (LYFE STUDIOS, Rusted Fair, com.lyfestudios.rustedfair, 1.0.0 / 1 / 1), Android ayarlarını (IL2CPP, yalnız ARM64, min API 24, hedef API 36, App Bundle, yalnız yatay sol/sağ, internet izni), iOS ayarlarını (min iOS 15.0, iPhone + iPad, tam ekran, durum çubuğu gizli), yayın kalitesini (Development Build kapalı, motor kodu ayıklama, Managed Stripping = Medium + `link.xml`, release'te sadece uyarı/hata logları), koyu "LYFE STUDIOS" açılış ekranını ve simgeleri uygular. **Setup Project** de aynı ayarları uygular.
- **Reklamlar:** Editörde ve Development Build'de her zaman Google'ın test reklamları kullanılır. Release build'de `StoreConfig` içindeki gerçek ID'ler kullanılır. Bir ID hâlâ `REPLACE_WITH_...` ise reklamlar hiç başlamaz (konsola hata yazılır); üretimde asla test reklamı çıkmaz. AdMob App ID'leri derleme sırasında Google Mobile Ads ayarlarına (Android manifest `com.google.android.gms.ads.APPLICATION_ID`) ve iOS `GADApplicationIdentifier` alanına otomatik yazılır. Elle yazdırmak için: **Rusted Fair → Apply AdMob App IDs**.
- **Sıra:** UMP onay formu → (iOS) App Tracking Transparency izni → AdMob başlatma → ilk reklam isteği. İzin verilmezse yalnızca kişiselleştirilmemiş reklam istenir. Gerektiğinde (ör. AB) Ayarlar'da **Gizlilik seçenekleri** düğmesi çıkar.
- **Satın almalar:** Ürün kimlikleri `com.lyfestudios.rustedfair.noads.monthly` (1 aylık, otomatik yenilenen abonelik) ve `com.lyfestudios.rustedfair.noads.lifetime` (tüketilemeyen). Mağaza panelinde fiyat, dönem ("aylık · otomatik yenilenir · istediğin zaman hesap ayarlarından iptal edebilirsin"), Satın Alımları Geri Yükle, Gizlilik Politikası ve Kullanım Şartları var. Ayarlar'da da Geri Yükle, Gizlilik Politikası, Kullanım Şartları (Apple'ın standart EULA'sı) ve gerekirse Gizlilik seçenekleri var. Gizlilik sayfası oyunun dilinde açılır (`…#tr`, `…#de` vb.).
- **Android manifest:** INTERNET ve `com.google.android.gms.permission.AD_ID` eklenir; oyunun kullanmadığı tehlikeli izinler (konum, mikrofon, kamera, telefon durumu, depolama) bir kütüphane isterse bile kaldırılır. Titreşim (VIBRATE, normal izin) kalıyor.
- **iOS:** Info.plist'e ATT açıklaması (14 dilde, `InfoPlist.strings`), `GADApplicationIdentifier`, Google'ın SKAdNetwork listesi, `ITSAppUsesNonExemptEncryption = false`, 14 dillik `CFBundleLocalizations`, tam ekran ve gizli durum çubuğu eklenir; AppTrackingTransparency çerçevesi bağlanır. Uygulamanın gizlilik bildirimi: `Assets/Plugins/iOS/PrivacyInfo.xcprivacy`.
- **Desteklenmeyen simgeler:** 🔦, ↻ ve 🔈 hiçbir fontta olmadığı için Unity metinleri yüklerken bunları siler.
- **Kare hızı:** Varsayılan 60 FPS. 120 seçeneği yalnızca 90 Hz ve üzeri ekranlarda çıkar.
- **Geliştirici araçları:** Erişilebilirlik ve görünürlük kontrolleri ile kasa şifresi logu yalnızca editörde ve Development Build'de çalışır; oyunda hile/test menüsü yok.

### 18.2 Simgeler ve mağaza görselleri

- Simgeler `Assets/RustedFair/Resources/RF/UI/` içinde: `app_icon_1024.png` (ana simge, opak), `app_icon_bg_1024.png` (Android uyarlanabilir simge arka planı) ve `app_icon_fg_1024.png` (ön plan, saydam). **Apply Store Settings** bunları tüm platformlara atar.
- Mağaza dosyaları proje kökündeki `StoreAssets/` klasöründe: `Icon/` (simgeler, Google Play için 512×512 dahil), `Google/` (öne çıkan görsel 1024×500), `Screenshots/<genişlik>x<yükseklik>/` (ekran görüntüleri).
- **Ekran görüntüleri:** Play modunda bir gece başlatın (görev 1'i yapıp elektriği açarsanız salon görüntüleri daha aydınlık olur), ardından **Rusted Fair → Capture Store Screenshots**. 6 açı (atlıkarınca salonu, oyun salonu, el fenerli hayalet treni tüneli, kırmızı gözlü Mister Grin, sigorta kutusu görevi, ana menü) 1920×1080 (Google Play), 2868×1320 (iPhone 6.9") ve 2752×2064 (iPad 13") boyutlarında `StoreAssets/Screenshots/` altına kaydedilir. HUD görünür, ipuçları gizlidir. Araç son çekim için ana menüye döner.

### 18.3 Ortak hazırlık (elle)

1. Unity Hub'da **Unity 6 LTS'nin en güncel yamasını** kurun: Android Build Support (OpenJDK, Android SDK & NDK) ve iOS Build Support modülleriyle. **Android SDK Platform 36** yüklü olmalı. Unity'nin SDK'sında yoksa: Android Studio → SDK Manager → Android 16 (API 36).
2. **Google Mobile Ads Unity Plugin**'i (bölüm 2) ve **Unity In-App Purchasing** paketini (Package Manager) kurun, ardından **Rusted Fair → Refresh SDK Defines**.
3. AdMob'da (admob.google.com) Android ve iOS için birer uygulama ve birer **Geçiş reklamı (Interstitial)** birimi oluşturun. App ID'leri ve birim ID'lerini `StoreConfig.cs` içindeki `REPLACE_WITH_...` alanlarına yazın.
4. AdMob → Gizlilik ve mesajlaşma: **GDPR (Avrupa) onay mesajını** ve iOS için **IDFA açıklama mesajını** oluşturup yayınlayın; UMP formu bunları gösterir.
5. **Rusted Fair → Apply Store Settings** komutunu çalıştırın.
6. Her güncellemede `StoreConfig.cs` içinde `Version` (ör. 1.0.1), `AndroidVersionCode` (+1) ve `IosBuildNumber` (+1) değerlerini artırıp **Apply Store Settings**'i tekrar çalıştırın.

### 18.4 Google Play (elle)

1. **Yükleme anahtarı:** **Rusted Fair → Android Keystore Help** komutundaki adımlarla bir yükleme keystore'u oluşturun. Dosyayı proje klasörünün **dışına** kaydedin, şifreleriyle birlikte yedekleyin ve asla depoya koymayın.
2. File → Build Profiles → Android → Switch Platform. **Development Build kapalı** olmalı, **Build App Bundle (Google Play)** açık olmalı (ayar bunu yapar). Player → Publishing Settings → **Debug Symbols: Symbol Table** seçin (Play Console'un çökme raporları için). **Build** ile `.aab` alın.
3. Play Console → **Uygulama oluştur**: ad "Rusted Fair", varsayılan dil, Oyun, Ücretsiz. **Play App Signing**'de Google'ın oluşturduğu anahtarı kullanın. İlk yüklediğiniz `.aab` yükleme anahtarınızı kaydeder.
4. **Para kazanma → Ürünler:** `com.lyfestudios.rustedfair.noads.monthly` aboneliğini (1 aylık, otomatik yenilenen bir temel plan) ve `com.lyfestudios.rustedfair.noads.lifetime` uygulama içi ürününü oluşturup etkinleştirin. Ödeme profili gerekiyor.
5. **Uygulama içeriği:**
   - Gizlilik politikası: `https://claude.ai/artifact/UD9mgE2TU7Y56yuRbjyMiq`
   - Reklamlar: **Evet, reklam içeriyor**
   - Reklam kimliği: **Evet, reklam için kullanılıyor**
   - Hedef kitle: 13 yaş altını hedeflemeyin (korku oyunu; ör. 16+ / 18+)
   - İçerik derecelendirmesi: IARC anketi (korku / şiddet)
   - **Veri güvenliği:** AdMob'un topladıklarını beyan edin: cihaz veya diğer kimlikler, uygulama etkileşimleri ve performans/tanılama, yaklaşık konum. Bunlar reklam ve analiz amacıyla üçüncü tarafla paylaşılıyor. Google'ın güncel "AdMob veri açıklaması" sayfasıyla karşılaştırın. Satın almaları Google Play işliyor; oyun bu verileri kendisi toplamıyor.
6. **Mağaza girişi:** kısa ve uzun açıklama, simge (`StoreAssets/Icon/app_icon_512.png`), öne çıkan görsel (`StoreAssets/Google/feature_graphic_1024x500.png`), en az 2 telefon ekran görüntüsü (`StoreAssets/Screenshots/1920x1080/`).
7. Önce **Dahili test** kanalına yükleyip satın almaları lisans test hesabıyla deneyin. **Kişisel** bir geliştirici hesabıysa üretimden önce en az 12 test kullanıcısıyla 14 gün **kapalı test** zorunludur.
8. **Son tarih:** Yeni uygulamalar ve güncellemeler 31 Ağustos 2026'dan beri **Android 16 (API 36)** hedeflemek zorunda (Play Console'dan 1 Kasım 2026'ya kadar uzatma istenebilir). AAB ve 64 bit zorunlu; bu proje ikisini de sağlıyor.

### 18.5 App Store (elle)

1. Bir **Mac**'te **Xcode 26** (iOS 26 SDK) kullanın; 28 Nisan 2026'dan beri yüklemeler bununla derlenmek zorunda. CocoaPods kurulu olsun (`sudo gem install cocoapods`).
2. developer.apple.com → Identifiers: `com.lyfestudios.rustedfair` kimliğini **In-App Purchase** yeteneğiyle kaydedin. App Store Connect → **Agreements, Tax and Banking** bölümünde ücretli uygulama sözleşmesini imzalayın, banka ve vergi bilgilerini girin.
3. App Store Connect → **Yeni uygulama**: ad "Rusted Fair", birincil dil, paket kimliği, SKU (ör. RUSTEDFAIR1).
4. **Uygulama içi satın almalar:**
   - "Remove Ads" adında bir abonelik grubu, içinde `com.lyfestudios.rustedfair.noads.monthly` (1 ay)
   - `com.lyfestudios.rustedfair.noads.lifetime` (Non-Consumable)
   - Her ürün için ad, açıklama, fiyat ve inceleme ekran görüntüsü (mağaza paneli)
5. **Uygulama gizliliği** (gizlilik etiketi):
   - Sizi izlemek için kullanılan veriler: **Cihaz Kimliği** ve **Reklam Verileri** (üçüncü taraf reklam, AdMob). Bunlar kullanıcıyla ilişkilendirilmiyor.
   - Gizlilik politikası adresi: yukarıdaki adres.
   - Satın almaları StoreKit işliyor; oyun bunları kendisi toplamıyor.
6. **Yaş derecelendirmesi:** Yeni anketi doldurun (korku teması, gerçekçi olmayan şiddet, reklamlar).
7. Unity: File → Build Profiles → iOS → Switch Platform → **Build**. Oluşan klasörde `Unity-iPhone.xcworkspace` dosyasını açın (CocoaPods kurulumu otomatik olur; olmazsa klasörde `pod install`). Signing & Capabilities → Team (LYFE STUDIOS) ve otomatik imzalama, **In-App Purchase** yeteneği.
8. Product → Archive → Distribute App → App Store Connect → Upload. Şifreleme sorusu çıkmaz (`ITSAppUsesNonExemptEncryption = false`). Önce **TestFlight**'ta Sandbox hesabıyla satın almayı, geri yüklemeyi ve ATT iznini deneyin.
9. Mağaza sayfası: açıklama, anahtar kelimeler, destek URL'si, gizlilik URL'si, ekran görüntüleri (`StoreAssets/Screenshots/2868x1320/` iPhone 6.9", `2752x2064/` iPad 13"). İnceleme notu: "Hesap gerekmez. Satın Alımları Geri Yükle: Ayarlar ve Reklamları Kaldır paneli."
10. Minimum iOS 15.0. Apple'ın 9 Eylül 2026'dan beri istediği iOS 13+ şartını karşılıyor.

### 18.6 Yüklemeden önce kontrol listesi

- `StoreConfig.cs` içinde `REPLACE_WITH_...` kalmadı mı? (Release build konsolda uyarır.)
- Development Build kapalı mı, sürüm numaraları artırıldı mı?
- Gerçek cihazda: reklam ölüm ekranından sonra çıkıyor mu (release'te gerçek reklam; kendi reklamlarınıza tıklamayın, AdMob'da test cihazı tanımlayın), satın alma/geri yükleme ve gizlilik bağlantıları çalışıyor mu, 14 dil ve yatay yön doğru mu?
