# LinkDesk: xem và điều khiển điện thoại từ máy tính, không cần gỡ lỗi USB

LinkDesk gồm 2 phần, giống Phone Link:

- **App Android `LinkDesk.apk`** cài trên điện thoại. App gửi hình màn hình qua WiFi và nhận lệnh chạm, vuốt, gõ chữ từ máy tính.
- **App Windows `LinkDesk.exe`** cài trên máy tính. App tự tìm điện thoại trong WiFi, hiện màn hình điện thoại, báo tin nhắn, chạy ở khay hệ thống và khởi động cùng Windows. Chạy trên Windows 10/11, không cần cài thêm gì.

App **không dùng adb**, nên **không cần bật Tùy chọn nhà phát triển hay Gỡ lỗi USB**. Thay vào đó app dùng 3 quyền bình thường của Android:

| Quyền | Để làm gì | Bắt buộc? |
|---|---|---|
| Chia sẻ màn hình | Gửi hình sang máy tính | Có. Android hỏi mỗi lần bấm "Bắt đầu". |
| Trợ năng (Accessibility) | Chạm, vuốt, gõ chữ, Back/Home/Đa nhiệm | Không bắt buộc. Thiếu quyền này thì chỉ xem được. |
| Truy cập thông báo | Hiện thông báo trên máy tính, trả lời tin nhắn nhanh | Không bắt buộc. |

> **Lưu ý:** bản này mới được build và kiểm tra trên máy chủ (app Windows và giao diện web đã chạy thử với điện thoại giả lập). **Chưa được chạy trên điện thoại thật và Windows thật.** Nếu có lỗi, bạn gửi ảnh chụp hoặc mô tả để sửa.

> **Mới ở bản 0.8.0 (máy tính) + 0.6.0 (điện thoại):** ô **nhạc đang phát** ở cột trái (ảnh bìa, phát/dừng, chuyển bài; cần quyền Thông báo). Tab **Tệp** có thêm "Tệp trong điện thoại" để duyệt và tải tệp về (lần đầu bật "Cho phép truy cập tệp" trong app điện thoại, mục Quyền). Trên điện thoại: bấm **Chia sẻ → "Gửi sang máy tính"** để gửi chữ, link, ảnh hoặc tệp sang máy tính (tệp lưu ở Tải xuống\LinkDesk), và nút "Gửi chữ đã chép sang máy tính" trong app.
>
> **Bản 0.7.0 (máy tính):** bố cục giống Phone Link. Cột trái: ảnh nhỏ màn hình điện thoại, pin, trạng thái kết nối, 4 nút nhanh (làm tối, giữ sáng, khoá máy, chụp màn hình) và danh sách thông báo. Bên phải: tab Màn hình, Ảnh, Tệp, Gõ chữ; nút Cài đặt và Ghim ở góc trên bên phải; thanh nút điều khiển dọc cạnh màn hình điện thoại (nút "…" chứa âm lượng và menu nguồn).
>
> **Bản 0.6.0 (máy tính):** giao diện Sáng/Tối/Theo Windows (Cài đặt → Giao diện), thanh tiêu đề cùng màu với app, công tắc và ô nhập kiểu Windows 11, cửa sổ Kết nối và Cài đặt làm lại.
>
> **Bản 0.5.0:** mục **Ảnh** để xem và tải ảnh trong điện thoại về máy tính (lần đầu cần bấm "Cho phép xem ảnh" trong app điện thoại, mục Quyền), biểu tượng ứng dụng trong thông báo, tự gửi chữ vừa chép (Ctrl+C) trên máy tính sang điện thoại (tắt được trong Cài đặt), nút ghim cửa sổ luôn nằm trên, khung điện thoại mới.
>
> **Bản 0.4.0:** tự kiểm tra cập nhật (mục 9), chỉnh số khung hình/giây, độ nét và chất lượng ảnh (mục 4), chế độ siêu tối (mục 5), giao diện mới trên cả máy tính và điện thoại.

---

## 1. Cài app lên điện thoại (làm 1 lần)

1. Chép file `LinkDesk.apk` sang điện thoại. Có thể gửi qua Zalo/Telegram cho chính mình, qua Google Drive, hoặc cắm cáp USB ở chế độ "Truyền tệp" (chế độ này không cần gỡ lỗi).
2. Mở file APK trên điện thoại rồi bấm **Cài đặt**.
   - Nếu máy hỏi "Cho phép cài ứng dụng không rõ nguồn gốc" thì bấm **Cài đặt** → bật cho app đang dùng để mở file (Files, Zalo…).
   - Xiaomi có thể hiện cảnh báo bảo mật hoặc bắt đợi vài giây. Play Protect có thể báo "ứng dụng không xác định". Chọn **Vẫn cài đặt**. Lý do: app tự build, không có trên CH Play.
3. Mở app **LinkDesk**, cho phép **gửi thông báo** khi được hỏi.

### Bật quyền Trợ năng (để điều khiển)

Trong app bấm **Bật trong cài đặt Trợ năng** → tìm **LinkDesk** (thường ở mục *Ứng dụng đã tải xuống*) → bật → **Cho phép**.

**Trên Xiaomi/HyperOS/Android 13+**, công tắc có thể bị mờ và báo *"Cài đặt bị hạn chế"* (vì app không cài từ CH Play). Cách xử lý:

1. Trong app LinkDesk bấm **Mở Thông tin ứng dụng**.
2. Bấm dấu **⋮** ở góc trên bên phải → **Cho phép cài đặt bị hạn chế** (Allow restricted settings).
3. Quay lại bật Trợ năng như trên.

### Bật quyền thông báo (tuỳ chọn)

Bấm **Mở cài đặt quyền thông báo** → bật **LinkDesk**.

### Để app không bị tắt ngang (Xiaomi)

Thông tin ứng dụng → **Tiết kiệm pin: Không hạn chế**, bật **Tự khởi động**. Nên khoá app trong màn hình đa nhiệm (kéo xuống ở thẻ app).

---

## 2. Cài app trên máy tính (làm 1 lần)

1. Tải `LinkDesk.exe` về máy rồi nhấp đúp.
   - Windows có thể hiện **"Windows đã bảo vệ PC của bạn"** (SmartScreen), vì app tự build nên chưa có chữ ký số. Bấm **Thông tin bổ sung → Vẫn chạy**.
2. App hỏi **"Cài đặt LinkDesk vào máy tính?"** thì bấm **Yes**. App tự cài vào máy (không cần quyền quản trị), tạo biểu tượng **LinkDesk** ở menu Start và Desktop.
3. Lần đầu mở, cửa sổ **Kết nối điện thoại** sẽ hiện ra (xem mục 3).

- **Gỡ cài đặt:** Cài đặt Windows → Ứng dụng → LinkDesk → Gỡ cài đặt. Hoặc trong app vào tab Cài đặt → "Gỡ cài đặt LinkDesk…".
- **Cập nhật bản mới:** từ bản 0.4.0 app tự báo khi có bản mới (xem mục 9). Bạn cũng có thể mở file `LinkDesk.exe` mới rồi chọn Yes.
- Windows Firewall có thể hỏi quyền mạng. Hãy cho phép ở **mạng riêng (Private)** để app tìm được điện thoại.

---

## 3. Mỗi lần dùng

1. Điện thoại và máy tính **dùng chung một WiFi**.
2. Trên điện thoại mở LinkDesk → **Bắt đầu chia sẻ màn hình** → **Bắt đầu / Cho phép**.
   - Trên Android 14+, nếu được hỏi thì chọn **Chia sẻ toàn bộ màn hình**.
3. Trên máy tính mở **LinkDesk**.
   - **Lần đầu:** điện thoại sẽ tự hiện trong danh sách. Chọn nó, nhập **mã PIN 6 số** đang hiện trong app trên điện thoại, rồi bấm **Kết nối**. Nếu không tự tìm thấy, nhập địa chỉ hiện trong app (ví dụ `192.168.1.5`).
   - **Các lần sau:** LinkDesk tự kết nối lại, kể cả khi điện thoại đổi địa chỉ IP.
4. Bấm X thì LinkDesk thu xuống **khay hệ thống** (góc phải thanh tác vụ) và vẫn báo tin nhắn mới. Muốn tắt hẳn: nhấp chuột phải biểu tượng → **Thoát**.

Dừng chia sẻ: bấm **Dừng chia sẻ** trong app điện thoại, hoặc nút **Dừng** trên thông báo.

Không muốn cài app Windows? Vẫn có thể mở trình duyệt Edge/Chrome và vào địa chỉ hiện trong app điện thoại (`http://…:8686`).

---

## 4. Điều khiển

**Chỉnh hình:** bấm biểu tượng bánh răng ở góc trên bên phải → mục **Hình ảnh**: số khung hình/giây (10 đến 60), độ nét (720 đến gốc) và chất lượng ảnh. Thay đổi có hiệu lực ngay. Nếu bị giật, giảm độ nét trước rồi mới giảm khung/giây.

| Trên máy tính | Trên điện thoại |
|---|---|
| Bấm chuột trái | Chạm |
| Giữ chuột trái | Nhấn giữ |
| Kéo chuột | Vuốt |
| Giữ yên ~0,5 giây rồi kéo | Kéo-thả (di chuyển icon…) |
| Lăn chuột | Cuộn |
| Chuột phải hoặc `Esc` | Quay lại |
| Chuột giữa | Màn hình chính |
| Gõ phím (sau khi bấm vào ô nhập trên điện thoại) | Gõ chữ. Tiếng Việt có dấu dùng được. `Backspace`, `Enter`, `←` `→` cũng dùng được. |
| `Ctrl+V` | Dán chữ từ máy tính vào ô đang chọn |
| `Ctrl+C` | Lấy chữ đang bôi đen trên điện thoại |

Thanh nút bên trái: Quay lại, Home, Đa nhiệm, Kéo thông báo, Cài đặt nhanh, Âm lượng, Bật sáng màn hình, Khoá màn hình, Menu nguồn, Chụp màn hình (lưu vào thư mục `Ảnh\LinkDesk` và chép vào clipboard), Toàn màn hình (`F11`).

Bảng bên phải:

- **Thông báo**: xem, mở, xoá, **trả lời nhanh** tin nhắn (Zalo, Messenger, SMS… nếu app đó có nút trả lời trong thông báo).
- **Tệp & chữ**: kéo thả tệp vào cửa sổ để gửi sang điện thoại (lưu ở `Download/LinkDesk`). Có ô gõ chữ dài và nút gửi clipboard máy tính sang điện thoại.
- **Cài đặt**: chất lượng hình (Mượt 720p / Cân bằng / Nét / Tối đa), giữ màn hình điện thoại luôn sáng, khởi động cùng Windows, báo tin nhắn điện thoại trên Windows, kết nối điện thoại khác.

Khi cửa sổ đang ẩn hoặc không được chọn, tin nhắn mới trên điện thoại sẽ hiện thành thông báo của Windows. Bấm vào thông báo để mở LinkDesk và trả lời.

Nếu gõ phím trực tiếp bằng Unikey bị lỗi dấu, hãy dùng ô **Gõ chữ vào điện thoại** ở bảng bên phải.

---

## 5. Khi màn hình điện thoại tắt

Android chỉ chia sẻ được hình khi màn hình đang bật. Vì vậy LinkDesk làm như sau:

- **Khi máy tính đang kết nối, điện thoại không tự tắt màn hình** (mặc định đã bật). Bạn có thể đổi trong app điện thoại, hoặc trên máy tính ở tab Cài đặt → "Giữ màn hình điện thoại sáng khi đang kết nối".
- **Làm tối màn hình** (nút hình mặt trăng trên thanh bên trái, mặc định bật): màn hình điện thoại giảm độ sáng xuống thấp nhất, gần như tắt, còn trên máy tính vẫn thấy hình và điều khiển bình thường. Tính năng này giống "tắt màn hình" của scrcpy và cần bật Trợ năng. Khi máy tính ngắt kết nối, độ sáng trở lại như cũ.
- Nếu bạn **bấm nút nguồn** hoặc màn hình vẫn bị tắt, **kết nối vẫn được giữ**: LinkDesk trên máy tính hiện "Màn hình điện thoại đang tắt", bạn bấm vào đó để bật lại màn hình, và thông báo/tin nhắn vẫn nhận bình thường.
- Một số máy (nhất là Xiaomi và Android 15+) **tự dừng chia sẻ hình khi khoá máy**. Khi đó LinkDesk trên máy tính sẽ báo, và bạn cần mở khoá điện thoại → app LinkDesk → **Chia sẻ lại màn hình**. Nếu đã chạy lệnh adb ở mục "Nâng cao" (`appops … PROJECT_MEDIA allow`), app sẽ **tự chia sẻ lại** ngay khi mở khoá, không cần bấm.

- **Siêu tối (gần như tắt hẳn):** nếu đã chạy lệnh adb số 1 ở mục "Nâng cao" (`WRITE_SECURE_SETTINGS`), khi bật "Làm tối màn hình" app còn bật thêm **Giảm độ sáng thêm (Extra dim)** của Android, nên màn hình gần như đen hẳn. Khi ngắt kết nối, app tự trả lại như cũ.
- **Vì sao không tắt hẳn màn hình được như scrcpy?** scrcpy tắt màn hình bằng quyền của adb (shell). Một app bình thường không có quyền đó, và nếu màn hình tắt thật thì Android ngừng gửi hình. Vì vậy LinkDesk dùng cách làm tối ở trên.

Lưu ý: giữ màn hình sáng (dù rất tối) sẽ tốn pin hơn. Nên cắm sạc nếu dùng lâu.

---

## 6. Dùng app ngân hàng

Nhiều app ngân hàng ở Việt Nam **không chạy khi có app lạ đang bật Trợ năng hoặc đang chia sẻ màn hình**. Đây là biện pháp chống lừa đảo của ngân hàng.

- **Trước khi mở app ngân hàng**: bấm **Chế độ ngân hàng (tắt hết)** trong app LinkDesk, hoặc trong tab Cài đặt trên máy tính. Thao tác này dừng chia sẻ màn hình và tắt Trợ năng của LinkDesk. Tùy chọn nhà phát triển thì không cần đụng tới.
- **Khi muốn dùng lại LinkDesk**: bật lại Trợ năng (mục 1) rồi bấm Bắt đầu chia sẻ.
- Một số app (ngân hàng, Netflix, ví điện tử…) chặn quay màn hình, nên trên máy tính sẽ thấy **màn hình đen** khi mở các app đó. Không vượt qua được, và cũng không nên vượt qua.
- Nếu app ngân hàng **vẫn báo lỗi dù đã tắt hết**, có thể app đó chặn mọi app cài ngoài CH Play có quyền Trợ năng. Khi đó báo lại tên ngân hàng để tìm cách khác.

### Nâng cao: bật/tắt Trợ năng bằng 1 chạm (cần adb đúng 1 lần)

Bình thường Android không cho app tự bật lại Trợ năng, nên mỗi lần phải vào Cài đặt. Nếu bạn chấp nhận bật Gỡ lỗi USB **một lần duy nhất** (ví dụ bằng scrcpy-launcher), chạy:

```
adb shell pm grant vn.linkdesk android.permission.WRITE_SECURE_SETTINGS
adb shell appops set vn.linkdesk PROJECT_MEDIA allow
```

Sau đó **tắt Tùy chọn nhà phát triển như bình thường**, vì quyền đã cấp vẫn giữ nguyên.

- Lệnh 1: bật chế độ **siêu tối** (mục 5), và nút "Bật/Tắt điều khiển" trong app và "Chế độ ngân hàng" sẽ tự bật/tắt Trợ năng, không cần vào Cài đặt.
- Lệnh 2: trên nhiều máy, Android sẽ không hỏi "Cho phép chia sẻ màn hình?" mỗi lần nữa, và LinkDesk tự chia sẻ lại sau khi mở khoá máy. Một số máy hoặc phiên bản Android mới vẫn hỏi.
- Xiaomi cần bật thêm **Gỡ lỗi USB (Cài đặt bảo mật)** lúc chạy lệnh.

---

## 7. Giới hạn hiện tại

- **Chưa có âm thanh** (chỉ có hình). Có thể thêm sau (Android 10+, nhiều app không cho thu tiếng).
- Hình gửi dạng ảnh JPEG nên **độ trễ và độ nét kém scrcpy** một chút. WiFi yếu thì chọn "Mượt 720p".
- Nếu **tắt hẳn màn hình** bằng nút nguồn thì hình sẽ dừng (xem mục "Khi màn hình điện thoại tắt").
- Chỉ dùng được trong **cùng mạng WiFi** (không qua Internet).
- Kết nối trong mạng nội bộ **không mã hoá**, chỉ được bảo vệ bằng PIN. Chỉ nên dùng ở WiFi nhà hoặc WiFi tin cậy, không dùng WiFi quán cà phê. Nhập sai PIN 5 lần sẽ bị khoá 1 phút. Có thể bấm **Đổi mã PIN** bất cứ lúc nào.
- Chưa có: gọi điện, xem toàn bộ SMS, đồng bộ ảnh, kéo tệp từ điện thoại về máy tính.

---

## 8. Dành cho người muốn sửa/build lại

- Mã nguồn app Windows: `windows-app/` (C# WinForms, .NET Framework 4.8). Build bằng `dotnet build -c Release`, chạy được cả trên Linux.
- Mã nguồn app Android: `android/` (Java thuần, không dùng thư viện ngoài). Mở được bằng **Android Studio** (thư mục `android/`). Giao diện web nằm ở `android/app/src/main/assets/index.html`.
- Build không cần Android Studio (Linux): `build/build.sh` (cần `openjdk`, `aapt`, `dalvik-exchange`, `zipalign`, `apksigner`).
- **Giữ lại file `build/linkdesk.keystore`** (mật khẩu `linkdesk`). Các bản cập nhật sau phải ký bằng đúng khoá này thì mới cài đè lên được, không phải gỡ app.

---

## 9. Cập nhật tự động

Từ bản 0.4.0, bạn **không cần tự tải lại app** mỗi khi có bản mới:

- **Máy tính:** app tự kiểm tra lúc mở và mỗi 6 tiếng. Khi có bản mới, Windows hiện thông báo và nút **Cập nhật x.y.z** xuất hiện ở góc trên bên phải. Bấm vào, app tự tải, tự cài và mở lại. Có thể tắt ở Cài đặt → "Tự kiểm tra bản cập nhật", và có nút "Kiểm tra cập nhật" để kiểm tra ngay.
- **Điện thoại:** mở app LinkDesk, nếu có bản mới sẽ thấy thẻ **"Có bản mới …"** ở đầu màn hình. Bấm **Cập nhật**, app tự tải về. Lần đầu Android sẽ hỏi quyền **"Cài đặt ứng dụng không xác định"** cho LinkDesk, bạn cho phép một lần. Sau đó Android vẫn hỏi xác nhận "Cập nhật?" mỗi lần (Android bắt buộc), bạn bấm **Cập nhật**.
- Bản mới được đăng trong thư mục `releases/` của repo GitHub `tdhieu94/LinkDesk` (tải trực tiếp ở đó nếu cần). Sau khi cập nhật điện thoại, nhớ kiểm tra lại Trợ năng vẫn bật (Xiaomi đôi khi tắt quyền này sau khi cập nhật app).
