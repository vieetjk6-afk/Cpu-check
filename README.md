# PC Check

Phần mềm kiểm tra máy tính Windows: xem thông tin phần cứng, theo dõi nhiệt độ – sức khỏe linh kiện, kiểm tra tương thích, phát hiện driver thiếu/lỗi, dọn rác, tìm phần mềm ít dùng và chẩn đoán lỗi hệ thống kèm gợi ý khắc phục 1 click.

- **Nền tảng:** Windows 10 / 11 (x64)
- **Công nghệ:** C# · .NET 10 (LTS) · WPF – ứng dụng native, nhẹ, khởi động nhanh
- **Giao diện:** hiện đại, tối/sáng, **song ngữ Tiếng Việt / English** (đổi ngay khi đang chạy)
- **Tác giả:** [vieetjk](https://github.com/vieetjk6-afk)

## Tính năng

| Trang | Nội dung |
|---|---|
| **Tổng quan** | Điểm sức khỏe 0–100, tóm tắt hệ thống, giám sát CPU/GPU/RAM/ổ đĩa trực tiếp (biểu đồ mini), tình trạng từng hạng mục, các vấn đề quan trọng nhất |
| **Phần cứng** | CPU, mainboard/BIOS, từng thanh RAM (khe, bus, hãng, mã), card màn hình (VRAM, driver), ổ cứng (loại, bus, S.M.A.R.T, phân vùng), màn hình (độ phân giải, tần số quét), mạng, âm thanh, pin, UEFI/Secure Boot/TPM. Mỗi mục **thu gọn / mở rộng** được (nút "Mở rộng tất cả / Thu gọn tất cả"). Có nút sao chép toàn bộ thông tin |
| **Nhiệt độ & Sức khỏe** | **Thẻ riêng cho từng thiết bị** (CPU, GPU, RAM, bo mạch, từng ổ cứng, pin, vùng nhiệt ACPI): tiêu đề hiện nhiệt độ/giá trị chính kèm màu cảnh báo, bấm để mở rộng xem thông tin thiết bị và toàn bộ cảm biến (nhiệt độ, tải, xung nhịp, quạt, điện áp, công suất – hiện tại / thấp nhất / cao nhất), cập nhật mỗi 2 giây. Sức khỏe ổ cứng (S.M.A.R.T, tuổi thọ SSD, giờ chạy, lỗi đọc/ghi); độ chai pin, báo cáo pin |
| **Tương thích** | RAM kênh đôi/đơn, RAM lệch dung lượng/bus, RAM chưa bật XMP/EXPO, khả năng nâng cấp RAM, Windows 32/64-bit, Windows chạy trên HDD hay SSD, màn hình chạy dưới tần số quét/độ phân giải tối đa, ảo hóa VT-x/AMD-V, xung đột nhiều phần mềm diệt virus, BIOS cũ, điều kiện nâng cấp Windows 11 (CPU, TPM 2.0, UEFI, Secure Boot) |
| **Driver** | Thiết bị chưa cài driver, thiết bị báo lỗi (giải thích mã lỗi Device Manager), đang dùng driver chung (Basic Display, HD Audio), driver không chữ ký, driver quá cũ; tìm driver theo Hardware ID, mở Driver tùy chọn của Windows Update, trang driver NVIDIA/AMD/Intel và hãng máy (Dell, HP, Lenovo, ASUS, Acer, MSI, Gigabyte…) |
| **Dọn dẹp** | **Tệp rác:** Temp, Windows Update cache, Delivery Optimization, báo cáo lỗi, log cũ, thumbnail, crash dump, shader cache, cache trình duyệt (Chrome, Edge, **Cốc Cốc**, Brave, Opera, Vivaldi, Firefox), Thùng rác. **Phần mềm:** ước lượng lần dùng cuối (UserAssist + Prefetch), đánh dấu phần mềm không dùng >90 ngày, phần mềm quảng cáo / "tối ưu hóa" giả / thanh công cụ / công cụ crack, app Store cài sẵn. **Gỡ sạch:** đánh dấu nhiều phần mềm (hoặc "Chọn các mục gợi ý") rồi gỡ lần lượt – gỡ im lặng khi phần mềm hỗ trợ (MSI, QuietUninstallString), tùy chọn tạo điểm khôi phục trước; sau đó tự tìm **phần còn sót** (thư mục cài đặt, dữ liệu trong AppData/ProgramData, lối tắt, khóa registry, mục tự khởi động, tác vụ hẹn giờ, dịch vụ mồ côi) để bạn xem và chọn xóa. **Khởi động:** bật/tắt chương trình chạy cùng Windows |
| **Lỗi & Khắc phục** | Màn hình xanh (kèm tên mã lỗi và nguyên nhân thường gặp), tắt máy đột ngột, lỗi phần cứng WHEA, lỗi ổ cứng/NTFS, driver card màn hình bị treo, ứng dụng hay crash, thiếu RAM, Windows hết hạn hỗ trợ, chờ khởi động lại, chưa cập nhật, chưa kích hoạt, diệt virus/tường lửa/UAC tắt… Mỗi vấn đề có gợi ý và nút khắc phục; **bấm vào thẻ lỗi để xem nguyên nhân thường gặp và các bước khắc phục chi tiết** (hơn 50 loại lỗi có hướng dẫn riêng). **Công cụ sửa nhanh:** SFC + DISM, kiểm tra ổ đĩa (chạy nền ngay trong ứng dụng, báo kết quả và đề nghị sửa nếu có lỗi), kiểm tra RAM, tạo điểm khôi phục, xóa DNS, reset mạng, sửa Windows Update, quét phần cứng, sửa Microsoft Store, báo cáo pin… |
| **Bảo mật** | Tìm dấu hiệu **bị theo dõi**: phần mềm gián điệp/keylogger, phần mềm giám sát, phần mềm điều khiển từ xa (TeamViewer, AnyDesk, UltraViewer, RustDesk…) đã cài/đang chạy/đang kết nối; **ứng dụng đã dùng camera, micro, vị trí** trong 30 ngày; chương trình tự chạy lạ (khóa Run, Startup, tác vụ hẹn giờ, dịch vụ – lệnh PowerShell mã hóa, tải từ Internet, tệp chưa ký số trong AppData/Temp); tiến trình giả mạo tên hệ thống; kết nối Internet theo từng chương trình, cổng đang mở; tiện ích trình duyệt (cài bắt buộc, quyền rộng), chính sách và proxy; chứng chỉ gốc lạ (nghe lén HTTPS), tệp hosts bị sửa; Microsoft Defender (mối đe dọa, mục loại trừ, Tamper Protection); Remote Desktop, SMBv1, tài khoản quản trị/ẩn, tự đăng nhập; chiếm quyền IFEO/Winlogon, SmartScreen, Secure Boot, BitLocker. Mỗi mục có hướng dẫn xử lý, nút **mở vị trí tệp**, **tra cứu VirusTotal** (chỉ gửi mã băm), gỡ cài đặt, tắt tự chạy. Nút **quét nhanh / toàn bộ / ngoại tuyến** bằng Microsoft Defender – quét chạy ngay trong ứng dụng, hiện thời gian đã quét, có nút dừng và báo số mối đe dọa khi xong |
| **Widget màn hình** | Bảng nhỏ nổi trên màn hình, **kéo thả tới bất kỳ đâu** (tự hít vào mép màn hình, nhớ vị trí, hỗ trợ nhiều màn hình). Tùy chọn thông số: CPU (% + xung), nhiệt độ CPU (+ công suất), GPU, nhiệt độ GPU (+ hot spot), VRAM, RAM, mạng (tải xuống/lên), ổ đĩa (% bận, đọc/ghi), nhiệt độ ổ, **nhiệt độ từng thiết bị**, quạt, pin, thời gian chạy, đồng hồ. Tùy chỉnh: **thu gọn** (lưới biểu tượng + giá trị, bấm mũi tên trên widget), luôn ở trên cùng, khóa vị trí, độ đậm, kích thước nhỏ/vừa/lớn, bố cục dọc/ngang, biểu đồ, tần suất 1/2/5 giây, **khởi động cùng Windows**. Widget vẫn chạy khi đóng cửa sổ chính; nhấp đúp để mở lại, chuột phải để xem menu |
| **Cập nhật** | Trang **Cập nhật** riêng trên thanh bên (phiên bản đang dùng, tác giả, mã nguồn). Tự kiểm tra bản mới trên GitHub Releases khi mở ứng dụng (tối đa 12 giờ/lần, tắt được), báo ở thanh bên kèm huy hiệu. Bấm **Kiểm tra bản mới** để: xem "Có gì mới", **Cập nhật ngay** (tải đúng bản portable/small, kiểm tra SHA-256, thay tệp .exe và tự khởi động lại; lỗi thì giữ nguyên bản cũ), xem trên GitHub hoặc bỏ qua phiên bản |
| **Báo cáo** | Xuất toàn bộ kết quả ra 1 tệp HTML (gửi cho khách/kỹ thuật viên, in ấn) – `Ctrl+S` |

## Tải về và chạy

1. Vào tab **Actions** của repo → chọn lần build mới nhất → tải artifact:
   - `PcCheck-portable-win-x64`: 1 file `PcCheck.exe` chạy ngay, **không cần cài gì** (~70 MB).
   - `PcCheck-small-win-x64`: bản nhỏ (~7 MB, 1 file), cần cài [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0).

   Khi tạo tag `v*` (ví dụ `v1.0.0`), workflow tự tạo GitHub Release kèm 2 tệp zip và ghi chú thay đổi, đồng thời ghi số phiên bản đó vào ứng dụng.

   **Phát hành bản mới để người dùng tự cập nhật:** tăng số phiên bản và đẩy tag, ví dụ `git tag v1.1.0 && git push origin v1.1.0`. Ứng dụng so sánh phiên bản đang chạy với bản phát hành mới nhất (bỏ qua bản nháp / pre-release) và tải tệp `PcCheck-portable-win-x64.zip` hoặc `PcCheck-small-win-x64.zip` tùy bản đang dùng. Bản tải từ tab Actions (không qua tag) mang số phiên bản mặc định trong `Directory.Build.props`.
2. Chạy `PcCheck.exe` → đồng ý quyền quản trị (UAC). Ứng dụng tự quét lần đầu (5–15 giây).
3. **Nhiệt độ CPU – không cần tự cài phần mềm ngoài:**
   - Ngay khi mở, PC Check đọc **nhiệt độ ước lượng từ vùng nhiệt ACPI** của Windows (không cần driver; ghi chú "ước lượng"). Con số này do firmware báo nên có thể thấp/chậm hơn nhiệt độ thật, vì vậy ứng dụng không báo đỏ chỉ dựa trên nó.
   - Muốn số đo chính xác từ cảm biến bên trong CPU, bấm **"Bật đọc nhiệt độ CPU chính xác"** (trang Nhiệt độ & Sức khỏe hoặc thẻ lỗi). Ứng dụng tự tải driver [PawnIO](https://pawnio.eu/) chính thức từ GitHub (miễn phí, mã nguồn mở – thay thế WinRing0 đã bị Defender chặn), **kiểm tra chữ ký số Authenticode** rồi cài im lặng và đọc cảm biến ngay, không cần mở lại ứng dụng. Windows không cho phần mềm thường đọc cảm biến trong CPU nếu không có driver nhân – HWiNFO, Core Temp, AIDA64 cũng đều kèm driver riêng.
   - Nhiệt độ GPU và ổ cứng đọc được mà không cần PawnIO.

> Phím tắt: `F5` quét lại · `Ctrl+S` xuất báo cáo · `Esc` đóng hộp thoại.
>
> Widget: bật ở trang **Widget** hoặc nút ghim ở góc dưới thanh bên. "Khởi động cùng Windows" tạo một tác vụ trong Task Scheduler (`PC Check Widget`) chạy `PcCheck.exe --widget` khi đăng nhập – chỉ hiện widget, không hỏi UAC. Mở PC Check lần nữa khi đang chạy sẽ hiện lại cửa sổ chính thay vì mở bản thứ hai.

## An toàn

- Mọi thao tác thay đổi hệ thống (dọn rác, gỡ phần mềm, tắt khởi động, SFC/DISM, reset mạng, khởi động lại) **đều hỏi xác nhận**.
- Dọn rác chỉ xóa trong các thư mục rác đã biết, bỏ qua tệp đang được dùng; tệp tạm chỉ xóa khi cũ hơn 24 giờ. Thùng rác, crash dump, shader cache, thumbnail **không được chọn sẵn**.
- Gỡ phần mềm dùng chính trình gỡ cài đặt của phần mềm đó. Phần mềm hệ thống / runtime / driver không bao giờ bị gợi ý gỡ.
- Phần còn sót chỉ bị xóa sau khi bạn xem và đồng ý: mục chắc chắn mới được đánh dấu sẵn; không bao giờ đụng tới thư mục Windows, thư mục gốc (Program Files, AppData…), thư mục tên hãng dùng chung (Microsoft, Google, Intel…) hay thư mục của phần mềm khác đang cài. Tệp/thư mục vào **Thùng rác**; khóa registry được **xuất ra tệp .reg** và tác vụ hẹn giờ được lưu bản XML trước khi xóa (thư mục `%LocalAppData%\PcCheck\Backups`); chỉ xóa dịch vụ khi tệp chạy của nó đã mất.
- Tắt khởi động dùng cơ chế giống Task Manager (khóa `StartupApproved`) – bật lại được bất cứ lúc nào.
- Lệnh nặng (SFC, DISM, reset Windows Update) mở cửa sổ riêng để theo dõi; kiểm tra ổ đĩa và quét virus chạy nền ngay trong ứng dụng.
- Trang Bảo mật chỉ **đọc** thông tin, không tự xóa gì; mọi thao tác (gỡ, tắt tự chạy, quét ngoại tuyến) đều hỏi xác nhận. Tra cứu VirusTotal chỉ mở trang theo mã băm SHA-256 – tệp không bị tải lên.
- Cập nhật chỉ tải từ GitHub Releases của dự án qua HTTPS, kiểm tra mã SHA-256 do GitHub cung cấp, giải nén duy nhất `PcCheck.exe` vào thư mục chỉ admin ghi được, khóa tệp và kiểm tra lại trước khi chạy. Bản cũ được giữ tới khi bản mới khởi động xong.
- Driver PawnIO chỉ được cài khi bạn bấm nút và xác nhận; tệp cài phải có chữ ký số hợp lệ, nếu không sẽ bị hủy. Gỡ: Settings → Apps → Installed apps → PawnIO.
- Log lỗi: `%LocalAppData%\PcCheck\logs`. Cài đặt: `%AppData%\PcCheck\settings.json`.

## Build từ mã nguồn

Yêu cầu: [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (Visual Studio 2026 hoặc VS Code / Rider).

```powershell
dotnet build PcCheck.sln -c Release
dotnet test PcCheck.sln
dotnet run --project src/PcCheck.App      # chạy terminal với quyền Administrator

# Đóng gói 1 file exe độc lập
dotnet publish src/PcCheck.App -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -p:PublishReadyToRun=true -o publish
```

Phần lõi và kiểm thử cũng build/chạy được trên Linux/macOS (`EnableWindowsTargeting`), tiện cho CI.

## Cấu trúc

```
src/PcCheck.Core/            Thư viện lõi (không phụ thuộc giao diện)
  Collectors/                Thu thập: phần cứng (WMI, Registry, Win32), driver, Event Log, phần mềm, khởi động, bảo mật
  Sensors/                   Cảm biến qua LibreHardwareMonitor
  Cleanup/                   Quét & dọn tệp rác; gỡ sạch (chạy trình gỡ, tìm và xóa an toàn phần còn sót)
  Analysis/                  Phân tích → vấn đề, kiểm tra tương thích, điểm sức khỏe (thuần logic, có kiểm thử)
  Fixes/                     Hành động khắc phục, công cụ hệ thống, quét Defender / kiểm tra ổ đĩa chạy nền
  Reporting/                 Xuất báo cáo HTML
  Localization/Lang/         vi.json, en.json – toàn bộ chuỗi giao diện
src/PcCheck.App/             Ứng dụng WPF (MVVM, không dùng thư viện UI ngoài)
tests/PcCheck.Core.Tests/    Kiểm thử xUnit (analyzer, bản dịch, tiện ích)
```

Thêm ngôn ngữ mới: tạo `Localization/Lang/<mã>.json` với cùng các khóa như `vi.json` và thêm mã vào `Loc.SupportedLanguages`. Kiểm thử sẽ báo nếu thiếu khóa.

## Giới hạn đã biết

- Ước lượng "lần dùng cuối" của phần mềm dựa trên lịch sử Windows (UserAssist/Prefetch) nên có phần mềm hiển thị "Chưa rõ lần dùng" – các phần mềm này không bị gợi ý gỡ.
- Kiểm tra CPU hỗ trợ Windows 11 là ước lượng theo tên CPU; trường hợp không chắc sẽ hiển thị "Không rõ".
- Không đo được công suất nguồn (PSU) bằng phần mềm.

## Thư viện bên thứ ba

- [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) – MPL-2.0
- System.Management, System.Diagnostics.EventLog – MIT (.NET)

## Tác giả

**vieetjk** – [github.com/vieetjk6-afk](https://github.com/vieetjk6-afk)

---

## English

**PC Check** is a Windows 10/11 PC diagnostics tool (C# / .NET 10 / WPF) with a modern dark/light, Vietnamese/English UI. It shows detailed hardware info, live temperatures and drive/battery health, checks component compatibility (RAM channel/speed/XMP, SSD vs HDD, display refresh rate, Windows 11 readiness), finds missing/failing/outdated drivers, cleans junk files, flags unused and unwanted software and removes them cleanly (batch, silent when supported, optional restore point, then finds leftover folders, registry keys, shortcuts, tasks and services for review – files go to the Recycle Bin, registry is exported first), manages startup apps, offers a draggable, collapsible, customizable always-on-top desktop widget (CPU/GPU/RAM/network/disk/per-device temperatures), looks for signs of spying (spyware/keyloggers, monitoring and remote-control tools, apps that used the camera/microphone, suspicious autostart entries and processes, browser extensions/policies/proxies, rogue root certificates, hosts redirects, Defender exclusions, hidden admin accounts, IFEO/Winlogon hijacks) with fix guides and in-app Defender quick/full/offline scans (elapsed time, stop button, threat count), checks GitHub Releases for new versions and updates itself in one click (SHA-256 verified, keeps the old copy on failure), shows a collapsible card per device with all of its sensors, explains every issue with common causes and step-by-step fixes, reads an estimated CPU temperature from ACPI thermal zones with no extra software (one click installs the signed PawnIO driver for exact readings), and diagnoses system errors (BSODs, unexpected shutdowns, WHEA, disk errors, GPU driver hangs…) with one-click fixes and an HTML report. Download the latest build from the **Actions** tab, or build with `dotnet build PcCheck.sln`. Author: **vieetjk**.
