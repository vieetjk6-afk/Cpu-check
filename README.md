# PC Check

Phần mềm kiểm tra máy tính Windows: xem thông tin phần cứng, theo dõi nhiệt độ – sức khỏe linh kiện, kiểm tra tương thích, phát hiện driver thiếu/lỗi, dọn rác, tìm phần mềm ít dùng và chẩn đoán lỗi hệ thống kèm gợi ý khắc phục 1 click.

- **Nền tảng:** Windows 10 / 11 (x64)
- **Công nghệ:** C# · .NET 10 (LTS) · WPF – ứng dụng native, nhẹ, khởi động nhanh
- **Giao diện:** hiện đại, tối/sáng, **song ngữ Tiếng Việt / English** (đổi ngay khi đang chạy)

## Tính năng

| Trang | Nội dung |
|---|---|
| **Tổng quan** | Điểm sức khỏe 0–100, tóm tắt hệ thống, giám sát CPU/GPU/RAM/ổ đĩa trực tiếp (biểu đồ mini), tình trạng từng hạng mục, các vấn đề quan trọng nhất |
| **Phần cứng** | CPU, mainboard/BIOS, từng thanh RAM (khe, bus, hãng, mã), card màn hình (VRAM, driver), ổ cứng (loại, bus, S.M.A.R.T, phân vùng), màn hình (độ phân giải, tần số quét), mạng, âm thanh, pin, UEFI/Secure Boot/TPM. Có nút sao chép toàn bộ thông tin |
| **Nhiệt độ & Sức khỏe** | Nhiệt độ CPU/GPU/ổ cứng, quạt, xung nhịp, điện áp, công suất – cập nhật mỗi 2 giây; sức khỏe ổ cứng (S.M.A.R.T, tuổi thọ SSD, giờ chạy, lỗi đọc/ghi); độ chai pin, báo cáo pin |
| **Tương thích** | RAM kênh đôi/đơn, RAM lệch dung lượng/bus, RAM chưa bật XMP/EXPO, khả năng nâng cấp RAM, Windows 32/64-bit, Windows chạy trên HDD hay SSD, màn hình chạy dưới tần số quét/độ phân giải tối đa, ảo hóa VT-x/AMD-V, xung đột nhiều phần mềm diệt virus, BIOS cũ, điều kiện nâng cấp Windows 11 (CPU, TPM 2.0, UEFI, Secure Boot) |
| **Driver** | Thiết bị chưa cài driver, thiết bị báo lỗi (giải thích mã lỗi Device Manager), đang dùng driver chung (Basic Display, HD Audio), driver không chữ ký, driver quá cũ; tìm driver theo Hardware ID, mở Driver tùy chọn của Windows Update, trang driver NVIDIA/AMD/Intel và hãng máy (Dell, HP, Lenovo, ASUS, Acer, MSI, Gigabyte…) |
| **Dọn dẹp** | **Tệp rác:** Temp, Windows Update cache, Delivery Optimization, báo cáo lỗi, log cũ, thumbnail, crash dump, shader cache, cache trình duyệt (Chrome, Edge, **Cốc Cốc**, Brave, Opera, Vivaldi, Firefox), Thùng rác. **Phần mềm:** ước lượng lần dùng cuối (UserAssist + Prefetch), đánh dấu phần mềm không dùng >90 ngày, phần mềm quảng cáo / "tối ưu hóa" giả / thanh công cụ / công cụ crack, app Store cài sẵn – gỡ bằng 1 nút. **Khởi động:** bật/tắt chương trình chạy cùng Windows |
| **Lỗi & Khắc phục** | Màn hình xanh (kèm tên mã lỗi và nguyên nhân thường gặp), tắt máy đột ngột, lỗi phần cứng WHEA, lỗi ổ cứng/NTFS, driver card màn hình bị treo, ứng dụng hay crash, thiếu RAM, Windows hết hạn hỗ trợ, chờ khởi động lại, chưa cập nhật, chưa kích hoạt, diệt virus/tường lửa/UAC tắt… Mỗi vấn đề có gợi ý và nút khắc phục. **Công cụ sửa nhanh:** SFC + DISM, CHKDSK, kiểm tra RAM, tạo điểm khôi phục, xóa DNS, reset mạng, sửa Windows Update, quét phần cứng, sửa Microsoft Store, báo cáo pin… |
| **Widget màn hình** | Bảng nhỏ nổi trên màn hình, **kéo thả tới bất kỳ đâu** (tự hít vào mép màn hình, nhớ vị trí, hỗ trợ nhiều màn hình). Tùy chọn thông số: CPU (% + xung), nhiệt độ CPU (+ công suất), GPU, nhiệt độ GPU (+ hot spot), VRAM, RAM, mạng (tải xuống/lên), ổ đĩa (% bận, đọc/ghi), nhiệt độ ổ, quạt, pin, thời gian chạy, đồng hồ. Tùy chỉnh: luôn ở trên cùng, khóa vị trí, độ đậm, kích thước nhỏ/vừa/lớn, bố cục dọc/ngang, biểu đồ, tần suất 1/2/5 giây, **khởi động cùng Windows**. Widget vẫn chạy khi đóng cửa sổ chính; nhấp đúp để mở lại, chuột phải để xem menu |
| **Báo cáo** | Xuất toàn bộ kết quả ra 1 tệp HTML (gửi cho khách/kỹ thuật viên, in ấn) – `Ctrl+S` |

## Tải về và chạy

1. Vào tab **Actions** của repo → chọn lần build mới nhất → tải artifact:
   - `PcCheck-portable-win-x64`: 1 file `PcCheck.exe` chạy ngay, **không cần cài gì** (~70 MB).
   - `PcCheck-small-win-x64`: bản nhỏ (~7 MB, 1 file), cần cài [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0).

   Khi tạo tag `v*` (ví dụ `v1.0.0`), workflow tự tạo GitHub Release kèm 2 tệp zip.
2. Chạy `PcCheck.exe` → đồng ý quyền quản trị (UAC). Ứng dụng tự quét lần đầu (5–15 giây).
3. **Nhiệt độ CPU:** cần driver [PawnIO](https://pawnio.eu/) (miễn phí, mã nguồn mở – thay thế WinRing0 đã bị Defender chặn). Nếu chưa cài, ứng dụng sẽ hiện nút cài đặt. Nhiệt độ GPU và ổ cứng đọc được mà không cần PawnIO.

> Phím tắt: `F5` quét lại · `Ctrl+S` xuất báo cáo · `Esc` đóng hộp thoại.
>
> Widget: bật ở trang **Widget** hoặc nút ghim ở góc dưới thanh bên. "Khởi động cùng Windows" tạo một tác vụ trong Task Scheduler (`PC Check Widget`) chạy `PcCheck.exe --widget` khi đăng nhập – chỉ hiện widget, không hỏi UAC. Mở PC Check lần nữa khi đang chạy sẽ hiện lại cửa sổ chính thay vì mở bản thứ hai.

## An toàn

- Mọi thao tác thay đổi hệ thống (dọn rác, gỡ phần mềm, tắt khởi động, SFC/DISM, reset mạng, khởi động lại) **đều hỏi xác nhận**.
- Dọn rác chỉ xóa trong các thư mục rác đã biết, bỏ qua tệp đang được dùng; tệp tạm chỉ xóa khi cũ hơn 24 giờ. Thùng rác, crash dump, shader cache, thumbnail **không được chọn sẵn**.
- Gỡ phần mềm dùng chính trình gỡ cài đặt của phần mềm đó. Phần mềm hệ thống / runtime / driver không bao giờ bị gợi ý gỡ.
- Tắt khởi động dùng cơ chế giống Task Manager (khóa `StartupApproved`) – bật lại được bất cứ lúc nào.
- Lệnh nặng (SFC, DISM, CHKDSK, reset Windows Update) mở cửa sổ riêng để theo dõi.
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
  Cleanup/                   Quét & dọn tệp rác
  Analysis/                  Phân tích → vấn đề, kiểm tra tương thích, điểm sức khỏe (thuần logic, có kiểm thử)
  Fixes/                     Hành động khắc phục, công cụ hệ thống, gỡ cài đặt
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

---

## English

**PC Check** is a Windows 10/11 PC diagnostics tool (C# / .NET 10 / WPF) with a modern dark/light, Vietnamese/English UI. It shows detailed hardware info, live temperatures and drive/battery health, checks component compatibility (RAM channel/speed/XMP, SSD vs HDD, display refresh rate, Windows 11 readiness), finds missing/failing/outdated drivers, cleans junk files, flags unused and unwanted software, manages startup apps, offers a draggable, customizable always-on-top desktop widget (CPU/GPU/RAM/network/disk/temperatures), and diagnoses system errors (BSODs, unexpected shutdowns, WHEA, disk errors, GPU driver hangs…) with one-click fixes and an HTML report. Download the latest build from the **Actions** tab, or build with `dotnet build PcCheck.sln`.
